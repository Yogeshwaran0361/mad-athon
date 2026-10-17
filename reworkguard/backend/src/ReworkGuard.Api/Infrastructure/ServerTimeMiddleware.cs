using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Api.Infrastructure;

/// <summary>
/// Stamps API responses with the application clock (X-Server-Time, ISO 8601 UTC) so the web app judges "today" by the
/// same clock the API validates against. It differs from the browser's clock when the development clock is moved.
/// </summary>
internal sealed class ServerTimeMiddleware
{
    public const string HeaderName = "X-Server-Time";

    private readonly RequestDelegate _next;

    public ServerTimeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, IClock clock)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = clock.UtcNow.ToString("O");
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
