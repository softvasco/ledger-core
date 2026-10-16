namespace LedgerCore.Application.Idempotency;

public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, byte[] Body);
