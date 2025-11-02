using InvoiceEasy.Domain.Interfaces;
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

    public InvoicesController(
        IInvoiceRepository invoiceRepository,
        IUserRepository userRepository,
        PdfService pdfService,
        IFileStorage fileStorage,
        IConfiguration configuration)
    {
        _invoiceRepository = invoiceRepository;
        _userRepository = userRepository;
        _pdfService = pdfService;
        _fileStorage = fileStorage;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return NotFound();

        // Check quota
        var currentMonth = DateTime.UtcNow.Date;
        var startOfMonth = new DateTime(currentMonth.Year, currentMonth.Month, 1);
        
        if (user.Plan != "pro")
        {
            var count = await _invoiceRepository.CountByUserIdAndMonthAsync(userId, startOfMonth);
            if (count >= 5)
                return BadRequest(new { error = "Invoice quota exceeded. Please upgrade to Pro." });
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

        var pdfPath = await _pdfService.GenerateInvoicePdfAsync(invoice, user);
        invoice.PdfPath = pdfPath;
        await _invoiceRepository.UpdateAsync(invoice);

        var baseUrl = _configuration["BASE_URL"] ?? "http://localhost:5000";
        var downloadUrl = $"{baseUrl}/api/invoices/{invoice.Id}/pdf";

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
}

