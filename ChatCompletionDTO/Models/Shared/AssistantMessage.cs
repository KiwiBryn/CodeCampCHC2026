using System.Text.Json.Serialization;

namespace MistralClient.Models.Shared;

public sealed class AssistantMessage : MessageBase
{
   public override string Role => "assistant";

   [JsonPropertyName("content")]
   public override string? Content { get; set; }
}