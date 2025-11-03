using InvoiceEasy.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReceiptsController : ControllerBase
{
    private readonly IReceiptService _receiptService;
    private readonly ILogger<ReceiptsController> _logger;

    public ReceiptsController(
        IReceiptService receiptService,
        ILogger<ReceiptsController> logger)
    {
        _receiptService = receiptService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<IActionResult> UploadReceipt(IFormFile file)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            var receipt = await _receiptService.ProcessReceiptUploadAsync(stream, file.FileName);
            return Ok(receipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading receipt");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReceipt(Guid id)
    {
        try
        {
            var receipt = await _receiptService.GetReceiptAsync(id);
            return Ok(receipt);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving receipt with ID {id}");
            return StatusCode(500, new { message = "An error occurred while retrieving the receipt" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllReceipts()
    {
        try
        {
            var receipts = await _receiptService.GetAllReceiptsAsync();
            return Ok(receipts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving receipts");
            return StatusCode(500, new { message = "An error occurred while retrieving receipts" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteReceipt(Guid id)
    {
        try
        {
            await _receiptService.DeleteReceiptAsync(id);
            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting receipt with ID {id}");
            return StatusCode(500, new { message = "An error occurred while deleting the receipt" });
        }
    }
}
