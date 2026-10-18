using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LedgerCore.Api.Problems;
using LedgerCore.Application.Idempotency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace LedgerCore.Api.Idempotency;

/// <summary>Runs a POST with an Idempotency-Key once and replays its response to retries.</summary>
internal static class IdempotencyFilter
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const string InvalidKeyCode = "idempotency.invalid_key";
    public const string InProgressCode = "idempotency.in_progress";
    public const string KeyReusedCode = "idempotency.key_reused";

    public const int MaxKeyLength = 255;

    private static readonly Type[] NotPartOfTheRequest = [typeof(CancellationToken), typeof(HttpContext), typeof(HttpRequest)];

    // a filter, not middleware, so callers that run the endpoint's delegate in-process get it too
    public static TBuilder WithIdempotencyKeys<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilterFactory((factory, next) =>
        {
            if (!ReturnsResult(factory.MethodInfo))
            {
                throw new InvalidOperationException($"{factory.MethodInfo.Name} must return an IResult for its response to be stored.");
            }

            var isService = factory.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
            var requestArguments = factory.MethodInfo.GetParameters()
                .Where(p => !NotPartOfTheRequest.Contains(p.ParameterType) && !isService.IsService(p.ParameterType))
                .Select(p => p.Position)
                .ToArray();

            return context => InvokeAsync(context, next, requestArguments);
        });

    private static async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, int[] requestArguments)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsPost(http.Request.Method) || !http.Request.Headers.TryGetValue(KeyHeader, out var values))
        {
            return await next(context);
        }

        var key = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return await ProblemAsync(http, StatusCodes.Status400BadRequest, InvalidKeyCode, $"Send one {KeyHeader} of 1 to {MaxKeyLength} characters.");
        }

        var store = http.RequestServices.GetRequiredService<IIdempotencyStore>();
        switch (await store.ClaimAsync(key, Fingerprint(context, requestArguments), http.RequestAborted))
        {
            case IdempotencyClaim.Completed completed:
                await ReplayAsync(http, completed.Response);
                return Results.Empty;
            case IdempotencyClaim.KeyInProgress:
                return await ProblemAsync(http, StatusCodes.Status409Conflict, InProgressCode, "A request with this key is still running. Retry once it has finished.");
            case IdempotencyClaim.KeyReused:
                return await ProblemAsync(http, StatusCodes.Status422UnprocessableEntity, KeyReusedCode, "This key was already used for a different request.");
        }

        await RunOnceAsync(context, next, store, key);
        return Results.Empty;
    }

    private static async Task RunOnceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, IIdempotencyStore store, string key)
    {
        var response = context.HttpContext.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;

        // the command may have committed, so store or release even if the client went away
        try
        {
            var result = (IResult?)await next(context) ?? Results.Empty;
            await result.ExecuteAsync(context.HttpContext);
        }
        catch
        {
            response.Body = original;
            await store.ReleaseAsync(key, CancellationToken.None);
            throw;
        }

        response.Body = original;
        if (response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
        }
        else
        {
            await store.CompleteAsync(
                key,
                new StoredResponse(response.StatusCode, response.ContentType, response.Headers.Location, buffer.ToArray()),
                CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.HttpContext.RequestAborted);
    }

    private static async Task ReplayAsync(HttpContext context, StoredResponse stored)
    {
        var response = context.Response;
        response.StatusCode = stored.StatusCode;
        response.ContentType = stored.ContentType;
        if (stored.Location is not null)
        {
            response.Headers.Location = stored.Location;
        }

        response.Headers[ReplayedHeader] = "true";
        await response.Body.WriteAsync(stored.Body, context.RequestAborted);
    }

    // bound arguments rather than raw body bytes, so in-process callers fingerprint the same way
    private static byte[] Fingerprint(EndpointFilterInvocationContext context, int[] requestArguments)
    {
        var json = context.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}\n"));
        foreach (var argument in requestArguments.Select(position => context.Arguments[position]))
        {
            hash.AppendData(argument is null ? "null"u8 : JsonSerializer.SerializeToUtf8Bytes(argument, argument.GetType(), json));
            hash.AppendData("\n"u8);
        }

        return hash.GetHashAndReset();
    }

    private static async Task<IResult> ProblemAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Detail = detail,
                Extensions = { [DomainProblems.CodeExtension] = code },
            },
        });
        return Results.Empty;
    }

    private static bool ReturnsResult(MethodInfo method)
    {
        var type = method.ReturnType;
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            type = type.GetGenericArguments()[0];
        }

        return typeof(IResult).IsAssignableFrom(type);
    }
}
