namespace GenesisDB.Client;

/// <summary>
/// Configuration options for the GenesisDB client.
/// </summary>
public class ClientConfig
{
    /// <summary>
    /// The base URL of the GenesisDB API (e.g., "http://localhost:8080").
    /// Falls back to the GENESISDB_API_URL environment variable if not set.
    /// </summary>
    public string? ApiUrl { get; set; }

    /// <summary>
    /// The API version to use (e.g., "v1").
    /// Falls back to the GENESISDB_API_VERSION environment variable if not set.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// The bearer authentication token.
    /// Falls back to the GENESISDB_AUTH_TOKEN environment variable if not set.
    /// </summary>
    public string? AuthToken { get; set; }
}
