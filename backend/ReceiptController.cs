using Microsoft.AspNetCore.Mvc;
namespace GeneralExpenses.Api;

[ApiController]
[Route("api/receipts/{receiptId}")]
public sealed class ReceiptController(ReceiptStore store) : ControllerBase
{
    // receiptId берётся из URL. Любая операция ограничена конкретным счётом.
    [HttpGet] public Task<ActionResult<ReceiptDto>> Get(string receiptId) => Execute(() => store.ReadAsync(HttpContext.RequestAborted, receiptId));
    [HttpPost("finish")] public Task<ActionResult<ReceiptDto>> Finish(string receiptId, ChangeRequest version) =>
        Execute(() => store.FinishAsync(receiptId, version, HttpContext.RequestAborted));
    [HttpPost("reopen")] public Task<ActionResult<ReceiptDto>> Reopen(string receiptId, ChangeRequest version) =>
        Execute(() => store.ReopenAsync(receiptId, version, HttpContext.RequestAborted));
    [HttpPost("admin/participants/{personId}/finish")] public Task<ActionResult<ReceiptDto>> AdminFinish(string receiptId, string personId, ChangeRequest version) =>
        Execute(() => store.AdminConfirmAsync(receiptId, personId, version, true, HttpContext.RequestAborted));
    [HttpPost("admin/participants/{personId}/reopen")] public Task<ActionResult<ReceiptDto>> AdminReopen(string receiptId, string personId, ChangeRequest version) =>
        Execute(() => store.AdminConfirmAsync(receiptId, personId, version, false, HttpContext.RequestAborted));
    [HttpPost("admin/finish-all")] public Task<ActionResult<ReceiptDto>> FinishAll(string receiptId, ChangeRequest version) =>
        Execute(() => store.AdminConfirmAsync(receiptId, null, version, true, HttpContext.RequestAborted));
    [HttpPut("me/{personId}")] public Task<ActionResult<ReceiptDto>> IdentifyOwner(string receiptId, string personId) =>
        Execute(() => store.IdentifyOwnerAsync(receiptId, personId, HttpContext.RequestAborted));
    [HttpPost("items")] public Task<ActionResult<ReceiptDto>> Add(string receiptId, ItemRequest request) => Execute(() => store.AddAsync(request, HttpContext.RequestAborted, receiptId));
    [HttpPut("items/{id}")] public Task<ActionResult<ReceiptDto>> Edit(string receiptId, string id, ItemRequest request) => Execute(() => store.EditAsync(id, request, HttpContext.RequestAborted, receiptId));
    [HttpDelete("items/{id}")] public Task<ActionResult<ReceiptDto>> Delete(string receiptId, string id, [FromBody] ChangeRequest request) => Execute(() => store.DeleteAsync(id, request, HttpContext.RequestAborted, receiptId));
    [HttpPut("items/{id}/participants/{personId}")] public Task<ActionResult<ReceiptDto>> Select(string receiptId, string id, string personId, SelectionRequest request) => Execute(() => store.SelectAsync(id, personId, request, HttpContext.RequestAborted, receiptId));
    [HttpPost("reset")] public Task<ActionResult<ReceiptDto>> Reset(string receiptId, ChangeRequest request) => Execute(() => store.ResetAsync(request, HttpContext.RequestAborted, receiptId));
    private async Task<ActionResult<ReceiptDto>> Execute(Func<Task<ReceiptDto>> action)
    {
        try { return await action(); }
        catch (ReceiptException error) { return Problem(statusCode: error.Status, title: error.Message); }
    }
}
