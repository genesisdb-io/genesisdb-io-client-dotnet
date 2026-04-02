using System.Net;

namespace GenesisDB.Client;

/// <summary>
/// Exception thrown when the GenesisDB API returns an error response.
/// </summary>
public class GenesisDbException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the API.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The reason phrase from the HTTP response.
    /// </summary>
    public string? ReasonPhrase { get; }

    /// <summary>
    /// Creates a new GenesisDbException.
    /// </summary>
    public GenesisDbException(HttpStatusCode statusCode, string? reasonPhrase)
        : base($"API Error: {(int)statusCode} {reasonPhrase}")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
    }

    /// <summary>
    /// Creates a new GenesisDbException with an inner exception.
    /// </summary>
    public GenesisDbException(HttpStatusCode statusCode, string? reasonPhrase, Exception innerException)
        : base($"API Error: {(int)statusCode} {reasonPhrase}", innerException)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
    }
}
