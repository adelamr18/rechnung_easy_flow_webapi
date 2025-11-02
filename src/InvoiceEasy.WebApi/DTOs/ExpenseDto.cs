namespace InvoiceEasy.WebApi.DTOs;

public class CreateExpenseRequest
{
    public decimal? Amount { get; set; }
    public string? Note { get; set; }
    public DateOnly? ExpenseDate { get; set; }
}

public class ExpenseResponse
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? ReceiptUrl { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

