namespace InvoiceEasy.WebApi.DTOs;

public class InvoiceAnalysisResponse
{
    public string? CustomerName { get; set; }
    public string? VendorName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceAnalysisItemResponse> Items { get; set; } = new();
    public Dictionary<string, string> RawFields { get; set; } = new();
}

public class InvoiceAnalysisItemResponse
{
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice { get; set; }
}
