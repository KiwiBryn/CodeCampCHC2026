using System.Text.Json.Serialization;
using MistralClient.Models.Shared;

namespace MistralClient.Models.Synchronous;

public class ChatCompletionResponse
{
   [JsonPropertyName("output")]
   public string? Output { get; set; }

   [JsonPropertyName("stop_reason")]
   public string? StopReason { get; set; }

   [JsonPropertyName("stop_sequence")]
   public string? StopSequence { get; set; }

   [JsonPropertyName("usage")]
   public Usage? Usage { get; set; }

   [JsonPropertyName("choices")]
   public List<Choice>? Choices { get; set; }
}

public class Usage
{
   [JsonPropertyName("prompt_tokens")]
   public int PromptTokens { get; set; }

   [JsonPropertyName("completion_tokens")]
   public int CompletionTokens { get; set; }

   [JsonPropertyName("total_tokens")]
   public int TotalTokens { get; set; }
}

public class Choice
{
   [JsonPropertyName("index")]
   public int Index { get; set; }

   [JsonPropertyName("message")]
   public AssistantMessage? Message { get; set; }

   //[JsonPropertyName("message")]
   //public AssistantMessage? Message { get; set; }

   [JsonPropertyName("finish_reason")]
   public string? FinishReason { get; set; }
}