public class RequestCounterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiConfig _config;

    public RequestCounterMiddleware(RequestDelegate next, ApiConfig config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Increment the counter
        if (!context.Request.Path.StartsWithSegments("/admin/metrics"))
        {
        _config.FileServerHits++;
        Console.WriteLine($"Request Count: {_config.FileServerHits}"); // Add this line for debugging
        }
        await _next(context);
    }
}

