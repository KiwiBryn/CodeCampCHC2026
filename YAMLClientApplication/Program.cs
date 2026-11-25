using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MistralAi.Agents.Models;
using MistralAi.Agents.Services;
using System.Net.Http.Headers;
using SampleConsoleApp;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, configuration) =>
    {
       configuration.AddUserSecrets<Program>(optional: true);
       configuration.AddEnvironmentVariables();
       if (args.Length > 0)
       {
          configuration.AddCommandLine(args);
       }
    })
    .ConfigureServices((context, services) =>
    {
       var apiKey = context.Configuration["Foehn:ApiKey"];
       if (string.IsNullOrWhiteSpace(apiKey))
       {
          throw new InvalidOperationException(
              "Foehn:ApiKey is not configured. Set it using Manage User Secrets or the Foehn__ApiKey environment variable.");
       }

       services.AddHttpClient<IMistralAiService, MistralAiService>(client =>
       {
          client.BaseAddress = new Uri("https://api.mistral.ai/v1/");
          client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", apiKey);
       });

       services.AddTransient<ChatDemo>();
    })
    .Build();

var demo = host.Services.GetRequiredService<ChatDemo>();
await demo.RunAsync();
