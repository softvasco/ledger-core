using LedgerCore.Application.EventStore;
using LedgerCore.Application.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Problems;

/// <summary>Answers the exceptions a client can act on with a specific problem; anything else is left to the generic 500.</summary>
internal sealed class ProblemExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public const string ConcurrencyConflictCode = "concurrency_conflict";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = exception switch
        {
            CommandValidationException invalid => Invalid(invalid),
            ConcurrencyConflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "The account changed while the request ran. Sending it again is safe.",
                Extensions = { [DomainProblems.CodeExtension] = ConcurrencyConflictCode },
            },
            BadHttpRequestException badRequest => new ProblemDetails { Status = badRequest.StatusCode },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static HttpValidationProblemDetails Invalid(CommandValidationException exception) =>
        new(exception.Errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
        };
}
