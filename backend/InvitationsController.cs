using Microsoft.AspNetCore.Mvc;

namespace GeneralExpenses.Api;

[ApiController]
public sealed class InvitationsController(InvitationStore invites) : ControllerBase
{
    [HttpPost("api/receipts/{receiptId}/participants/{personId}/invite")]
    public Task<ActionResult<InvitationCode>> Issue(string receiptId, string personId, CancellationToken ct) =>
        Execute(() => invites.Issue(receiptId, personId, ct));

    // Передаём секрет в теле запроса: путь запроса попадает в журналы сервера.
    [HttpPost("api/invitations/preview")]
    public Task<ActionResult<InvitationInfo>> Preview(InvitationCode input, CancellationToken ct) =>
        Execute(() => invites.Preview(input.Token, ct));

    [HttpPost("api/invitations/accept")]
    public Task<ActionResult<JoinedReceipt>> Accept(InvitationCode input, CancellationToken ct) =>
        Execute(() => invites.Accept(input.Token, ct));

    [HttpPost("api/invitations/share")]
    public Task<ActionResult<PreparedInvitation>> Share(InvitationCode input, [FromServices] TelegramInvitationShare telegram,
        CancellationToken ct) => Execute(async () =>
    {
        var invitation = await invites.ReadForSharing(input.Token, ct);
        return await telegram.Prepare(input.Token, invitation, ct);
    });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ReceiptException error) { return Problem(statusCode: error.Status, title: error.Message); }
    }
}
