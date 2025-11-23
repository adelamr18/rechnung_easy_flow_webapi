namespace InvoiceEasy.WebApi.DTOs;

public class CreateInvoiceRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public List<InvoiceLineItemDto>? Items { get; set; }
}

public class GenerateInvoicePdfRequest
{
    public string? Template { get; set; }
}

public class InvoiceResponse
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateOnly InvoiceDate { get; set; }
    public string? DownloadUrl { get; set; }
    public List<InvoiceLineItemDto>? Items { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string>? Meta { get; set; }
}

public class InvoiceLineItemDto
{
    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice { get; set; }
}
