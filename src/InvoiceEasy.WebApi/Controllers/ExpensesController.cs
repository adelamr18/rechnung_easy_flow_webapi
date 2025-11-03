using System.Globalization;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IConfiguration _configuration;

    public ExpensesController(
        IExpenseRepository expenseRepository,
        IUserRepository userRepository,
        IFileStorage fileStorage,
        IConfiguration configuration)
    {
        _expenseRepository = expenseRepository;
        _userRepository = userRepository;
        _fileStorage = fileStorage;
        _configuration = configuration;
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
        
        if (user.Plan != "pro")
        {
            var count = await _expenseRepository.CountByUserIdAndMonthAsync(userId, startOfMonth);
            if (count >= 10)
                return BadRequest(new { error = "Expense quota exceeded. Please upgrade to Pro." });
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

        if (!TryParseAmount(amountStr, out var parsedAmount))
        {
            return BadRequest(new { error = "Invalid amount format." });
        }

        var expense = new Domain.Entities.Expense
        {
            UserId = userId,
            Amount = parsedAmount,
            Note = note,
            ExpenseDate = !string.IsNullOrEmpty(expenseDateStr) && DateOnly.TryParse(expenseDateStr, out var date) 
                ? date 
                : DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var receiptPath = await _fileStorage.SaveFileAsync(file.OpenReadStream(), file.FileName, "receipts");
        expense.ReceiptPath = receiptPath;

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
}
