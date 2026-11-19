using System.Security.Cryptography;
using System.Text;
using GeneralExpenses.Api.Auth;
using Xunit;

public sealed class TelegramAuthTests
{
    private const string Token = "123456:test-token-only";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);

    private static string Signed(string user = """{"id":5000000001,"first_name":"Анна + &","last_name":"Иванова","username":"anna"}""",
        long time = 1800000000, string? signature = null)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = time.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["query_id"] = "test-query",
            ["user"] = user
        };
        if (signature is not null) fields["signature"] = signature;
        using var secret = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        using var hmac = new HMACSHA256(secret.ComputeHash(Encoding.UTF8.GetBytes(Token)));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"))))).ToLowerInvariant();
        return string.Join("&", fields.Reverse().Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + hash;
    }

    [Fact]
    public void AcceptsSignedUnicodeLargeIdAndAdditionalSignature()
    {
        var user = TelegramInitData.Validate(Signed(signature: "additional-ed25519-signature"), Token, Now);
        Assert.NotNull(user);
        Assert.Equal(5000000001, user.Id);
        Assert.Equal("Анна + & Иванова", user.Name);
        Assert.Equal("anna", user.Username);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(31)]
    public void RejectsExpiredOrFutureData(int offset) =>
        Assert.Null(TelegramInitData.Validate(Signed(time: Now.ToUnixTimeSeconds() + offset), Token, Now));

    [Fact]
    public void RejectsTamperingWrongTokenAndDuplicateFields()
    {
        var raw = Signed();
        Assert.Null(TelegramInitData.Validate(raw.Replace("5000000001", "5000000002"), Token, Now));
        Assert.Null(TelegramInitData.Validate(raw, "different-bot-token", Now));
        Assert.Null(TelegramInitData.Validate(raw, "", Now));
        Assert.Null(TelegramInitData.Validate(raw + "&auth_date=1800000000", Token, Now));
        Assert.Null(TelegramInitData.Validate(raw + "&HASH=" + new string('a', 64), Token, Now));
        Assert.Null(TelegramInitData.Validate(raw[..^64] + new string('z', 64), Token, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"id":-1,"first_name":"Fake local user"}""")]
    [InlineData("""{"id":"123","first_name":"Bad type"}""")]
    [InlineData("""{"id":123,"first_name":null}""")]
    [InlineData("""{"id":123,"first_name":" "}""")]
    public void RejectsMalformedSignedUserWithoutCrashing(string user) =>
        Assert.Null(TelegramInitData.Validate(Signed(user), Token, Now));
}
