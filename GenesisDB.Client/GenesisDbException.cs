using System.Text.Json;

namespace GenesisDB.Client;

/// <summary>A non-2xx answer from GenesisDB, or a transport failure. Subclasses classify the status.</summary>
public class GenesisDbException : Exception
{
    public GenesisDbException(string operation, int status, string body)
        : base($"{operation}: {status} {(string.IsNullOrWhiteSpace(body) ? "no response body" : body.Trim())}")
    {
        Operation = operation;
        Status = status;
        Body = body;
    }

    public GenesisDbException(string operation, string message, Exception inner)
        : base($"{operation}: {message}", inner)
    {
        Operation = operation;
        Status = 0;
        Body = "";
    }

    /// <summary>The client operation that failed, for example "commit".</summary>
    public string Operation { get; }

    /// <summary>The HTTP status, 0 for client-side failures.</summary>
    public int Status { get; }

    /// <summary>The response body as text.</summary>
    public string Body { get; }

    internal static GenesisDbException ForResponse(string operation, int status, string body) => status switch
    {
        401 or 403 => new AuthenticationException(operation, status, body),
        404 => new NotFoundException(operation, body),
        409 => new ConflictException(operation, body),
        412 => new PreconditionFailedException(operation, body),
        422 => new UnprocessableException(operation, body),
        503 => new NotReadyException(operation, body),
        _ => new GenesisDbException(operation, status, body),
    };
}

/// <summary>A commit was rejected because a precondition did not hold (412). Nothing was written.</summary>
public sealed class PreconditionFailedException : GenesisDbException
{
    public PreconditionFailedException(string operation, string body) : base(operation, 412, body)
    {
        PreconditionType = "";
        Reason = body.Trim();
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (parsed.RootElement.TryGetProperty("type", out var type)) PreconditionType = type.GetString() ?? "";
                if (parsed.RootElement.TryGetProperty("message", out var message)) Reason = message.GetString() ?? Reason;
            }
        }
        catch (JsonException)
        {
            // keep the raw body
        }
    }

    /// <summary>The precondition type that failed, for example "isSubjectNew".</summary>
    public string PreconditionType { get; }

    /// <summary>The server's explanation.</summary>
    public string Reason { get; }
}

/// <summary>The token was missing or wrong (401), or valid but not permitted for this operation (403).</summary>
public sealed class AuthenticationException : GenesisDbException
{
    public AuthenticationException(string operation, int status, string body) : base(operation, status, body) { }
}

/// <summary>The subject has no events (404).</summary>
public sealed class NotFoundException : GenesisDbException
{
    public NotFoundException(string operation, string body) : base(operation, 404, body) { }
}

/// <summary>A schema that already exists (409).</summary>
public sealed class ConflictException : GenesisDbException
{
    public ConflictException(string operation, string body) : base(operation, 409, body) { }
}

/// <summary>A read budget was hit, or an erase found no reference payloads (422).</summary>
public sealed class UnprocessableException : GenesisDbException
{
    public UnprocessableException(string operation, string body) : base(operation, 422, body) { }
}

/// <summary>The store is rebuilding or recovering (503).</summary>
public sealed class NotReadyException : GenesisDbException
{
    public NotReadyException(string operation, string body) : base(operation, 503, body) { }
}
