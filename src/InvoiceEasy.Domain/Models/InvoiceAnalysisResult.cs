namespace InvoiceEasy.Domain.Models;

public class InvoiceAnalysisResult
{
    public string? CustomerName { get; set; }
    public string? VendorName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceAnalysisItem> Items { get; set; } = new();
    public Dictionary<string, string> RawFields { get; set; } = new();
}

public class InvoiceAnalysisItem
{
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice { get; set; }
}
