using System.Data;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api;

// Scoped-сервис: один DbContext на HTTP-запрос. DbContext не потокобезопасен,
// поэтому его нельзя регистрировать Singleton, как прежнее хранилище в памяти.
public sealed class ReceiptStore(ExpensesDbContext db)
{
    private IQueryable<ReceiptEntity> Graph => db.Receipts
        .Include(x => x.Participants).Include(x => x.Items).ThenInclude(x => x.Assignments).AsSplitQuery(); // AsSplitQuery разделяет один большой SQL на несколько SELECT, чтобы избежать переполнения буфера PostgreSQL.

    public async Task<ReceiptDto> ReadAsync(CancellationToken ct = default, string receiptId = "demo")
    {
        // Несколько SELECT должны видеть одну версию данных даже при параллельной записи.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var entity = await Graph.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new ReceiptException(404, "Счёт не найден.");
        var result = ReceiptMapping.ToDto(entity);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<ReceiptDto> ChangeAsync(Func<ReceiptState, ReceiptDto> change, CancellationToken ct, string receiptId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // FOR UPDATE блокирует строку чека в PostgreSQL до конца транзакции.
        // Это работает и при двух процессах API, в отличие от C# lock.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receipts WHERE \"Id\" = {receiptId} FOR UPDATE", ct);
        var entity = await Graph.SingleOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new ReceiptException(404, "Счёт не найден.");
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

    public Task<ReceiptDto> AddAsync(ItemRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Add(request), ct, receiptId);
    public Task<ReceiptDto> EditAsync(string id, ItemRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Edit(id, request), ct, receiptId);
    public Task<ReceiptDto> DeleteAsync(string id, ChangeRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Delete(id, request), ct, receiptId);
    public Task<ReceiptDto> UndoAsync(ChangeRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Undo(request), ct, receiptId);
    public Task<ReceiptDto> SelectAsync(string id, string personId, SelectionRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Select(id, personId, request), ct, receiptId);
    public Task<ReceiptDto> ResetAsync(ChangeRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Reset(request), ct, receiptId);

    public Task<ReceiptSummaryDto[]> ListAsync(CancellationToken ct = default) => db.Receipts.AsNoTracking()
        .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => new ReceiptSummaryDto(x.Id, x.Title, x.CreatedAt, x.Participants.Count,
            x.Items.Count(i => !i.IsDeleted), x.Items.Where(i => !i.IsDeleted).Sum(i => i.Amount)))
        .ToArrayAsync(ct);

    public async Task<ReceiptDto> CreateAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        var title = request.Title?.Trim();
        var names = request.Participants?.Select(x => x?.Trim() ?? "").ToArray() ?? [];
        if (request.RequestId is null || request.RequestId == Guid.Empty || string.IsNullOrEmpty(title) || title.Length > 100
            || names.Length is < 1 or > 12 || names.Any(x => x.Length is < 1 or > 100)
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ReceiptException(400, "Введите название и от 1 до 12 разных имён участников (до 100 символов каждое).");

        // Клиент сохраняет RequestId до успешного ответа. Повтор после потери связи
        // возвращает уже созданный счёт, а не создаёт второй. SQL параметризован.
        var id = request.RequestId.Value.ToString();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id}, 0))", ct);
        var existing = await Graph.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (existing is not null)
        {
            var sameNames = existing.Participants.OrderBy(x => x.Position).Select(x => x.Name).SequenceEqual(names);
            if (existing.Title != title || !sameNames) throw new ReceiptException(409, "Этот запрос уже создал другой счёт. Обновите список.");
            await transaction.CommitAsync(ct);
            return ReceiptMapping.ToDto(existing);
        }
        var people = names.Select(name => new Participant(Guid.NewGuid().ToString(), name,
            System.Globalization.StringInfo.GetNextTextElement(name))).ToArray();
        var snapshot = new ReceiptDto(people, [], [], ReceiptCalculator.Calculate([], new Dictionary<string, string[]>(), people),
            0, Guid.NewGuid().ToString(), null, id, title, DateTimeOffset.UtcNow);
        db.Receipts.Add(ReceiptMapping.Create(snapshot));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return snapshot;
    }
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
