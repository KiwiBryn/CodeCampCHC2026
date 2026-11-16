using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class ChatMessage
{
   [JsonRequired]
   public MessageRole Role { get; set; }

   [JsonRequired]
   public required object Content { get; set; } // Can be string or array of objects

   [JsonPropertyName("tool_calls")]
   public List<ToolCall>? ToolCalls { get; set; }

   [JsonPropertyName("tool_call_id")]
   public string? ToolCallId { get; set; }
}

public class ToolCall
{
   public string? Id { get; set; }
   public ToolType Type { get; set; }
   public FunctionCall? Function { get; set; }
}

public class FunctionCall
{
   public required string Name { get; set; }
   public required string Arguments { get; set; }
}