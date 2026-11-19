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
       var apiKey = context.Configuration["MistralAi:ApiKey"];
       if (string.IsNullOrWhiteSpace(apiKey))
       {
          throw new InvalidOperationException(
              "MistralAi:ApiKey is not configured. Set it using Manage User Secrets or the MistralAi__ApiKey environment variable.");
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

// --- Working directory resolution --------------------------------------
// The agent should operate on your project, not on bin\Debug\net10.0 (which
// is where it lands when you launch the built exe). Two ways to control it:
//   1. "WorkingDirectory" in appsettings.json (or --WorkingDirectory on the
//      command line / WorkingDirectory=... env var, since the host binds all
//      three). Ideal for demos: point it at the repo or project you want live.
//   2. Auto-detection: if unset, walk up from wherever we started until a
//      .csproj appears and use that folder.
// This must run before ChatDemo is resolved: its constructor records the
// working directory in the system prompt.
var configuration = host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
var configuredWorkingDirectory = configuration["WorkingDirectory"];

if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
{
   var workingDirectory = Path.GetFullPath(configuredWorkingDirectory);
   if (!Directory.Exists(workingDirectory))
      throw new DirectoryNotFoundException($"Configured WorkingDirectory not found: {workingDirectory}");

   Directory.SetCurrentDirectory(workingDirectory);
}
else
{
   // This is very simple auto-detection: it just climbs until it finds a .csproj. this is not a robust solution, but it works for the sample app. A more robust solution would be to use the MSBuild APIs to find the project root.
   var dir = new System.IO.DirectoryInfo(Directory.GetCurrentDirectory());
   while (dir is not null
       && dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).Length == 0)
   {
      dir = dir.Parent;
   }

   if (dir is not null && dir.FullName != Directory.GetCurrentDirectory())
      Directory.SetCurrentDirectory(dir.FullName);
}

Console.WriteLine($"Working directory: {Directory.GetCurrentDirectory()}");

var demo = host.Services.GetRequiredService<ChatDemo>();
await demo.RunAsync();
