using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SummaryController : ControllerBase
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IExpenseRepository _expenseRepository;

    public SummaryController(
        IInvoiceRepository invoiceRepository,
        IExpenseRepository expenseRepository)
    {
        _invoiceRepository = invoiceRepository;
        _expenseRepository = expenseRepository;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthlySummary([FromQuery] int? year = null, [FromQuery] int? month = null)
    {
        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var targetYear = year ?? now.Year;
        var targetMonth = month ?? now.Month;
        var startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1);

        var income = await _invoiceRepository.SumAmountByUserIdAndMonthAsync(userId, startDate, endDate);
        var expenses = await _expenseRepository.SumAmountByUserIdAndMonthAsync(userId, startDate, endDate);
        var profit = income - expenses;

        // Chart data for last 6 months
        var chartData = new List<ChartDataPoint>();
        for (int i = 5; i >= 0; i--)
        {
            var monthDate = startDate.AddMonths(-i);
            var monthStart = new DateTime(monthDate.Year, monthDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);

            var monthIncome = await _invoiceRepository.SumAmountByUserIdAndMonthAsync(userId, monthStart, monthEnd);
            var monthExpenses = await _expenseRepository.SumAmountByUserIdAndMonthAsync(userId, monthStart, monthEnd);

            chartData.Add(new ChartDataPoint
            {
                Label = monthStart.ToString("MMM yyyy"),
                Income = monthIncome,
                Expenses = monthExpenses
            });
        }

        return Ok(new MonthlySummaryResponse
        {
            Income = income,
            Expenses = expenses,
            Profit = profit,
            Chart = chartData
        });
    }
}
