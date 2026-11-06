using System.Data;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api;

// Scoped-сервис: один DbContext на HTTP-запрос. DbContext не потокобезопасен,
// поэтому его нельзя регистрировать Singleton, как прежнее хранилище в памяти.
public sealed class ReceiptStore(ExpensesDbContext db)
{
    private IQueryable<ReceiptEntity> Graph => db.Receipts
        .Include(x => x.Participants).Include(x => x.Items).ThenInclude(x => x.Assignments).AsSplitQuery();

    public async Task<ReceiptDto> ReadAsync(CancellationToken ct = default)
    {
        // Несколько SELECT должны видеть одну версию данных даже при параллельной записи.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var entity = await Graph.AsNoTracking().SingleAsync(x => x.Id == "demo", ct);
        var result = ReceiptMapping.ToDto(entity);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<ReceiptDto> ChangeAsync(Func<ReceiptState, ReceiptDto> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // FOR UPDATE блокирует строку чека в PostgreSQL до конца транзакции.
        // Это работает и при двух процессах API, в отличие от C# lock.
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM receipts WHERE \"Id\" = 'demo' FOR UPDATE", ct);
        var entity = await Graph.SingleAsync(x => x.Id == "demo", ct);
        var state = new ReceiptState(ReceiptMapping.ToDto(entity));
        var result = change(state); // Здесь проверяются revision и бизнес-правила.
        var previousDeleted = entity.Items.SingleOrDefault(x => x.IsDeleted);
        if (previousDeleted is not null && result.Deleted?.Item.Id != previousDeleted.Id
            && result.Items.All(x => x.Id != previousDeleted.Id))
        {
            // При втором удалении освобождаем уникальный слот отмены до UPDATE
            // следующего блюда. Оба SaveChanges входят в одну транзакцию:
            // читатели не увидят промежуточное состояние, ошибка откатит всё.
            entity.Items.Remove(previousDeleted);
            await db.SaveChangesAsync(ct);
        }
        ReceiptMapping.Apply(entity, result);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // Ответ отправляется только после COMMIT: успех означает сохранение в БД.
        return result;
    }

    public Task<ReceiptDto> AddAsync(ItemRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Add(request), ct);
    public Task<ReceiptDto> EditAsync(string id, ItemRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Edit(id, request), ct);
    public Task<ReceiptDto> DeleteAsync(string id, ChangeRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Delete(id, request), ct);
    public Task<ReceiptDto> UndoAsync(ChangeRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Undo(request), ct);
    public Task<ReceiptDto> SelectAsync(string id, string personId, SelectionRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Select(id, personId, request), ct);
    public Task<ReceiptDto> ResetAsync(ChangeRequest request, CancellationToken ct = default) => ChangeAsync(s => s.Reset(request), ct);

    public static async Task InitializeAsync(ExpensesDbContext db, ReceiptDto? import = null, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Строки ещё может не быть, поэтому начальное создание защищаем advisory lock.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74219831)", ct);
        if (!await db.Receipts.AnyAsync(ct))
        {
            var snapshot = import ?? new ReceiptState().Read();
            db.Receipts.Add(ReceiptMapping.Create(snapshot));
            await db.SaveChangesAsync(ct);
        }
        else if (import is not null)
            throw new InvalidOperationException("Импорт разрешён только в пустую базу; существующий чек не изменён.");
        await transaction.CommitAsync(ct);
    }
}
