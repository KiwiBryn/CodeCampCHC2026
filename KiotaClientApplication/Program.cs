using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Mistral;
using Mistral.Models;

var apiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
    ?? throw new InvalidOperationException("MISTRAL_API_KEY not configured.");

var adapter = new HttpClientRequestAdapter(
    new BearerAuthenticationProvider(apiKey));

var client = new MistralChatCompletionClient(adapter);


var request = new ChatCompletionRequest
{
   //Model = "mistral-large-latest",
   Model = "mistral-small-latest",

   Messages =
    [
      new ChatCompletionRequest.ChatCompletionRequest_messages
        {
            SystemMessage = new SystemMessage
            {
                Content = new SystemMessage.SystemMessage_content
                {
                    String = File.ReadAllText("system.md")
                }
            },

        },
        new ChatCompletionRequest.ChatCompletionRequest_messages
        {
            UserMessage = new UserMessage
            {
                Content = new UserMessage.UserMessage_content
                {
                    //String = "Generate a console application that displays \"hello world\""
                    String = "Generate a minimalAPI application which returned the weather for a given location"
                }
            }
        },
   ],
   ToolChoice = new ChatCompletionRequest.ChatCompletionRequest_tool_choice
   {
      ToolChoiceEnum = ToolChoiceEnum.Auto
   },

   Tools = new List<CustomConnector>
   {
     new CustomConnector
        {
            AdditionalData = new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = "get_weather",
                    ["description"] = "Get current weather for a location",
                    ["parameters"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "City name"
                            }
                        },
                        ["required"] = new string[] { "location" }
                    }
                }
            }
        } 
   }
};

var response = await client
    .V1
    .Chat
    .Completions
    .PostAsync(request);


foreach (var choice in response.Choices ?? new List<ChatCompletionChoice>())
{
   if (choice.Message?.Content?.String is not null)
   {
      Console.WriteLine(choice.Message.Content.String);
   }

   foreach (var contentChunk in choice?.Message?.Content?.ContentChunk ?? new List<ContentChunk>())
   {
      foreach (var additionalData in contentChunk.AdditionalData ?? new Dictionary<string, object>())
      {
         Console.WriteLine($"{additionalData.Key} {additionalData.Value}");
      }
   }

   Console.WriteLine($"Token Usage: {response.Usage?.PromptTokens} {response.Usage?.CompletionTokens} {response.Usage?.TotalTokens}");
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();
