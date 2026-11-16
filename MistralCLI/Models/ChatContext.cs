using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class ChatContext
{
   [JsonPropertyName("systemPrompt")]
   public string? SystemPrompt { get; set; }

   [JsonPropertyName("messages")]
   public List<ChatMessage> Messages { get; set; } = new();

   [JsonPropertyName("model")]
   public string Model { get; set; } = "mistral-large-latest";

   [JsonPropertyName("temperature")]
   public float Temperature { get; set; } = 0.7f;

   [JsonPropertyName("lastUpdated")]
   public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
}
