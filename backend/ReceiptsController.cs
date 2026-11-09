using Microsoft.AspNetCore.Mvc;
namespace GeneralExpenses.Api;

[ApiController]
[Route("api/receipts")]
public sealed class ReceiptsController(ReceiptStore store) : ControllerBase
{
    [HttpGet] public Task<ReceiptSummaryDto[]> List() => store.ListAsync(HttpContext.RequestAborted);
    [HttpPost] public async Task<ActionResult<ReceiptDto>> Create(CreateReceiptRequest request)
    {
        try
        {
            var receipt = await store.CreateAsync(request, HttpContext.RequestAborted);
            return Created($"/api/receipts/{receipt.Id}", receipt);
        }
        catch (ReceiptException error) { return Problem(statusCode: error.Status, title: error.Message); }
    }
}
