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

                foreach (var parameter in operation.Parameters ?? [])
                {
                    if (accountIds.Contains(parameter.Name!) && parameter.Schema is OpenApiSchema schema)
                    {
                        schema.Format = "uuid";
                    }
                }

                return Task.CompletedTask;
            });
        });
}
