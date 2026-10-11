using LedgerCore.Application.Accounts;
using LedgerCore.Application.Commands;
using LedgerCore.Application.Queries;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerCore.Api.Accounts;

internal static class AccountEndpoints
{
    private const string GetAccountRoute = "GetAccount";

    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder routes)
    {
        var accounts = routes.MapGroup("/accounts").WithTags("Accounts");
        accounts.MapPost("/", OpenAsync);
        accounts.MapGet("/{id}", GetAsync).WithName(GetAccountRoute);
        accounts.MapPost("/{id}/deposits", DepositAsync);
        accounts.MapPost("/{id}/withdrawals", WithdrawAsync);
        return routes;
    }

    private static async Task<Results<CreatedAtRoute<AccountOpenedResponse>, ValidationProblem>> OpenAsync(
        OpenAccountRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Iban.TryParse(request.Iban, out var iban))
        {
            errors["iban"] = ["Not a valid IBAN."];
        }

        if (!Currency.TryFromCode(request.Currency, out var currency))
        {
            errors["currency"] = ["Not a supported ISO 4217 currency code."];
        }

        if (iban is null || currency is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var id = (await commands.DispatchAsync(new OpenAccount(iban, currency), cancellationToken)).Value.Value;
        return TypedResults.CreatedAtRoute(new AccountOpenedResponse(id), GetAccountRoute, new { id });
    }

    // the read model catches up in the background, so a GET right after opening can still be a 404
    private static async Task<Results<Ok<AccountResponse>, NotFound>> GetAsync(
        AccountId id, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var summary = await queries.DispatchAsync(new GetAccount(id), cancellationToken);
        return summary is null ? TypedResults.NotFound() : TypedResults.Ok(AccountResponse.From(summary));
    }

    private static async Task<Results<Ok<BalanceResponse>, ValidationProblem, NotFound, Conflict<DomainError>>> DepositAsync(
        AccountId id, AmountRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReadAmount(request, errors) is not { } amount)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Answer(await commands.DispatchAsync(new Deposit(id, amount), cancellationToken));
    }

    private static async Task<Results<Ok<BalanceResponse>, ValidationProblem, NotFound, Conflict<DomainError>>> WithdrawAsync(
        AccountId id, AmountRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReadAmount(request, errors) is not { } amount)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Answer(await commands.DispatchAsync(new Withdraw(id, amount), cancellationToken));
    }

    private static Money? ReadAmount(AmountRequest request, Dictionary<string, string[]> errors)
    {
        if (!Currency.TryFromCode(request.Currency, out var currency))
        {
            errors["currency"] = ["Not a supported ISO 4217 currency code."];
            return null;
        }

        // Money refuses extra decimals with an exception; here it's the client's mistake, so a 400
        if (decimal.Round(request.Amount, currency.MinorUnits) != request.Amount)
        {
            errors["amount"] = [$"{currency.Code} amounts have at most {currency.MinorUnits} decimal places."];
            return null;
        }

        return Money.Of(request.Amount, currency);
    }

    private static Results<Ok<BalanceResponse>, ValidationProblem, NotFound, Conflict<DomainError>> Answer(Result<Money> result)
    {
        if (result.IsSuccess)
        {
            return TypedResults.Ok(BalanceResponse.From(result.Value));
        }

        if (result.Error.Code == AccountErrors.NotFoundCode)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Conflict(result.Error);
    }
}
