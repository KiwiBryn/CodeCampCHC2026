using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class Error
{
   public required string Message { get; set; }
   public string? Type { get; set; }
   public int? Code { get; set; }
}