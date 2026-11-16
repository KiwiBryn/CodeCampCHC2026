using System.Text.Json.Serialization;

namespace MistralClient.Models.Shared;

public sealed class SystemMessage : MessageBase
{
   public override string Role => "system";

   [JsonPropertyName("content")]
   public override string? Content { get; set; }
}