using System.Net;
using System.Text.Json;
using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api;

public sealed class SettlementSender(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IConfiguration configuration, ILogger<SettlementSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var client = clients.CreateClient("TelegramBot");
        client.BaseAddress = new Uri($"https://api.telegram.org/bot{configuration["Telegram:BotToken"]}/");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
                if (await DeliverNext(db, client, ct)) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogWarning("Повтор доставки итога после ошибки {Type}.", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
        }
    }

    public async Task<bool> DeliverNext(ExpensesDbContext db, HttpClient client, CancellationToken ct)
    {
        // Отправляем зафиксированный снимок. При перезапуске задания остаются в БД,
        // а SKIP LOCKED не даёт двум экземплярам одновременно взять одно сообщение.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Messages.FromSqlRaw("""
            SELECT m.* FROM receipt_messages m WHERE m."NextAttempt" <= CURRENT_TIMESTAMP
            AND NOT EXISTS (SELECT 1 FROM receipt_messages p WHERE p."ChatId" = m."ChatId" AND p."Id" < m."Id")
            ORDER BY m."Id" LIMIT 1 FOR UPDATE OF m SKIP LOCKED
            """).ToListAsync(ct);
        var msg = rows.SingleOrDefault();
        if (msg is null) return false;
        var keyboard = msg.OwnerLink is null ? null : new { inline_keyboard = new[] { new[] {
            new { text = "Написать создателю счёта", url = msg.OwnerLink } } } };
        using var response = await client.PostAsJsonAsync("sendMessage", new { chat_id = msg.ChatId, text = msg.Text, reply_markup = keyboard }, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Forbidden)
            db.Messages.Remove(msg);
        else if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // Telegram может отклонить кнопку профиля из-за приватности. Сохраняем сам итог.
            if (msg.OwnerLink != null)
            {
                msg.Text += "\nСоздатель счёта: " + msg.OwnerLink;
                msg.OwnerLink = null;
            }
            else
            {
                log.LogWarning("Telegram отклонил доставку итогового сообщения {Id}.", msg.Id);
                db.Messages.Remove(msg);
            }
        }
        else
        {
            var delay = 60;
            if ((int)response.StatusCode == 429)
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (error.RootElement.TryGetProperty("parameters", out var parameters) &&
                    parameters.TryGetProperty("retry_after", out var seconds)) delay = Math.Max(1, seconds.GetInt32());
            }
            msg.NextAttempt = DateTimeOffset.UtcNow.AddSeconds(delay);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
