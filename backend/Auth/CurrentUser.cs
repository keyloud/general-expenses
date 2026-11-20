using System.Security.Claims;

namespace GeneralExpenses.Api.Auth;

// Владелец счёта берётся только из проверенной серверной сессии.
public interface ICurrentUser { long Id { get; } bool CanSelectForOthers => false; }
public sealed class CurrentUser(IHttpContextAccessor accessor, IConfiguration configuration) : ICurrentUser
{
    // Username может перейти другому человеку. Право закреплено за числовым Telegram ID.
    public bool CanSelectForOthers => Id > 0 && Id == configuration.GetValue<long?>("Telegram:AdminUserId");
    public long Id => long.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ReceiptException(401, "Необходимо войти.");
}
