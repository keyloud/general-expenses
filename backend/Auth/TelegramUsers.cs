using GeneralExpenses.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api.Auth;

internal static class TelegramUsers
{
    internal static async Task SaveAsync(ExpensesDbContext db, VerifiedTelegramUser user, CancellationToken ct)
    {
        // UPSERT делает два одновременных входа одного пользователя безопасными.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users ("Id", "Name", "Username") VALUES ({user.Id}, {user.Name}, {user.Username})
            ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "Username" = EXCLUDED."Username"
            """, ct);
    }
}
