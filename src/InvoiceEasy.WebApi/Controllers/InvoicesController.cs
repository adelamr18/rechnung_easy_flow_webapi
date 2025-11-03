using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Infrastructure.Services;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IUserRepository _userRepository;
    private readonly PdfService _pdfService;
    private readonly IFileStorage _fileStorage;
    private readonly IConfiguration _configuration;
    private readonly IInvoiceOcrService _invoiceOcrService;

    public InvoicesController(
        IInvoiceRepository invoiceRepository,
        IUserRepository userRepository,
        PdfService pdfService,
        IFileStorage fileStorage,
        IConfiguration configuration,
        IInvoiceOcrService invoiceOcrService)
    {
        _invoiceRepository = invoiceRepository;
        _userRepository = userRepository;
        _pdfService = pdfService;
        _fileStorage = fileStorage;
        _configuration = configuration;
        _invoiceOcrService = invoiceOcrService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return NotFound();

        // Check quota
        var utcNow = DateTime.UtcNow;
        var startOfMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        if (!PlanAllowsUnlimitedInvoices(user.Plan))
        {
            var count = await _invoiceRepository.CountByUserIdAndMonthAsync(userId, startOfMonth);
            if (count >= GetInvoiceLimit(user.Plan))
                return BadRequest(new { error = "Invoice quota exceeded. Please upgrade your plan." });
        }

        var invoice = new Domain.Entities.Invoice
        {
            UserId = userId,
            CustomerName = request.CustomerName,
            ServiceDescription = request.ServiceDescription,
            Amount = request.Amount,
            InvoiceDate = request.InvoiceDate,
            Currency = "EUR"
        };

        await _invoiceRepository.AddAsync(invoice);

        string? downloadUrl = null;
        if (PlanAllowsPdfGeneration(user.Plan))
        {
            var pdfPath = await _pdfService.GenerateInvoicePdfAsync(invoice, user);
            invoice.PdfPath = pdfPath;
            await _invoiceRepository.UpdateAsync(invoice);

            var baseUrl = _configuration["BASE_URL"] ?? "http://localhost:5000";
            downloadUrl = $"{baseUrl}/api/invoices/{invoice.Id}/pdf";
        }
        else
        {
            await _invoiceRepository.UpdateAsync(invoice);
        }

        return Created($"/api/invoices/{invoice.Id}", new InvoiceResponse
        {
            Id = invoice.Id,
            CustomerName = invoice.CustomerName,
            ServiceDescription = invoice.ServiceDescription,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            InvoiceDate = invoice.InvoiceDate,
            DownloadUrl = downloadUrl,
            CreatedAt = invoice.CreatedAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = User.GetUserId();
        var invoices = await _invoiceRepository.GetByUserIdAsync(userId, page, pageSize);

        var baseUrl = _configuration["BASE_URL"] ?? "http://localhost:5000";
        var result = invoices.Select(i => new InvoiceResponse
        {
            Id = i.Id,
            CustomerName = i.CustomerName,
            ServiceDescription = i.ServiceDescription,
            Amount = i.Amount,
            Currency = i.Currency,
            InvoiceDate = i.InvoiceDate,
            DownloadUrl = string.IsNullOrEmpty(i.PdfPath) ? null : $"{baseUrl}/api/invoices/{i.Id}/pdf",
            CreatedAt = i.CreatedAt
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(Guid id)
    {
        var userId = User.GetUserId();
        var invoice = await _invoiceRepository.GetByIdAsync(id);

        if (invoice == null || invoice.UserId != userId || string.IsNullOrEmpty(invoice.PdfPath))
            return NotFound();

        var fileStream = await _fileStorage.GetFileAsync(invoice.PdfPath);
        return File(fileStream, "application/pdf", $"invoice_{id}.pdf");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInvoice(Guid id)
    {
        var userId = User.GetUserId();
        var invoice = await _invoiceRepository.GetByIdAsync(id);

        if (invoice == null || invoice.UserId != userId)
            return NotFound();

        if (!string.IsNullOrEmpty(invoice.PdfPath))
        {
            await _fileStorage.DeleteFileAsync(invoice.PdfPath);
        }

        await _invoiceRepository.DeleteAsync(invoice);
        return NoContent();
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeInvoice()
    {
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("invoice");

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Invoice file is required" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File too large. Maximum 10MB." });

        var allowedTypes = new[] { "application/pdf", "image/jpeg", "image/png", "image/jpg" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { error = "Invalid file type. Only PDF or images are allowed." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _invoiceOcrService.AnalyzeAsync(stream, file.FileName);

            return Ok(new InvoiceAnalysisResponse
            {
                CustomerName = result.CustomerName,
                VendorName = result.VendorName,
                InvoiceNumber = result.InvoiceNumber,
                InvoiceDate = result.InvoiceDate,
                TotalAmount = result.TotalAmount,
                CurrencyCode = result.CurrencyCode,
                Notes = result.Notes,
                Items = result.Items.Select(i => new InvoiceAnalysisItemResponse
                {
                    Description = i.Description,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    TotalPrice = i.TotalPrice
                }).ToList(),
                RawFields = result.RawFields
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Invoice analysis failed: {ex.Message}" });
        }
    }
    private static bool PlanAllowsUnlimitedInvoices(string plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "pro" => true,
            "elite" => true,
            _ => false
        };
    }

    private static int GetInvoiceLimit(string plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "starter" => 5,
            "free" => 5,
            _ => 5
        };
    }

    private static bool PlanAllowsPdfGeneration(string plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "pro" => true,
            "elite" => true,
            _ => false
        };
    }
}
