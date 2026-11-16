using MistralAi.Agents.Models;
using MistralAi.Agents.Services;
using System.Text.Json;

namespace SampleConsoleApp;

public class ChatDemo(IMistralAiService mistralService)
{
   public async Task RunAsync()
   {
      try
      {
         // Basic chat completion example
         await BasicChatCompletionAsync();

         // Streaming chat completion example
         await StreamingChatCompletionAsync();

         // Tool calling example
         await ToolCallingExampleAsync();
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error: {ex.Message}");
      }
   }

   private async Task BasicChatCompletionAsync()
   {
      Console.WriteLine("\n=== Basic Chat Completion ===");

      var request = new ChatCompletionRequest
      {
         Model = "mistral-large-latest",
         Messages =
          [
              new ChatMessage { Role = MessageRole.System, Content = "You are a helpful assistant." },
              new ChatMessage { Role = MessageRole.User, Content = "What is the capital of France?" }
          ],
         Temperature = 0.7f,
         MaxTokens = 100
      };

      var response = await mistralService.CreateChatCompletionAsync(request);
      Console.WriteLine($"Response: {response.Choices[0].Message.Content}");
      Console.WriteLine($"Usage: {response.Usage?.TotalTokens} tokens");
   }

   private async Task StreamingChatCompletionAsync()
   {
      Console.WriteLine("\n=== Streaming Chat Completion ===");

      var request = new ChatCompletionRequest
      {
         Model = "mistral-large-latest",
         Messages =
          [
              new ChatMessage { Role = MessageRole.User, Content = "Write a short poem about the ocean." }
          ],
         Temperature = 0.9f,
         MaxTokens = 200
      };


      await foreach (var chunk in mistralService.CreateChatCompletionStreamAsync(request))
      {
         foreach (var choice in chunk.Choices)
         {
            WriteStreamContent(choice.Delta.Content);
         }
      }

      Console.WriteLine();
   }

   private static void WriteStreamContent(JsonElement? content)
   {
      if (content is not { } value)
         return;

      if (value.ValueKind == JsonValueKind.String)
      {
         Console.Write(value.GetString());
         return;
      }

      if (value.ValueKind != JsonValueKind.Array)
         return;

      foreach (var part in value.EnumerateArray())
      {
         if (part.ValueKind == JsonValueKind.Object
             && part.TryGetProperty("type", out var type)
             && type.ValueKind == JsonValueKind.String
             && type.GetString() == "text"
             && part.TryGetProperty("text", out var text)
             && text.ValueKind == JsonValueKind.String)
         {
            Console.Write(text.GetString());
         }
      }
   }

   private async Task ToolCallingExampleAsync()
   {
      Console.WriteLine("\n=== Tool Calling Example ===");

      var tools = new List<Tool>
        {
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "get_weather",
                    Description = "Get the current weather for a location",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The city and state, e.g. San Francisco, CA"
                            }
                        },
                        ["required"] = new[] { "location" }
                    }
                }
            }
        };

      var request = new ChatCompletionRequest
      {
         Model = "mistral-large-latest",
         Messages =
          [
              new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = "What's the weather like in Paris?"
                }
          ],
         Tools = tools,
         ToolChoice = ToolChoice.Auto
      };

      var response = await mistralService.CreateChatCompletionAsync(request);
      if (response.Choices[0].Message.ToolCalls is { Count: > 0 })
      {
         Console.WriteLine("Model requested tool calls:");
         foreach (var toolCall in response.Choices[0].Message.ToolCalls)
         {
            Console.WriteLine($"- {toolCall.Function?.Name}({toolCall.Function?.Arguments})");
         }
      }
      else
      {
         Console.WriteLine($"Response: {response.Choices[0].Message.Content}");
      }
   }
}
