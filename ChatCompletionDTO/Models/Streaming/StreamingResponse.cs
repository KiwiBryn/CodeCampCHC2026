namespace MistralClient.Models.Streaming;


using System.Text.Json.Serialization;

public class StreamingResponse
{
   [JsonPropertyName("output")]
   public string? Output { get; set; }

   [JsonPropertyName("stop_reason")]
   public string? StopReason { get; set; }

   [JsonPropertyName("stop_sequence")]
   public string? StopSequence { get; set; }
}