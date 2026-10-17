namespace ReworkGuard.Api.Infrastructure;

/// <summary>
/// Response headers that close common browser attack paths. API responses get a deny-all CSP and are never cached;
/// the Hangfire dashboard and Swagger UI (development only) get a CSP that allows their own inline scripts and styles.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    public const string ApiCsp = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    public const string UiCsp = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;
            var ui = path.StartsWithSegments("/hangfire") || path.StartsWithSegments("/swagger");
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = ui ? UiCsp : ApiCsp;
            if (path.StartsWithSegments("/api") && string.IsNullOrEmpty(headers.CacheControl))
            {
                headers.CacheControl = "no-store";
            }

            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
