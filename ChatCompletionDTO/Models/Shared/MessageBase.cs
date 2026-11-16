using System.Text.Json.Serialization;

namespace MistralClient.Models.Shared;

/*
[JsonDerivedType(typeof(SystemMessage), typeDiscriminator: "system")]
[JsonDerivedType(typeof(UserMessage), typeDiscriminator: "user")]
[JsonDerivedType(typeof(AssistantMessage), typeDiscriminator: "assistant")]
[JsonDerivedType(typeof(ToolMessage), typeDiscriminator: "tool")]
*/
[JsonDerivedType(typeof(SystemMessage))]
[JsonDerivedType(typeof(UserMessage))]
[JsonDerivedType(typeof(AssistantMessage))]
[JsonDerivedType(typeof(ToolMessage))]

public abstract class MessageBase
{
   [JsonPropertyName("role")]
   public abstract string Role { get; }

   [JsonPropertyName("content")]
   public virtual string? Content { get; set; }

   [JsonPropertyName("name")]
   public string? Name { get; set; }

   [JsonPropertyName("tool_calls")]
   public List<ToolCall>? ToolCalls { get; set; }

   [JsonPropertyName("tool_call_id")]
   public virtual string? ToolCallId { get; set; }
}

public class ToolCall
{
   [JsonPropertyName("id")]
   public string? Id { get; set; }

   [JsonPropertyName("type")]
   public string? Type { get; set; }

   [JsonPropertyName("function")]
   public FunctionCall? Function { get; set; }
}

public class FunctionCall
{
   [JsonPropertyName("name")]
   public string? Name { get; set; }

   [JsonPropertyName("arguments")]
   public string? Arguments { get; set; }
}