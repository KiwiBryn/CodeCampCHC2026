using MistralAi.Agents.Models;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MistralAi.Agents.Services;

public class MistralAiService : IMistralAiService
{
   private readonly HttpClient _httpClient;
   private readonly JsonSerializerOptions _jsonOptions;

   public MistralAiService(HttpClient httpClient)
   {
      _httpClient = httpClient;
      // Streaming agent turns legitimately run for minutes; the old 90s cap
      // timed out mid-generation. Cancellation is owned by the caller instead.
      _httpClient.Timeout = Timeout.InfiniteTimeSpan;
      _jsonOptions = new JsonSerializerOptions
      {
         PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
      };
      _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
   }

   public async Task<ChatCompletionResponse> CreateChatCompletionAsync(
       ChatCompletionRequest request,
       CancellationToken cancellationToken = default)
   {
      using var response = await _httpClient.PostAsJsonAsync(
          "chat/completions",
          request,
          _jsonOptions,
          cancellationToken)
          .ConfigureAwait(false);

       await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
 
       return await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
   }

   public async IAsyncEnumerable<ChatCompletionStreamResponse> CreateChatCompletionStreamAsync(
       ChatCompletionRequest request,
       [EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      request.Stream = true;
      var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
      {
         Content = JsonContent.Create(request, options: _jsonOptions)
      };

      using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
          .ConfigureAwait(false);

       await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
 
       using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
          .ConfigureAwait(false);
      using var reader = new StreamReader(stream);
      var eventData = new StringBuilder();

      while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
      {
         if (line.Length == 0)
         {
            if (eventData.Length == 0)
               continue;

            var data = eventData.ToString();
            eventData.Clear();
            if (data == "[DONE]")
               yield break;

            yield return DeserializeChunk(data);
            continue;
         }

         if (line.StartsWith("data:", StringComparison.Ordinal))
         {
            if (eventData.Length > 0)
               eventData.Append('\n');
            eventData.Append(line.AsSpan(5).TrimStart());
         }
      }

      if (eventData.Length > 0)
      {
         var data = eventData.ToString();
         if (data != "[DONE]")
            yield return DeserializeChunk(data);
      }

      ChatCompletionStreamResponse DeserializeChunk(string data) =>
          JsonSerializer.Deserialize<ChatCompletionStreamResponse>(data, _jsonOptions)
                    ?? throw new JsonException("The Mistral streaming response contained an empty data event.");
             }

             private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
             {
                if (response.IsSuccessStatusCode) return;

                var details = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Mistral API returned {(int)response.StatusCode} ({response.ReasonPhrase}): {details}",
                    null,
                    response.StatusCode);
             }
          }