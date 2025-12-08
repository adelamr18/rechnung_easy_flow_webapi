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
    public async Task<IActionResult> GetMonthlySummary(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] bool allTime = false)
    {
        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var anchorMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        decimal income;
        decimal expenses;
        decimal profit;

        if (allTime)
        {
            income = await _invoiceRepository.SumAmountByUserIdAsync(userId);
            expenses = await _expenseRepository.SumAmountByUserIdAsync(userId);
            profit = income - expenses;
        }
        else
        {
            var targetYear = year ?? now.Year;
            var targetMonth = month ?? now.Month;
            var startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = startDate.AddMonths(1);

            income = await _invoiceRepository.SumAmountByUserIdAndMonthAsync(userId, startDate, endDate);
            expenses = await _expenseRepository.SumAmountByUserIdAndMonthAsync(userId, startDate, endDate);
            profit = income - expenses;

            anchorMonth = startDate;
        }

        var chartData = new List<ChartDataPoint>();
        for (int i = 5; i >= 0; i--)
        {
            var monthDate = anchorMonth.AddMonths(-i);
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
