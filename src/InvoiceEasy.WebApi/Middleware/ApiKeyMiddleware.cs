using Microsoft.AspNetCore.Http;

namespace InvoiceEasy.WebApi.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly string? _configuredApiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _configuredApiKey = config["ApiKey"] ??
                            Environment.GetEnvironmentVariable("API_KEY") ??
                            Environment.GetEnvironmentVariable("ApiKey");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuredApiKey))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string? incomingApiKey =
            context.Request.Headers[ApiKeyHeaderName].FirstOrDefault() ??
            context.Request.Query["apiKey"].FirstOrDefault() ??
            context.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(incomingApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("API Key is missing.");
            return;
        }

        if (!string.Equals(incomingApiKey, _configuredApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid API Key.");
            return;
        }

        await _next(context);
    }
}
