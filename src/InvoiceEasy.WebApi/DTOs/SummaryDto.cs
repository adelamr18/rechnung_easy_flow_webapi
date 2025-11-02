namespace InvoiceEasy.WebApi.DTOs;

public class MonthlySummaryResponse
{
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit { get; set; }
    public List<ChartDataPoint> Chart { get; set; } = new();
}

public class ChartDataPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
}

