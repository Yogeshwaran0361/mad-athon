using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Common;

namespace ReworkGuard.Api.Infrastructure;

/// <summary>Turns application and domain exceptions into RFC 7807 problem details.</summary>
internal sealed partial class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => ValidationProblem(validation),
            AuthenticationFailedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found", exception.Message),
            ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            DomainForbiddenException domain => Problem(StatusCodes.Status403Forbidden, "Forbidden", domain.Message, domain.Code),
            DomainException domain => Problem(StatusCodes.Status409Conflict, "Business rule violated", domain.Message, domain.Code),
            DbUpdateConcurrencyException => Problem(
                StatusCodes.Status409Conflict,
                "Concurrent update",
                "The record was changed by someone else. Reload and try again.",
                "concurrency_conflict"),
            _ => null,
        };

        if (problem is null)
        {
            LogUnhandled(_logger, exception.GetType().Name, httpContext.TraceIdentifier, exception);
            problem = Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail, string? code = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    private static ValidationProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled {ExceptionType} for request {TraceId}")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType, string traceId, Exception exception);
}
