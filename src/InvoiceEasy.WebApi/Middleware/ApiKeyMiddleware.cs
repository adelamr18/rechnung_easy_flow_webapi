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
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Server API key not configured.");
            return;
        }

        string? incomingApiKey =
            context.Request.Headers[ApiKeyHeaderName].FirstOrDefault() ??
            context.Request.Query["apiKey"].FirstOrDefault() ??
            context.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(incomingApiKey))
        {
            var auth = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(auth) &&
                auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                incomingApiKey = auth.Substring("Bearer ".Length).Trim();
            }
        }

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