using System.Text.Json.Serialization;

public class ApiConfig
{
    public int FileServerHits { get; set; }
}

public class ChirpRequest
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

