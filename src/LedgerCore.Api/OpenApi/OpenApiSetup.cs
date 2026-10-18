using LedgerCore.Api.Idempotency;
using LedgerCore.Domain.Accounts;
using Microsoft.OpenApi;

namespace LedgerCore.Api.OpenApi;

internal static class OpenApiSetup
{
    public static IServiceCollection AddLedgerOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "ledger-core API";
                return Task.CompletedTask;
            });

            // AccountId binds through TryParse, so the generator only sees a string
            options.AddOperationTransformer((operation, context, _) =>
            {
                var accountIds = context.Description.ParameterDescriptions
                    .Where(p => p.Type == typeof(AccountId))
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal);

                var schemas = (operation.Parameters ?? [])
                    .Where(p => accountIds.Contains(p.Name!))
                    .Select(p => p.Schema)
                    .OfType<OpenApiSchema>();

                foreach (var schema in schemas)
                {
                    schema.Format = "uuid";
                }

                return Task.CompletedTask;
            });

            // the header is read by IdempotencyFilter, so no endpoint declares it
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (HttpMethods.IsPost(context.Description.HttpMethod ?? string.Empty))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(IdempotencyKey);
                }

                return Task.CompletedTask;
            });
        });

    private static readonly OpenApiParameter IdempotencyKey = new()
    {
        Name = IdempotencyFilter.KeyHeader,
        In = ParameterLocation.Header,
        Required = false,
        Description = "Send the same key to retry safely: a repeat gets the first response back. "
            + "The same key for a different request is a 422, and one still running is a 409.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = IdempotencyFilter.MaxKeyLength },
    };
}
