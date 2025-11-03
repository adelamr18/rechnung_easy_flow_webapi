using System.Globalization;
using System.IO;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IReceiptService _receiptService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExpensesController> _logger;

    public ExpensesController(
        IExpenseRepository expenseRepository,
        IUserRepository userRepository,
        IFileStorage fileStorage,
        IReceiptService receiptService,
        IConfiguration configuration,
        ILogger<ExpensesController> logger)
    {
        _expenseRepository = expenseRepository;
        _userRepository = userRepository;
        _fileStorage = fileStorage;
        _receiptService = receiptService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateExpense()
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return NotFound();

        // Check quota
        var utcNow = DateTime.UtcNow;
        var startOfMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        if (!PlanAllowsUnlimitedExpenses(user.Plan))
        {
            var count = await _expenseRepository.CountByUserIdAndMonthAsync(userId, startOfMonth);
            if (count >= GetExpenseLimit(user.Plan))
                return BadRequest(new { error = "Expense quota exceeded. Please upgrade your plan." });
        }

        var form = await Request.ReadFormAsync();
        var amountStr = form["amount"].ToString();
        var note = form["note"].ToString();
        var expenseDateStr = form["expenseDate"].ToString();
        var file = form.Files.GetFile("receipt");

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Receipt file is required" });

        if (file.Length > 5 * 1024 * 1024) // 5MB
            return BadRequest(new { error = "File too large. Maximum 5MB." });

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/jpg", "image/heic" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { error = "Invalid file type. Only images are allowed." });

        TryParseAmount(amountStr, out var parsedAmount);
        Domain.Models.Receipt? processedReceipt = null;
        try
        {
            await using var workingStream = new MemoryStream();
            await file.CopyToAsync(workingStream);
            workingStream.Position = 0;
            processedReceipt = await _receiptService.ProcessReceiptUploadAsync(workingStream, file.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Receipt OCR failed for user {UserId}", userId);
        }

        var finalAmount = DetermineExpenseAmount(parsedAmount, processedReceipt);

        var expense = new Domain.Entities.Expense
        {
            UserId = userId,
            Amount = finalAmount,
            Note = note,
            ExpenseDate = !string.IsNullOrEmpty(expenseDateStr) && DateOnly.TryParse(expenseDateStr, out var date) 
                ? date 
                : DateOnly.FromDateTime(DateTime.UtcNow)
        };

        if (processedReceipt != null)
        {
            expense.ReceiptPath = processedReceipt.FilePath;
            if (string.IsNullOrWhiteSpace(expense.Note) && !string.IsNullOrWhiteSpace(processedReceipt.MerchantName))
            {
                expense.Note = processedReceipt.MerchantName;
            }
        }
        else
        {
            var receiptPath = await _fileStorage.SaveFileAsync(file.OpenReadStream(), file.FileName, "receipts");
            expense.ReceiptPath = receiptPath;
        }

        await _expenseRepository.AddAsync(expense);

        var baseUrl = _configuration["BASE_URL"] ?? "http://localhost:5000";
        var receiptUrl = $"{baseUrl}/api/expenses/{expense.Id}/receipt";

        return Created($"/api/expenses/{expense.Id}", new ExpenseResponse
        {
            Id = expense.Id,
            Amount = expense.Amount,
            Note = expense.Note,
            ReceiptUrl = receiptUrl,
            ExpenseDate = expense.ExpenseDate,
            CreatedAt = expense.CreatedAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetExpenses([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = User.GetUserId();
        var expenses = await _expenseRepository.GetByUserIdAsync(userId, page, pageSize);

        var baseUrl = _configuration["BASE_URL"] ?? "http://localhost:5000";
        var result = expenses.Select(e => new ExpenseResponse
        {
            Id = e.Id,
            Amount = e.Amount,
            Note = e.Note,
            ReceiptUrl = string.IsNullOrEmpty(e.ReceiptPath) ? null : $"{baseUrl}/api/expenses/{e.Id}/receipt",
            ExpenseDate = e.ExpenseDate,
            CreatedAt = e.CreatedAt
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id}/receipt")]
    public async Task<IActionResult> GetExpenseReceipt(Guid id)
    {
        var userId = User.GetUserId();
        var expense = await _expenseRepository.GetByIdAsync(id);

        if (expense == null || expense.UserId != userId || string.IsNullOrEmpty(expense.ReceiptPath))
            return NotFound();

        var fileStream = await _fileStorage.GetFileAsync(expense.ReceiptPath);
        var contentType = expense.ReceiptPath.EndsWith(".png") ? "image/png" : "image/jpeg";
        return File(fileStream, contentType);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteExpense(Guid id)
    {
        var userId = User.GetUserId();
        var expense = await _expenseRepository.GetByIdAsync(id);

        if (expense == null || expense.UserId != userId)
            return NotFound();

        if (!string.IsNullOrEmpty(expense.ReceiptPath))
        {
            await _fileStorage.DeleteFileAsync(expense.ReceiptPath);
        }

        await _expenseRepository.DeleteAsync(expense);
        return NoContent();
    }

    private static bool TryParseAmount(string amountInput, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(amountInput))
        {
            return true;
        }

        var sanitized = amountInput.Trim()
            .Replace(" ", string.Empty)
            .Replace("\u00A0", string.Empty); // non-breaking space

        var styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;

        return decimal.TryParse(sanitized, styles, CultureInfo.InvariantCulture, out amount) ||
               decimal.TryParse(sanitized, styles, CultureInfo.GetCultureInfo("de-DE"), out amount) ||
               decimal.TryParse(sanitized, styles, CultureInfo.CurrentCulture, out amount);
    }

    private static decimal DetermineExpenseAmount(decimal providedAmount, Domain.Models.Receipt? receipt)
    {
        decimal detectedAmount = 0;

        if (receipt != null)
        {
            if (receipt.TotalAmount.HasValue && receipt.TotalAmount.Value > 0)
            {
                detectedAmount = receipt.TotalAmount.Value;
            }
            else if (receipt.ExtractedData != null)
            {
                if (receipt.ExtractedData.TryGetValue("total", out var totalStr) &&
                    decimal.TryParse(totalStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var totalParsed))
                {
                    detectedAmount = totalParsed;
                }
                else if (receipt.ExtractedData.TryGetValue("itemsTotal", out var itemsTotalStr) &&
                         decimal.TryParse(itemsTotalStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var itemsParsed))
                {
                    detectedAmount = itemsParsed;
                }
                else if (receipt.ExtractedData.TryGetValue("totalDetectedFromText", out var textTotalStr) &&
                         decimal.TryParse(textTotalStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var textParsed))
                {
                    detectedAmount = textParsed;
                }
            }
        }

        if (providedAmount > 0)
        {
            return providedAmount;
        }

        return detectedAmount > 0 ? detectedAmount : 0;
    }

    private static bool PlanAllowsUnlimitedExpenses(string plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "pro" => true,
            "elite" => true,
            _ => false
        };
    }

    private static int GetExpenseLimit(string plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "starter" => 5,
            "free" => 5,
            _ => 5
        };
    }
}
