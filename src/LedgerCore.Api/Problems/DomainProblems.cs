using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerCore.Api.Problems;

/// <summary>Turns a <see cref="DomainError"/> into an RFC 9457 problem. The error code goes out as the <c>code</c> extension.</summary>
internal static class DomainProblems
{
    public const string CodeExtension = "code";

    // 409 when the request would work against another state of the account, 422 when it can never work as sent
    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        [AccountErrors.NotFoundCode] = StatusCodes.Status404NotFound,
        [AccountErrors.InvalidStateCode] = StatusCodes.Status409Conflict,
        [AccountErrors.InsufficientFundsCode] = StatusCodes.Status409Conflict,
        [AccountErrors.CurrencyMismatchCode] = StatusCodes.Status422UnprocessableEntity,
        [LedgerErrors.TooFewPostingsCode] = StatusCodes.Status422UnprocessableEntity,
        [LedgerErrors.UnbalancedCode] = StatusCodes.Status422UnprocessableEntity,
    };

    public static int StatusFor(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return StatusByCode.GetValueOrDefault(error.Code, StatusCodes.Status422UnprocessableEntity);
    }

    public static ProblemHttpResult ToProblem(this DomainError error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusFor(error),
            extensions: new Dictionary<string, object?> { [CodeExtension] = error.Code });
}
