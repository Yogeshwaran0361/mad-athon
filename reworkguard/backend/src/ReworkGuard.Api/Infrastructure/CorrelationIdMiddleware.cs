using System.Text.RegularExpressions;
using Serilog.Context;

namespace ReworkGuard.Api.Infrastructure;

/// <summary>
/// Gives every request a correlation id: the caller's <c>X-Correlation-ID</c> when it is a safe token, otherwise a new
/// one. It becomes the request's TraceIdentifier (so problem details carry it), is echoed in the response and is
/// attached to every log line written while handling the request.
/// </summary>
internal sealed partial class CorrelationIdMiddleware
{
    public const string Header = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Header].ToString();
        var id = SafeId().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex SafeId();
}
