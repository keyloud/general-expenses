using System.Security.Cryptography;
using System.Text;
using GeneralExpenses.Api.Auth;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api;

public record InvitationInfo(string Title, string ParticipantName);
public record InvitationCode(string Token);
public record JoinedReceipt(string ReceiptId, string Title);

public sealed class InvitationStore(ExpensesDbContext db, ICurrentUser current)
{
    private static ReceiptException Unavailable() => new(404, "Приглашение недействительно. Попросите новую ссылку у владельца.");
    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    // Все выдачи и принятия для одного чека идут по очереди. Два аккаунта
    // не смогут занять одно место, даже если откроют ссылку одновременно.
    private Task LockReceipt(string id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receipts WHERE \"Id\" = {id} FOR UPDATE", ct);

    public async Task<InvitationCode> Issue(string receiptId, string personId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockReceipt(receiptId, ct);
        var receipt = await db.Receipts.Include(r => r.Participants)
            .SingleOrDefaultAsync(r => r.Id == receiptId && r.OwnerUserId == current.Id, ct)
            ?? throw new ReceiptException(404, "Счёт не найден.");
        var person = receipt.Participants.SingleOrDefault(p => p.Id == personId)
            ?? throw new ReceiptException(404, "Участник не найден.");
        if (person.Finished || person.UserId.HasValue) throw new ReceiptException(409, "Место уже занято или подтверждено.");

        // В БД остаётся только хеш. Выпуск новой ссылки сразу отменяет предыдущую.
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        person.InviteHash = Digest(code);
        person.InviteExpiresAt = DateTimeOffset.UtcNow.AddDays(2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(code);
    }

    private IQueryable<ParticipantEntity> Find(string? code)
    {
        if (code is null || code.Length != 48 || !code.All(char.IsAsciiHexDigit)) throw Unavailable();
        var hash = Digest(code);
        return db.Set<ParticipantEntity>().Where(p => p.InviteHash == hash);
    }

    private static void Validate(ParticipantEntity person, long userId)
    {
        if (person.InviteExpiresAt <= DateTimeOffset.UtcNow || person.InviteExpiresAt is null ||
            ((person.Finished || person.UserId.HasValue) && person.UserId != userId)) throw Unavailable();
        if (userId <= 0) throw new ReceiptException(403, "Примите приглашение из своего Telegram-аккаунта.");
    }

    public async Task<InvitationInfo> ReadForSharing(string? code, CancellationToken ct)
    {
        // Повторная отправка использует тот же код. Имя и название берём из чека владельца.
        return await (from p in Find(code) join r in db.Receipts on p.ReceiptId equals r.Id
                      where r.OwnerUserId == current.Id && p.UserId == null && !p.Finished && p.InviteExpiresAt > DateTimeOffset.UtcNow
                      select new InvitationInfo(r.Title, p.Name)).SingleOrDefaultAsync(ct) ?? throw Unavailable();
    }

    public async Task<InvitationInfo> Preview(string? code, CancellationToken ct)
    {
        var data = await (from p in Find(code).AsNoTracking() join r in db.Receipts on p.ReceiptId equals r.Id
                          select new { Person = p, r.Title }).SingleOrDefaultAsync(ct) ?? throw Unavailable();
        Validate(data.Person, current.Id);
        return new(data.Title, data.Person.Name);
    }

    public Task<JoinedReceipt> Accept(string? code, CancellationToken ct) =>
        AcceptCore(code, current.Id, null, ct);

    // Эту точку входа вызывает только обработчик личного сообщения, полученного от Bot API.
    public Task<JoinedReceipt> AcceptFromChat(string code, VerifiedTelegramUser sender, CancellationToken ct) =>
        AcceptCore(code, sender.Id, sender, ct);

    private async Task<JoinedReceipt> AcceptCore(string? code, long userId, VerifiedTelegramUser? sender, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var query = Find(code);
        var id = await query.Select(p => p.ReceiptId).SingleOrDefaultAsync(ct) ?? throw Unavailable();
        await LockReceipt(id, ct);
        // Повторно читаем код под блокировкой: его могли заменить или уже использовать.
        var person = await query.SingleOrDefaultAsync(ct) ?? throw Unavailable();
        Validate(person, userId);
        var receipt = await db.Receipts.Include(r => r.Participants).SingleAsync(r => r.Id == id, ct);
        if (person.UserId != userId)
        {

            if (receipt.OwnerUserId == userId || receipt.Participants.Any(p => p.UserId == userId))
                throw new ReceiptException(409, "Вы уже участвуете в этом счёте.");
            if (sender is not null) await TelegramUsers.SaveAsync(db, sender, ct);
            person.UserId = userId;
            receipt.Revision++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(id, receipt.Title);
    }
}
