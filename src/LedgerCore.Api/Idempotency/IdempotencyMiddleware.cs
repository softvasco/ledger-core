using System.Security.Cryptography;
using System.Text;
using LedgerCore.Api.Problems;
using LedgerCore.Application.Idempotency;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Idempotency;

/// <summary>Runs a POST with an Idempotency-Key once and replays its response to any retry with the same key.</summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store, IProblemDetailsService problems)
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const string InvalidKeyCode = "idempotency.invalid_key";
    public const string InProgressCode = "idempotency.in_progress";
    public const string KeyReusedCode = "idempotency.key_reused";

    private const int MaxKeyLength = 255;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Headers.TryGetValue(KeyHeader, out var values))
        {
            await next(context);
            return;
        }

        var key = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, InvalidKeyCode, $"Send one {KeyHeader} of 1 to {MaxKeyLength} characters.");
            return;
        }

        var fingerprint = await FingerprintAsync(context.Request);
        switch (await store.ClaimAsync(key, fingerprint, context.RequestAborted))
        {
            case IdempotencyClaim.Completed completed:
                await ReplayAsync(context, completed.Response);
                return;
            case IdempotencyClaim.KeyInProgress:
                await WriteProblemAsync(context, StatusCodes.Status409Conflict, InProgressCode, "A request with this key is still running. Retry once it has finished.");
                return;
            case IdempotencyClaim.KeyReused:
                await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, KeyReusedCode, "This key was already used for a different request.");
                return;
        }

        await RunOnceAsync(context, key);
    }

    private async Task RunOnceAsync(HttpContext context, string key)
    {
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        // the command may have committed, so store or release even if the client went away
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = original;
            await store.ReleaseAsync(key, CancellationToken.None);
            throw;
        }

        context.Response.Body = original;
        var response = context.Response;
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
        await buffer.CopyToAsync(original, context.RequestAborted);
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

    // the same key on another route, or with another body, is a different request
    private static async Task<byte[]> FingerprintAsync(HttpRequest request)
    {
        request.EnableBuffering();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n"));

        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return hash.GetHashAndReset();
    }

    private async Task WriteProblemAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Detail = detail,
                Extensions = { [DomainProblems.CodeExtension] = code },
            },
        });
    }
}
