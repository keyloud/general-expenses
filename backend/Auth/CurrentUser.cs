using System.Security.Claims;

namespace GeneralExpenses.Api.Auth;

// Владелец счёта берётся только из проверенной серверной сессии.
public interface ICurrentUser { long Id { get; } }
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public long Id => long.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ReceiptException(401, "Необходимо войти.");
}
