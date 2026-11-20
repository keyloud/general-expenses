using GeneralExpenses.Api.Auth;
using System.Data;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api;

// Scoped-сервис: один DbContext на HTTP-запрос. DbContext не потокобезопасен,
// поэтому его нельзя регистрировать Singleton, как прежнее хранилище в памяти.
public sealed class ReceiptStore(ExpensesDbContext db, ICurrentUser current)
{
    private IQueryable<ReceiptEntity> Graph => db.Receipts
        .Include(x => x.Participants).Include(x => x.Items).ThenInclude(x => x.Assignments).AsSplitQuery(); // AsSplitQuery разделяет один большой SQL на несколько SELECT, чтобы не размножать строки при JOIN нескольких коллекций.

    private IQueryable<ReceiptEntity> Visible => db.Receipts.Where(r =>
        r.OwnerUserId == current.Id || r.Participants.Any(p => p.UserId == current.Id));

    private ReceiptDto View(ReceiptEntity entity) => ReceiptMapping.ToDto(entity) with
    {
        MyParticipantId = entity.Participants.SingleOrDefault(p => p.UserId == current.Id)?.Id,
        IsOwner = entity.OwnerUserId == current.Id,
        CanSelectForOthers = current.CanSelectForOthers
    };

    public async Task<ReceiptDto> ReadAsync(CancellationToken ct = default, string receiptId = "demo")
    {
        // Несколько SELECT должны видеть одну версию данных даже при параллельной записи.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var entity = await Graph.AsNoTracking().Where(x => Visible.Any(r => r.Id == x.Id)).SingleOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new ReceiptException(404, "Счёт не найден.");
        var result = View(entity);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<ReceiptDto> ChangeAsync(Func<ReceiptState, ReceiptDto> change, CancellationToken ct, string receiptId, string? selectingFor = null, bool adminMode = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // FOR UPDATE блокирует строку чека в PostgreSQL до конца транзакции.
        // Это работает и при двух процессах API, в отличие от C# lock.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receipts WHERE \"Id\" = {receiptId} FOR UPDATE", ct);
        var entity = await Graph.Where(x => Visible.Any(r => r.Id == x.Id)).SingleOrDefaultAsync(x => x.Id == receiptId, ct) ?? throw new ReceiptException(404, "Счёт не найден.");
        if (selectingFor is null && entity.OwnerUserId != current.Id)
            throw new ReceiptException(403, "Это действие доступно владельцу счёта.");
        if (selectingFor is not null && !(adminMode && current.CanSelectForOthers) &&
            !entity.Participants.Any(p => p.UserId == current.Id && p.Id == selectingFor))
            throw new ReceiptException(403, "Участник может выбирать только свои блюда.");
        var before = ReceiptMapping.ToDto(entity);
        if (before.IsCompleted) throw new ReceiptException(409, "Счёт завершён. Итоги зафиксированы.");
        var actingForParticipant = selectingFor != null && adminMode && current.CanSelectForOthers;
        if (entity.Participants.Any(p => p.Finished && (actingForParticipant ? p.Id == selectingFor : p.UserId == current.Id)))
            throw new ReceiptException(409, "Выбор уже подтверждён. Участнику нужно нажать «Изменить чек».");
        if (entity.Participants.All(p => p.Finished))
            throw new ReceiptException(409, "Чтобы продолжить распределение, снимите подтверждение кнопкой «Изменить чек».");
        var state = new ReceiptState(before);
        var result = change(state); // Здесь проверяются revision и бизнес-правила.
        ReceiptMapping.Apply(entity, result);
        // Переподтверждают только те, у кого поменялись позиции, доли или суммы.
        foreach (var person in entity.Participants.Where(p => p.Finished))
            if (!ReceiptCalculator.Lines(before, person.Id).SequenceEqual(ReceiptCalculator.Lines(result, person.Id)))
            {
                person.Finished = false;
                person.ConfirmedByAdminId = null;
            }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // После изменения сохраняем в ответе и права текущего пользователя.
        return View(entity);
    }

    public Task<ReceiptDto> AddAsync(ItemRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Add(request), ct, receiptId);
    public Task<ReceiptDto> EditAsync(string id, ItemRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Edit(id, request), ct, receiptId);
    public Task<ReceiptDto> DeleteAsync(string id, ChangeRequest request, CancellationToken ct = default, string receiptId = "demo") => ChangeAsync(s => s.Delete(id, request), ct, receiptId);
    public Task<ReceiptDto> SelectAsync(string id, string personId, SelectionRequest request, CancellationToken ct = default, string receiptId = "demo") =>
        ChangeAsync(s => s.Select(id, personId, request), ct, receiptId, personId, request.AdminMode);
    public Task<ReceiptDto> ResetAsync(ChangeRequest request, CancellationToken ct = default, string receiptId = "demo") =>
        ChangeAsync(s => current.CanSelectForOthers ? s.Reset(request) : throw new ReceiptException(403, "Сброс общего выбора доступен администратору."), ct, receiptId);

    public Task<ReceiptDto> FinishAsync(string receiptId, ChangeRequest version, CancellationToken ct = default) =>
        SetFinishedAsync(receiptId, version, true, ct);

    public Task<ReceiptDto> ReopenAsync(string receiptId, ChangeRequest version, CancellationToken ct = default) =>
        SetFinishedAsync(receiptId, version, false, ct);

    public Task<ReceiptDto> AdminConfirmAsync(string receiptId, string? participantId, ChangeRequest version, bool finished, CancellationToken ct = default)
    {
        if (!current.CanSelectForOthers) throw new ReceiptException(403, "Доступно только администратору.");
        return SetFinishedAsync(receiptId, version, finished, ct, true, participantId);
    }

    private async Task<ReceiptDto> SetFinishedAsync(string receiptId, ChangeRequest version, bool finished, CancellationToken ct,
        bool asAdmin = false, string? participantId = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receipts WHERE \"Id\" = {receiptId} FOR UPDATE", ct);
        var entity = await Graph.Where(r => Visible.Any(v => v.Id == r.Id)).SingleOrDefaultAsync(r => r.Id == receiptId, ct)
            ?? throw new ReceiptException(404, "Счёт не найден.");
        // Администратор может работать и с местами без привязанного Telegram-аккаунта.
        var targets = entity.Participants.Where(p => asAdmin
            ? participantId == null || p.Id == participantId : p.UserId == current.Id).ToArray();
        if (targets.Length == 0) throw asAdmin ? new ReceiptException(404, "Участник не найден.")
            : new ReceiptException(403, "Для подтверждения займите своё место в счёте.");
        var before = View(entity);
        if (!finished && before.IsCompleted) throw new ReceiptException(409, "Счёт завершён. Итоги зафиксированы.");
        if (targets.Any(p => p.Finished != finished))
        {
            if (finished && before.Items.Length == 0) throw new ReceiptException(400, "В чеке пока нет позиций.");
            if (before.InstanceId != version.InstanceId || before.Revision != version.Revision)
                throw new ReceiptException(409, "Чек изменился. Проверьте актуальные суммы и повторите действие.");
            if (finished && before.Result.Unassigned > 0 && entity.Participants.All(p => p.Finished || targets.Contains(p)))
                throw new ReceiptException(409, "Перед последним подтверждением распределите остаток.");
            foreach (var person in targets.Where(p => p.Finished != finished))
            {
                person.Finished = finished;
                person.ConfirmedByAdminId = finished && asAdmin ? current.Id : null;
            }
            entity.Revision++;
            // Последнее подтверждение и очередь сообщений фиксируем под одной блокировкой.
            var after = View(entity);
            if (after.IsCompleted) await QueueSettlement(entity, after, ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return View(entity);
    }

    private async Task QueueSettlement(ReceiptEntity entity, ReceiptDto receipt, CancellationToken ct)
    {
        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.Id == entity.OwnerUserId, ct);
        var link = owner.Id <= 0 ? null : string.IsNullOrWhiteSpace(owner.Username)
            ? $"tg://user?id={owner.Id}" : $"https://t.me/{owner.Username}";
        var recipients = entity.Participants.OrderBy(p => p.Position)
            .GroupBy(p => p.ConfirmedByAdminId ?? p.UserId).Where(g => g.Key > 0);
        foreach (var group in recipients)
        {
            long chatId = group.Key!.Value;
            var messages = group.Any(p => p.ConfirmedByAdminId.HasValue)
                ? SettlementMessages.ForAdmin(receipt, group.Select(p => p.Id).ToArray())
                : SettlementMessages.For(receipt, group.Single().Id, chatId == owner.Id);
            foreach (var text in messages)
                db.Messages.Add(new ReceiptMessage { ReceiptId = entity.Id, ChatId = chatId,
                    Text = text, OwnerLink = link, NextAttempt = DateTimeOffset.UtcNow });
        }
    }

    public async Task<ReceiptDto> IdentifyOwnerAsync(string receiptId, string personId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Та же блокировка, что при принятии приглашения: место может занять только один аккаунт.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receipts WHERE \"Id\" = {receiptId} FOR UPDATE", ct);
        var entity = await Graph.SingleOrDefaultAsync(r => r.Id == receiptId && r.OwnerUserId == current.Id, ct)
            ?? throw new ReceiptException(404, "Счёт не найден.");
        var person = entity.Participants.SingleOrDefault(p => p.Id == personId)
            ?? throw new ReceiptException(404, "Участник не найден.");
        if (person.UserId != current.Id)
        {
            if (person.Finished || person.UserId.HasValue || entity.Participants.Any(p => p.UserId == current.Id))
                throw new ReceiptException(409, "Место уже закреплено. Обновите чек.");
            person.UserId = current.Id;
            person.InviteHash = null;
            person.InviteExpiresAt = null;
            entity.Revision++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return View(entity);
    }

    public Task<ReceiptSummaryDto[]> ListAsync(CancellationToken ct = default) => Visible.AsNoTracking()
        .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => new ReceiptSummaryDto(x.Id, x.Title, x.CreatedAt, x.Participants.Count,
            x.Items.Count, x.Items.Sum(i => i.Amount),
            x.OwnerUserId == current.Id, x.InstanceId, x.Revision))
        .ToArrayAsync(ct);

    public async Task DeleteReceiptAsync(string id, ChangeRequest version, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Внешние ключи PostgreSQL в том же DELETE удаляют позиции, участников и назначения.
        var owned = db.Receipts.Where(r => r.Id == id && r.OwnerUserId == current.Id);
        // Удаление счёта каскадно удаляет его очередь. Даём боту сначала передать итоги.
        var removed = await owned.Where(r => r.InstanceId == version.InstanceId && r.Revision == version.Revision &&
            !db.Messages.Any(m => m.ReceiptId == r.Id))
            .ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            if (await owned.AnyAsync(r => db.Messages.Any(m => m.ReceiptId == r.Id), ct))
                throw new ReceiptException(409, "Бот отправляет итоги. Удалить счёт можно после рассылки.");
            if (await owned.AnyAsync(ct))
                throw new ReceiptException(409, "Счёт изменился. Обновите список и подтвердите удаление ещё раз.");
            throw new ReceiptException(404, "Счёт не найден.");
        }
        db.DeletedReceipts.Add(new() { Id = id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

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
            if (existing.OwnerUserId != current.Id) throw new ReceiptException(404, "Счёт не найден.");
            var sameNames = existing.Participants.OrderBy(x => x.Position).Select(x => x.Name).SequenceEqual(names);
            if (existing.Title != title || !sameNames) throw new ReceiptException(409, "Этот запрос уже создал другой счёт. Обновите список.");
            await transaction.CommitAsync(ct);
            return View(existing);
        }
        if (await db.DeletedReceipts.AnyAsync(r => r.Id == id, ct))
            throw new ReceiptException(409, "Этот счёт уже удалён. Создайте новый счёт.");
        var people = names.Select(name => new Participant(Guid.NewGuid().ToString(), name,
            System.Globalization.StringInfo.GetNextTextElement(name))).ToArray();
        var snapshot = new ReceiptDto(people, [], [], ReceiptCalculator.Calculate([], new Dictionary<string, string[]>(), people),
            0, Guid.NewGuid().ToString(), id, title, DateTimeOffset.UtcNow);
        var entity = ReceiptMapping.Create(snapshot);
        entity.OwnerUserId = current.Id;
        db.Receipts.Add(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(entity);
    }
    public static async Task InitializeAsync(ExpensesDbContext db, ReceiptDto? import = null, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Строки ещё может не быть, поэтому начальное создание защищаем advisory lock.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74219831)", ct);
        // После удаления последнего чека перезапуск должен оставить список пустым.
        if (!await db.Receipts.AnyAsync(ct) && !await db.DeletedReceipts.AnyAsync(ct))
        {
            var snapshot = import ?? new ReceiptState().Read();
            var entity = ReceiptMapping.Create(snapshot);
            entity.OwnerUserId = -1;
            db.Receipts.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        else if (import is not null)
            throw new InvalidOperationException("Импорт разрешён только в пустую базу; существующий чек не изменён.");
        await transaction.CommitAsync(ct);
    }
}
