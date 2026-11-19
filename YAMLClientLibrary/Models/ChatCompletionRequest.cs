using OneOf;
using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class ChatCompletionRequest
{
   [JsonRequired]
   public required string Model { get; set; }

   [JsonRequired]
   public required List<ChatMessage> Messages { get; set; }

   public float Temperature { get; set; } = 0.7f;
   public float TopP { get; set; } = 1.0f;
   public int? MaxTokens { get; set; }
   public bool Stream { get; set; } = false;

   [JsonPropertyName("stream_options")]
   public StreamOptions? StreamOptions { get; set; }
   public OneOf<string, List<string>>? Stop { get; set; }
   public int? RandomSeed { get; set; }

   [JsonPropertyName("response_format")]
   public ResponseFormat? ResponseFormat { get; set; }

   public List<Tool>? Tools { get; set; }

   [JsonPropertyName("tool_choice")]
   public ToolChoice? ToolChoice { get; set; }

   [JsonPropertyName("presence_penalty")]
   public float PresencePenalty { get; set; } = 0.0f;

   [JsonPropertyName("frequency_penalty")]
   public float FrequencyPenalty { get; set; } = 0.0f;

   [JsonPropertyName("safe_prompt")]
   public bool SafePrompt { get; set; } = false;
}

public class StreamOptions
{
   [JsonPropertyName("include_usage")]
   public bool IncludeUsage { get; set; }
}

public class ResponseFormat
{
   public ResponseFormatType Type { get; set; }
}