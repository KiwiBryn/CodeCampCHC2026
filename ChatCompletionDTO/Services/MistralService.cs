using Microsoft.Extensions.Configuration;
using MistralClient.Models.Shared;
using MistralClient.Models.Streaming;
using MistralClient.Models.Synchronous;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MistralClient.Services;

public class MistralService : IMistralService
{
   private readonly HttpClient _httpClient;
   private readonly string _apiKey;
   private readonly string _defaultModel;

   public MistralService(HttpClient httpClient, IConfiguration configuration)
   {
      _httpClient = httpClient;
      _apiKey = configuration["FOEHN_API_KEY"]
          ?? throw new InvalidOperationException("FOEHN_API_KEY environment variable is not set");

      _defaultModel = configuration["FOEHN_MODEL"] ?? "mistral-small-latest";

      _httpClient.BaseAddress = new Uri("https://api.mistral.ai/v1/");
      _httpClient.DefaultRequestHeaders.Authorization =
          new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
   }

   public async Task<ChatCompletionResponse> GetChatCompletionAsync(
       List<MessageBase> messages,
       string? model = null,
       double? temperature = null,
       int? maxTokens = null,
       bool? stream = false,
       CancellationToken cancellationToken = default)
   {
      var request = new
      {
         model = model ?? _defaultModel,
         messages,
         temperature,
         max_tokens = maxTokens,
         stream
      };

      var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
      {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      });

      // Console.WriteLine(requestJson); BHL debugging

      using var content = new StringContent(
          requestJson,
          System.Text.Encoding.UTF8,
          "application/json");

      using var response = await _httpClient.PostAsync(
          "chat/completions",
          content,
          cancellationToken);

      /* BHL
      var response = await _httpClient.PostAsJsonAsync(
          "chat/completions",
          request,
          cancellationToken: cancellationToken);
      */

      response.EnsureSuccessStatusCode();

      return await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
          ?? throw new JsonException("Failed to deserialize response");
   }

   public async IAsyncEnumerable<StreamingResponse> StreamChatCompletionAsync(
       List<MessageBase> messages,
       string? model = null,
       double? temperature = null,
       int? maxTokens = null,
       [EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      var request = new
      {
         model = model ?? _defaultModel,
         messages,
         temperature,
         max_tokens = maxTokens,
         stream = true
      };

      using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
      {
         Content = JsonContent.Create(request)
      };

      using var response = await _httpClient.SendAsync(
          httpRequest,
          HttpCompletionOption.ResponseHeadersRead,
          cancellationToken);

      response.EnsureSuccessStatusCode();

      using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var reader = new StreamReader(stream);

      var jsonSerializerOptions = new JsonSerializerOptions
      {
         PropertyNameCaseInsensitive = true
      };

      string? line;
      while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
      {
         if (string.IsNullOrWhiteSpace(line) || line.StartsWith("data: [DONE]"))
            continue;

         if (line.StartsWith("data: "))
         {
            line = line[6..];
            var chunk = JsonSerializer.Deserialize<StreamingResponse>(line, jsonSerializerOptions);
            if (chunk != null)
            {
               yield return chunk;
            }
         }
      }
   }
}