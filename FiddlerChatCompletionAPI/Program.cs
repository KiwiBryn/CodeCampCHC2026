// The user wants a .NET 10 C# console app that calls Mistral's chat completions API with user input, looping until blank line
// - .NET 10 (use `net10.0` target framework)
// - Console app
// - HttpClient to POST to https://api.mistral.ai/v1/chat/completions
// - API key from environment variable (best practice) — MISTRAL_API_KEY
// - Loop reading Console.ReadLine() until blank (null or whitespace)
// - No context management: send only the single user message each time
// - Model: e.g., "mistral-small-latest" or "mistral-large-latest"
// — make it configurable via env var or constant with sensible default
// - Parse JSON response with System.Text.Json, extract choices[0].message.content
// - Handle errors gracefully (HTTP non-success, exceptions)
// - Use async Main
// - Maybe use IHttpClientFactory? For a simple console app, a single static HttpClient is fine. Could show typed client but keep simple.
// - .NET 10 features: could use `field` keyword? Not necessary. Keep it clean.Maybe use collection expressions, etc. Keep it simple and correct.
// - Use `JsonSerializer` with source-gen?
// - Could use JsonSerializerContext for trimming/AOT friendliness. Nice touch for .NET 10.But keep readable.
//    I'll use a JsonSerializerContext for good practice, or just JsonDocument.
//    Actually JsonDocument is simplest for reading response.
//    For request, build with anonymous type or a record + context. I'll use a small request DTO with JsonSerializerContext to be modern,
//    or just JsonNode/JsonObject. Simplicity: use JsonSerializer with anonymous object and JsonSerializerOptions with camelCase...
//    Actually API expects camelCase JSON: model, messages, etc. Default System.Text.Json serializes PascalCase unless options.
//    Use JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase } or write explicit lowercase property names.
//    Cleaner: define records with [JsonPropertyName].
// Let me write the code:
//
// Configuration
const string Endpoint = "https://api.mistral.ai/v1/chat/completions";
const string DefaultModel = "mistral-small-latest"; // BHL
//const string DefaultModel = "mistral-medium-latest"; // BHL
//const string DefaultModel = "mistral-large-latest"; // BHL
//const string DefaultModel = "codestral-latest"; // BHL

var apiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
   ?? throw new InvalidOperationException("Set the MISTRAL_API_KEY environment variable.");

var model = Environment.GetEnvironmentVariable("MISTRAL_MODEL") ?? DefaultModel;

using var http = new HttpClient();

http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

Console.WriteLine($"Mistral chat ({model}) 1.FiddlerChatCompletionAPI");
Console.WriteLine("Type a message and press Enter. Blank line to quit.");
Console.WriteLine();

while (true)
{
   Console.Write(">");
   var input = Console.ReadLine();

   if (string.IsNullOrWhiteSpace(input))
      break;

   var request = new ChatRequest(model, [new ChatMessage("user", input)]);

   try
   {
      using var response = await http.PostAsJsonAsync(Endpoint, request, jsonOptions);
      var body = await response.Content.ReadAsStringAsync();
      if (!response.IsSuccessStatusCode)
      {
         Console.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

         continue;
      }

      using var doc = JsonDocument.Parse(body);
      var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message")
         .GetProperty("content")
         .GetString();
      Console.WriteLine(content);
      Console.WriteLine();
   }
   catch (HttpRequestException ex)
   {
      Console.WriteLine($"Request failed: {ex.Message}");
   }
   catch (JsonException ex)
   {
      Console.WriteLine($"Could not parse response: {ex.Message}");
   }
}
Console.WriteLine("Bye!");

record ChatRequest(string Model, ChatMessage[] Messages);
record ChatMessage(string Role, string Content);
