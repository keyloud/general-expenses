using System.Text.Json;
using GeneralExpenses.Api.Auth;

namespace GeneralExpenses.Api;

public record PreparedInvitation(string Id);

public sealed class TelegramInvitationShare(IHttpClientFactory clients, IConfiguration config, ICurrentUser current)
{
    private sealed record TelegramResponse(bool Ok, PreparedInvitation? Result);

    public async Task<PreparedInvitation> Prepare(string code, InvitationInfo invite, CancellationToken ct)
    {
        var ownerId = current.Id;
        if (ownerId <= 0) throw new ReceiptException(400, "Откройте чек в Telegram для выбора получателя.");
        var token = config["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new ReceiptException(503, "Отправка через Telegram недоступна.");

        var url = $"https://t.me/porovnu_expenses_bot?start=invite_{code}";
        var payload = new
        {
            // Это ID отправителя из сессии. Получателя он выберет в самом Telegram.
            user_id = ownerId,
            allow_user_chats = true, allow_group_chats = false, allow_channel_chats = false, allow_bot_chats = false,
            result = new
            {
                type = "article", id = Guid.NewGuid().ToString("N"), title = "Приглашение в Поровну",
                input_message_content = new { message_text = $"Приглашение для {invite.ParticipantName} в счёт «{invite.Title}»." },
                reply_markup = new { inline_keyboard = new[] { new[] { new { text = "Присоединиться к счёту", url } } } }
            }
        };
        using var client = clients.CreateClient("TelegramBot");
        try
        {
            using var response = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/savePreparedInlineMessage", payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<TelegramResponse>(ct);
                if (body is { Ok: true, Result.Id.Length: > 0 }) return body.Result;
            }
        }
        // Ошибки сети могут содержать URL с токеном. Возвращаем безопасный текст.
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (JsonException) { }
        throw new ReceiptException(502, "Telegram не подготовил приглашение. Попробуйте ещё раз.");
    }
}
