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

   // Populated on the final chunk when stream_options.include_usage is true
   public Usage? Usage { get; set; }
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

   [JsonPropertyName("tool_calls")]
   public List<ChatCompletionDeltaToolCall>? ToolCalls { get; set; }
}

// Tool call fragments as they arrive in stream deltas: Id and name are only
// present on the first fragment for a given index; argument text arrives in
// pieces on subsequent fragments.
public class ChatCompletionDeltaToolCall
{
   public int Index { get; set; }
   public string? Id { get; set; }
   public ChatCompletionDeltaFunction? Function { get; set; }
}

public class ChatCompletionDeltaFunction
{
   public string? Name { get; set; }
   public string? Arguments { get; set; }
}