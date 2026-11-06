using Microsoft.AspNetCore.Mvc;

namespace GeneralExpenses.Api;

[ApiController]
[Route("api/receipt")]
public sealed class ReceiptController(ReceiptStore store) : ControllerBase
{
    // DI создаёт сервис и DbContext для этого запроса. Контроллер переводит HTTP в вызовы C#.
    [HttpGet] public Task<ActionResult<ReceiptDto>> Get() => Execute(() => store.ReadAsync(HttpContext.RequestAborted));
    [HttpPost("items")] public Task<ActionResult<ReceiptDto>> Add(ItemRequest request) => Execute(() => store.AddAsync(request, HttpContext.RequestAborted));
    [HttpPut("items/{id}")] public Task<ActionResult<ReceiptDto>> Edit(string id, ItemRequest request) => Execute(() => store.EditAsync(id, request, HttpContext.RequestAborted));
    [HttpDelete("items/{id}")] public Task<ActionResult<ReceiptDto>> Delete(string id, [FromBody] ChangeRequest request) => Execute(() => store.DeleteAsync(id, request, HttpContext.RequestAborted));
    [HttpPost("undo")] public Task<ActionResult<ReceiptDto>> Undo(ChangeRequest request) => Execute(() => store.UndoAsync(request, HttpContext.RequestAborted));
    [HttpPut("items/{id}/participants/{personId}")] public Task<ActionResult<ReceiptDto>> Select(string id, string personId, SelectionRequest request) => Execute(() => store.SelectAsync(id, personId, request, HttpContext.RequestAborted));
    [HttpPost("reset")] public Task<ActionResult<ReceiptDto>> Reset(ChangeRequest request) => Execute(() => store.ResetAsync(request, HttpContext.RequestAborted));

    private async Task<ActionResult<ReceiptDto>> Execute(Func<Task<ReceiptDto>> action)
    {
        try { return await action(); }
        catch (ReceiptException error) { return Problem(statusCode: error.Status, title: error.Message); }
    }
}

