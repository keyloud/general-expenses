using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeneralExpenses.Api.Auth;

namespace GeneralExpenses.Api;

// Telegram отдаёт команды исходящему HTTPS-запросу. Публичный webhook для этого не нужен.
public sealed class TelegramChatBot(
    IHttpClientFactory clients, IServiceScopeFactory scopes, IConfiguration config, ILogger<TelegramChatBot> log)
    : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private sealed record Account(long Id, bool IsBot, string FirstName, string? LastName, string? Username);
    private sealed record Chat(long Id, string Type);
    private sealed record Message(Chat Chat, Account? From, string? Text);
    private sealed record Update(long UpdateId, Message? Message);
    private sealed record Batch(bool Ok, Update[] Result);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var client = clients.CreateClient("TelegramBot");
        var token = config["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Заполните Telegram:BotToken.");
        var app = new Uri(config["Telegram:MiniAppUrl"]!, UriKind.Absolute);
        if (app.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Для кнопки чека нужен HTTPS-адрес.");
        client.BaseAddress = new Uri($"https://api.telegram.org/bot{token}/");
        long offset = 0;
        var connected = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await client.PostAsJsonAsync("getUpdates",
                    new { offset, timeout = 25, allowed_updates = new[] { "message" } }, ct);
                response.EnsureSuccessStatusCode();
                var updates = await response.Content.ReadFromJsonAsync<Batch>(Json, ct);
                if (updates?.Ok != true) throw new HttpRequestException("Telegram не вернул обновления.");
                if (!connected)
                {
                    log.LogInformation("Связь с Telegram установлена, бот принимает команды.");
                    connected = true;
                }
                foreach (var update in updates.Result)
                {
                    if (update.Message is { } msg) await Reply(client, app, msg, ct);
                    // Подтверждаем сообщение только после обработки. При временном сбое
                    // повторное принятие тем же аккаунтом сохраняет одну запись в чеке.
                    offset = update.UpdateId + 1;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // URL Bot API содержит секрет. Не пишем URL, содержимое команды или тело ответа.
                log.LogWarning("Не удалось обработать Telegram update ({Type}, HTTP {Status}). Повтор через 5 секунд.",
                    error.GetType().Name, (error as HttpRequestException)?.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task Reply(HttpClient client, Uri app, Message message, CancellationToken ct)
    {
        var sender = message.From;
        if (message.Chat.Type != "private" || sender is null || sender.IsBot ||
            sender.Id <= 0 || sender.Id != message.Chat.Id || message.Text is null) return;
        var command = message.Text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (command.Length == 0 || command[0] != "/start") return;

        var url = app.AbsoluteUri;
        var label = "Мои счета";
        var text = "Здесь появятся подтверждения приглашений. Откройте свои счета или перейдите по персональной ссылке от друга.";
        if (command.Length == 2)
        {
            const string prefix = "invite_";
            if (!command[1].StartsWith(prefix, StringComparison.Ordinal))
                text = "Неизвестное приглашение. Попросите владельца прислать новую ссылку.";
            else
            {
                await using var scope = scopes.CreateAsyncScope();
                var invitations = scope.ServiceProvider.GetRequiredService<InvitationStore>();
                try
                {
                    var user = new VerifiedTelegramUser(sender.Id, (sender.FirstName + " " + sender.LastName).Trim(), sender.Username);
                    var joined = await invitations.AcceptFromChat(command[1][prefix.Length..], user, ct);
                    text = $"Чек «{joined.Title}» добавлен в ваши счета.";
                    label = "Открыть чек";
                    url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(app.AbsoluteUri, "receipt", joined.ReceiptId);
                }
                catch (ReceiptException e) { text = e.Message; }
            }
        }

        using var sent = await client.PostAsJsonAsync("sendMessage", new {
            chat_id = sender.Id, text,
            reply_markup = new { inline_keyboard = new[] { new[] { new { text = label, web_app = new { url } } } } }
        }, ct);
        // Пользователь мог заблокировать бота, пока тот работал с БД.
        if (sent.StatusCode == HttpStatusCode.Forbidden) return;
        if ((int)sent.StatusCode == 429)
        {
            using var body = JsonDocument.Parse(await sent.Content.ReadAsStringAsync(ct));
            if (body.RootElement.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var delay))
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(delay.GetInt32(), 1)), ct);
        }
        sent.EnsureSuccessStatusCode();
    }
}
