using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GeneralExpenses.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api.Auth;

[ApiController, Route("api/auth")]
public sealed class AuthController(ExpensesDbContext db, IConfiguration configuration, ICurrentUser current) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == current.Id, ct);
        return user is null ? Unauthorized() : Ok(new { id = user.Id.ToString(), user.Name, user.Username, isLocal = user.Id == -1 });
    }

    [AllowAnonymous, HttpPost("telegram")]
    public async Task<IActionResult> Telegram(TelegramLogin request, CancellationToken ct)
    {
        var user = TelegramInitData.Validate(request.InitData!, configuration["Telegram:BotToken"] ?? "", DateTimeOffset.UtcNow);
        if (user is null) return Problem(statusCode: 401, title: "Не удалось подтвердить вход. Закройте приложение и откройте его заново в Telegram.");
        // UPSERT делает два одновременных входа одного пользователя безопасными.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users ("Id", "Name", "Username") VALUES ({user.Id}, {user.Name}, {user.Username})
            ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "Username" = EXCLUDED."Username"
            """, ct);
        await SignIn(user.Id);
        return Ok(new { id = user.Id.ToString(), user.Name, user.Username, isLocal = false });
    }

    [AllowAnonymous, HttpPost("local")]
    public async Task<IActionResult> Local(LocalLogin request, CancellationToken ct)
    {
        var expected = configuration["Auth:LocalKey"];
        // Нет ключа на сервере — локальный вход выключен. localhost сам по себе не даёт прав.
        if (string.IsNullOrEmpty(expected) || expected.Length < 32 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Key!))))
            return Problem(statusCode: 401, title: "Неверный ключ локального входа.");
        await SignIn(-1);
        return await MeForLocal(ct);
    }

    private async Task<IActionResult> MeForLocal(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == -1, ct);
        return Ok(new { id = "-1", user.Name, user.Username, isLocal = true });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { signedOut = true });
    }

    private Task SignIn(long id) => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = false });
}

public sealed class TelegramLogin { [Required, StringLength(16384)] public string? InitData { get; init; } }
public sealed class LocalLogin { [Required, StringLength(256)] public string? Key { get; init; } }
