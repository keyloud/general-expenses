using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GeneralExpenses.Api.Auth;

public record VerifiedTelegramUser(long Id, string Name, string? Username);

// Проверяем исходную строку initData, а не доверяем присланным отдельно id и имени.
public static class TelegramInitData
{
    public static VerifiedTelegramUser? Validate(string raw, string token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(raw) || raw.Length > 16384) return null;
        try
        {
            var fields = QueryHelpers.ParseQuery(raw);
            if (fields.Any(x => x.Value.Count != 1) ||
                !fields.TryGetValue("hash", out var hash) || hash.ToString().Length != 64 ||
                !fields.TryGetValue("auth_date", out var date) ||
                !long.TryParse(date.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return null;
            var issued = DateTimeOffset.FromUnixTimeSeconds(seconds);
            // При входе принимаем данные не старше пяти минут; сессия затем живёт отдельно.
            if (issued < now.AddMinutes(-5) || issued > now.AddSeconds(30)) return null;
            var check = string.Join("\n", fields.Where(x => x.Key != "hash")
                .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
            var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
            var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check));
            if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash.ToString()))) return null;
            if (!fields.TryGetValue("user", out var user)) return null;
            using var json = JsonDocument.Parse(user.ToString());
            var root = json.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value) || value <= 0 ||
                !root.TryGetProperty("first_name", out var first) || first.ValueKind != JsonValueKind.String) return null;
            var name = first.GetString()!;
            if (root.TryGetProperty("last_name", out var last) && last.ValueKind == JsonValueKind.String) name += " " + last.GetString();
            string? username = root.TryGetProperty("username", out var handle) && handle.ValueKind == JsonValueKind.String ? handle.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || username?.Length > 64) return null;
            return new(value, name, username);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null; // Некорректный ввод — отказ во входе, а не ошибка сервера.
        }
    }
}
