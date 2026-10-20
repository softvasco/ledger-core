using LedgerCore.Api.Accounts;
using LedgerCore.Api.Idempotency;
using LedgerCore.Api.Problems;
using LedgerCore.Application.Commands;
using LedgerCore.Application.Queries;
using LedgerCore.Application.Transfers;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Transfers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerCore.Api.Transfers;

internal static class TransferEndpoints
{
    public const string TransferIdExtension = "transferId";

    private const string GetTransferRoute = "GetTransfer";

    public static IEndpointRouteBuilder MapTransfers(this IEndpointRouteBuilder routes)
    {
        var transfers = routes.MapGroup("/transfers").WithTags("Transfers").WithIdempotencyKeys();
        transfers.MapPost("/", StartAsync)
            .WithName("StartTransfer")
            .WithSummary("Move money from one account to another")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapGet("/{id}", GetAsync)
            .WithName(GetTransferRoute)
            .WithSummary("Read where a transfer got to")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<Results<CreatedAtRoute<TransferResponse>, ValidationProblem, ProblemHttpResult>> StartAsync(
        TransferRequest request, ICommandDispatcher commands, IQueryDispatcher queries, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (AmountBinding.ReadAmount(request.Amount, request.Currency, errors) is not { } amount)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var id = TransferId.New(clock);
        var command = new StartTransfer(id, AccountOrDefault(request.Source), AccountOrDefault(request.Destination), amount);
        var result = await commands.DispatchAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            // the refused transfer is still recorded, so say which one it was
            var problem = result.Error.ToProblem();
            problem.ProblemDetails.Extensions[TransferIdExtension] = id.Value;
            return problem;
        }

        var summary = (await queries.DispatchAsync(new GetTransfer(id), cancellationToken))!;
        return TypedResults.CreatedAtRoute(TransferResponse.From(summary), GetTransferRoute, new { id = id.Value });
    }

    private static async Task<Results<Ok<TransferResponse>, ProblemHttpResult>> GetAsync(
        TransferId id, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var summary = await queries.DispatchAsync(new GetTransfer(id), cancellationToken);
        return summary is null ? TransferErrors.NotFound(id).ToProblem() : TypedResults.Ok(TransferResponse.From(summary));
    }

    // an empty id is left to the validator, which reports it with the field name
    private static AccountId AccountOrDefault(Guid id) => id == Guid.Empty ? default : AccountId.From(id);
}
