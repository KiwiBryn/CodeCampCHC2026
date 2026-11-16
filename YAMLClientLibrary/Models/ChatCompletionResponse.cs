using System.Text.Json;
using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class ChatCompletionResponse
{
   public required string Id { get; set; }
   public string Object { get; set; } = "chat.completion";
   public required long Created { get; set; }
   public required string Model { get; set; }
   public required List<ChatCompletionChoice> Choices { get; set; }
   public Usage? Usage { get; set; }
}

public class ChatCompletionChoice
{
   public int Index { get; set; }
   public required ChatMessage Message { get; set; }

   [JsonPropertyName("finish_reason")]
   public FinishReason? FinishReason { get; set; }
}

public class ChatCompletionStreamResponse
{
   public required string Id { get; set; }
   public string Object { get; set; } = "chat.completion.chunk";
   public required long Created { get; set; }
   public required string Model { get; set; }
   public required List<ChatCompletionStreamChoice> Choices { get; set; }
}

public class ChatCompletionStreamChoice
{
   public int Index { get; set; }
   public required ChatCompletionDelta Delta { get; set; }

   [JsonPropertyName("finish_reason")]
   public FinishReason? FinishReason { get; set; }
}

public class ChatCompletionDelta
{
   public MessageRole? Role { get; set; }
   public JsonElement? Content { get; set; }
}