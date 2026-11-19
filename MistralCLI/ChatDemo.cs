using MistralAi.Agents.Models;
using MistralAi.Agents.Services;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SampleConsoleApp;

public class ChatDemo
{
   private readonly IMistralAiService mistralService;
   private List<ChatMessage> _messages = new();
   private string _systemPrompt = "You are a helpful, concise assistant that provides accurate information.If you do not know the answer, say so rather than making something up.";
   private string _model = "mistral-small-latest";
   private float _temperature = 0.7f;
   private const int MaxToolRounds = 10;
   private const string ContextFileName = "context.json";
   private string _systemFileName = "system.md";
   private readonly Dictionary<string, Skill> _loadedSkills = new();
   private readonly string _skillsDirectory = "skills";
   private readonly string _skillsManifestFile = "skills.json";

   private readonly Dictionary<string, Func<string, Task<object>>> _toolFunctions = new()
   {
      ["scan_folder_tree"] = ScanFolderTreeAsync,
      ["read_text_file"] = ReadTextFileAsync,
      ["write_text_file"] = WriteTextFileAsync,
      ["scan_folder"] = ScanFolderAsync,
      ["delete_file"] = DeleteFileAsync,
      ["delete_folder"] = DeleteFolderAsync,
      ["create_folder"] = CreateFolderAsync
   };

   public ChatDemo(IMistralAiService mistralService)
   {
      this.mistralService = mistralService;
      // Initialize skills directory if it doesn't exist
      if (!Directory.Exists(_skillsDirectory))
      {
         Directory.CreateDirectory(_skillsDirectory);
      }

      if (File.Exists(_systemFileName))
      {
         _systemPrompt = File.ReadAllText(_systemFileName);
      }

      // Tell the model where it is, so it never needs a scan just to orient itself
      _systemPrompt += $"Working directory: {Environment.CurrentDirectory}";

      // And where the repository root is, so it never walks up the tree to find siblings
      var repoRoot = FindRepositoryRoot(Environment.CurrentDirectory);
      if (repoRoot != null)
         _systemPrompt += $"Repository root: {repoRoot}";
   }

   public async Task RunAsync()
   {
      try
      {
         // Load tools from JSON file
         var tools = await LoadToolsFromFileAsync("tools.json");

         // Load previous context first so default skills are applied on top of it
         // (loading it afterwards would overwrite the skills' messages and settings)
         await LoadContextAsync();

         await LoadSkillsManifestAsync();

         Console.WriteLine("\n=== AI Assistant with Context ===");
         Console.WriteLine("Available commands:");
         Console.WriteLine("- /exit - Exit the application");
         Console.WriteLine("- /save - Save current conversation context");
         Console.WriteLine("- /clear - Clear conversation context (brain wipe)");
         Console.WriteLine("- /skills - Show available skills");
         Console.WriteLine("- /skill <skill_name> - load or unload a skill");
         Console.WriteLine("- /help - Show this help message");
         Console.WriteLine("\nType your message to start chatting...");

         // Main chat loop
         await InteractiveChatWithToolsAsync(tools);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error: {ex.Message}");
      }
   }

   private async Task InteractiveChatWithToolsAsync(List<Tool> tools)
   {
      while (true)
      {
         Console.Write("\n> ");
         var input = Console.ReadLine();

         if (string.IsNullOrWhiteSpace(input))
            continue;

         // Handle special commands
         if (input.StartsWith("/"))
         {
            await HandleCommandAsync(input);
            continue;
         }

         // Add user message to context
         _messages.Add(new ChatMessage { Role = MessageRole.User, Content = input });

         // Prepare request
         var request = new ChatCompletionRequest
         {
            Model = _model,
            Messages = _messages,
            Tools = tools,
            ToolChoice = ToolChoice.Auto,
            Temperature = _temperature
         };

         try
         {
            /* This is the sync version that waits for the entire response before printing it, which can be slow for long responses or tool calls.
            var response = await mistralService.CreateChatCompletionAsync(request);

            if (response.Choices[0].Message.ToolCalls is { Count: > 0 } toolCalls)
            {
               _messages.Add(response.Choices[0].Message);
               await ProcessToolCallsAsync(toolCalls, tools);
            }
            else
            {
               // Regular response
               Console.WriteLine(GetText(response.Choices[0].Message.Content));

               Console.WriteLine($"Token Usage Prompt:{response.Usage?.PromptTokens} Response:{response.Usage?.CompletionTokens} Total:{response.Usage?.TotalTokens}");

               _messages.Add(response.Choices[0].Message);
            }
            */
            var message = await CreateStreamingChatMessageAsync(request);

            if (message.ToolCalls is { Count: > 0 } toolCalls)
            {
               _messages.Add(message);
               await ProcessToolCallsAsync(toolCalls, tools);
            }
            else
            {
               _messages.Add(message);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error: {ex.Message}");
         }
      }
   }

   private async Task HandleCommandAsync(string command)
   {
      var parts = command.Trim().Split(' ', 2);
      var cmd = parts[0].ToLower();
      var arg = parts.Length > 1 ? parts[1] : string.Empty;

      switch (cmd)
      {
         case "/exit":
            await SaveContextAsync();
            Console.WriteLine("Goodbye!");
            Environment.Exit(0);
            break;

         case "/save":
            await SaveContextAsync();
            Console.WriteLine("Context saved to context.json");
            break;

         case "/clear":
            _messages.Clear();
            _messages.Add(new ChatMessage { Role = MessageRole.System, Content = _systemPrompt });
            Console.WriteLine("Conversation context cleared (brain wipe performed).");
            break;

         case "/skill":
            await HandleSkillCommandAsync(arg);
            break;

         case "/skills":
            await ListSkillsAsync();
            break;

         case "/help":
            ShowHelp();
            break;

         default:
            Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
            break;
      }
   }

   /// <summary>
   /// Extracts plain text from a message's Content, which may be a string, a JSON string,
   /// or an array of content chunks ([{"type":"text","text":"..."}]). Using ToString() on a
   /// JsonElement array/object returns raw JSON with escaped newlines and quotes.
   /// </summary>
   private static string GetText(object? content) => content switch
   {
      null => string.Empty,
      string s => s,
      JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
      JsonElement { ValueKind: JsonValueKind.Array } e => string.Concat(
         e.EnumerateArray()
          .Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
          .Select(c => c.GetProperty("text").GetString())),
      _ => content.ToString() ?? string.Empty
   };

   private static void ShowHelp()
   {
      Console.WriteLine("\nAvailable commands:");
      Console.WriteLine("- /exit - Exit the application");
      Console.WriteLine("- /save - Save current conversation context");
      Console.WriteLine("- /clear - Clear conversation context (brain wipe)");
      Console.WriteLine("- /skill {filename} - Load/unload a skill (toggle)");
      Console.WriteLine("- /skills - List all currently loaded skills");
      Console.WriteLine("- /help - Show this help message");
      Console.WriteLine("\nType your message to continue chatting...");
   }

   private async Task HandleSkillCommandAsync(string skillName)
   {
      if (string.IsNullOrWhiteSpace(skillName))
      {
         Console.WriteLine("Usage: /skill {filename} (without .json extension)");
         return;
      }

      // Try with and without .json extension
      var skillFiles = new[]
      {
        Path.Combine(_skillsDirectory, $"{skillName}.json"),
        Path.Combine(_skillsDirectory, skillName)
    };

      string? skillFile = skillFiles.FirstOrDefault(File.Exists);

      if (skillFile == null)
      {
         Console.WriteLine($"Skill file not found: {skillName}.json in {_skillsDirectory} directory");
         return;
      }

      // Check if skill is already loaded
      if (_loadedSkills.TryGetValue(skillName, out var existingSkill))
      {
         // Unload the skill
         UnloadSkill(skillName);
         Console.WriteLine($"Skill '{skillName}' unloaded from context.");
      }
      else
      {
         // Load the skill
         try
         {
            var skill = await LoadSkillFromFileAsync(skillFile);
            if (skill != null)
            {
               LoadSkill(skill);
               Console.WriteLine($"Skill '{skill.Name}' loaded from {skillFile}");
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error loading skill: {ex.Message}");
         }
      }
   }

   private async Task ListSkillsAsync()
   {
      if (_loadedSkills.Count == 0)
      {
         Console.WriteLine("No skills currently loaded.");
         return;
      }

      Console.WriteLine("\nLoaded Skills:");
      foreach (var skill in _loadedSkills.Values)
      {
         Console.WriteLine($"- {skill.Name}: {skill.Description ?? "No description"}");
      }
   }

   private async Task<Skill?> LoadSkillFromFileAsync(string filePath)
   {
      var json = await File.ReadAllTextAsync(filePath);
      var options = new JsonSerializerOptions
      {
         PropertyNameCaseInsensitive = true,
         Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
      };
      var skill = JsonSerializer.Deserialize<Skill>(json, options);

      if (skill != null)
      {
         skill.FileName = Path.GetFileNameWithoutExtension(filePath);
         return skill;
      }
      return null;
   }

   private void LoadSkill(Skill skill)
   {
      // Store the skill
      _loadedSkills[skill.Name] = skill;

      // Apply skill configuration
      if (skill.Temperature.HasValue)
      {
         _temperature = skill.Temperature.Value;
      }

      if (!string.IsNullOrEmpty(skill.SystemPrompt))
      {
         // Update or add system prompt
         var systemMessage = _messages.FirstOrDefault(m => m.Role == MessageRole.System);
         if (systemMessage != null)
         {
            // Skip if already present (e.g. restored from a saved context)
            var existing = GetText(systemMessage.Content);
            if (!existing.Contains(skill.SystemPrompt))
            {
               systemMessage.Content = $"{existing}\n\n{skill.SystemPrompt}";
            }
         }
         else
         {
            _messages.Insert(0, new ChatMessage
            {
               Role = MessageRole.System,
               Content = $"{_systemPrompt}\n\n{skill.SystemPrompt}"
            });
         }
      }

      // Add skill messages to context
      if (skill.Messages != null)
      {
         foreach (var message in skill.Messages)
         {
            if (!_messages.Any(m => m.Role == message.Role && m.Content == message.Content))
            {
               _messages.Add(message);
            }
         }
      }

      // Register skill tools
      if (skill.Tools != null)
      {
         foreach (var tool in skill.Tools)
         {
            if (tool.Function != null && !string.IsNullOrEmpty(tool.Function.Name))
            {
               // Check if this tool is already registered
               if (!_toolFunctions.ContainsKey(tool.Function.Name))
               {
                  // For now, we'll just add a placeholder. In a real implementation,
                  // you'd want to dynamically register the actual function.
                  _toolFunctions[tool.Function.Name] = async (args) =>
                  {
                     return new { error = $"Tool {tool.Function.Name} is not implemented" };
                  };
               }
            }
         }
      }
   }

   private void UnloadSkill(string skillName)
   {
      if (!_loadedSkills.TryGetValue(skillName, out var skill))
         return;

      // Remove skill messages from context
      if (skill.Messages != null)
      {
         foreach (var message in skill.Messages)
         {
            _messages.RemoveAll(m =>
                m.Role == message.Role &&
                m.Content == message.Content &&
                m.ToolCalls == message.ToolCalls &&
                m.ToolCallId == message.ToolCallId);
         }
      }

      // Remove skill system prompt if it was added
      if (!string.IsNullOrEmpty(skill.SystemPrompt))
      {
         var systemMessage = _messages.FirstOrDefault(m => m.Role == MessageRole.System);
         if (systemMessage != null)
         {
            systemMessage.Content = GetText(systemMessage.Content).Replace(skill.SystemPrompt, "").Trim();
         }
      }

      // Remove skill tools
      if (skill.Tools != null)
      {
         foreach (var tool in skill.Tools)
         {
            if (tool.Function != null)
            {
               _toolFunctions.Remove(tool.Function.Name);
            }
         }
      }

      // Remove from loaded skills
      _loadedSkills.Remove(skillName);
   }

   private async Task LoadSkillsManifestAsync()
   {
      var manifestPath = Path.Combine(_skillsDirectory, _skillsManifestFile);
      if (!File.Exists(manifestPath))
         return;

      try
      {
         var json = await File.ReadAllTextAsync(manifestPath);
         var manifest = JsonSerializer.Deserialize<SkillManifest>(json);

         if (manifest?.DefaultSkills != null)
         {
            foreach (var skillName in manifest.DefaultSkills)
            {
               var skillFile = Path.Combine(_skillsDirectory, $"{skillName}.json");
               if (File.Exists(skillFile))
               {
                  var skill = await LoadSkillFromFileAsync(skillFile);
                  if (skill != null)
                  {
                     LoadSkill(skill);
                     Console.WriteLine($"Loaded default skill: {skill.Name}");
                  }
               }
            }
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Warning: Could not load skills manifest - {ex.Message}");
      }
   }


   private async Task LoadContextAsync()
   {
      if (!File.Exists(ContextFileName))
      {
         // Initialize with system prompt
         _messages.Add(new ChatMessage { Role = MessageRole.System, Content = _systemPrompt });
         return;
      }

      try
      {
         var json = await File.ReadAllTextAsync(ContextFileName);
         var context = JsonSerializer.Deserialize<ChatContext>(json);

         if (context != null)
         {
            _systemPrompt = context.SystemPrompt ?? _systemPrompt;
            _messages = context.Messages;
            _model = context.Model;
            _temperature = context.Temperature;

            // Ensure we have at least the system prompt
            if (!_messages.Any(m => m.Role == MessageRole.System))
            {
               _messages.Insert(0, new ChatMessage { Role = MessageRole.System, Content = _systemPrompt });
            }

            Console.WriteLine($"Loaded context from {ContextFileName} (last updated: {context.LastUpdated:g})");
            Console.WriteLine($"Model: {_model}, Temperature: {_temperature}");
         }
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Warning: Could not load context - {ex.Message}");
         _messages.Add(new ChatMessage { Role = MessageRole.System, Content = _systemPrompt });
      }
   }

   private async Task SaveContextAsync()
   {
      try
      {
         var context = new ChatContext
         {
            SystemPrompt = _systemPrompt,
            Messages = _messages,
            Model = _model,
            Temperature = _temperature,
            LastUpdated = DateTimeOffset.Now
         };

         var json = JsonSerializer.Serialize(context, new JsonSerializerOptions
         {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
         });

         await File.WriteAllTextAsync(ContextFileName, json);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error saving context: {ex.Message}");
      }
   }

   /// <summary>
   /// Streams a completion, printing content live as it arrives, and merges
   /// streamed tool-call fragments (id/name on the first chunk for an index,
   /// argument text in pieces afterwards) back into full ToolCall objects.
   /// </summary>
   private async Task<ChatMessage> CreateStreamingChatMessageAsync(ChatCompletionRequest request)
   {
      request.StreamOptions = new StreamOptions { IncludeUsage = true };

      var contentBuilder = new System.Text.StringBuilder();
      var toolCalls = new SortedDictionary<int, ToolCall>();
      Usage? usage = null;

      await foreach (var chunk in mistralService.CreateChatCompletionStreamAsync(request))
      {
         usage ??= chunk.Usage;

         foreach (var choice in chunk.Choices)
         {
            var delta = choice.Delta;

            if (delta.Content is { ValueKind: JsonValueKind.String } contentElement
                && contentElement.GetString() is { } text && text.Length > 0)
            {
               Console.Write(text);
               contentBuilder.Append(text);
            }

            if (delta.ToolCalls is { } fragments)
            {
               foreach (var fragment in fragments)
               {
                  if (!toolCalls.TryGetValue(fragment.Index, out var existing))
                  {
                     existing = new ToolCall
                     {
                        Id = fragment.Id,
                        Type = ToolType.Function,
                        Function = new FunctionCall { Name = fragment.Function?.Name ?? "", Arguments = "" }
                     };
                     toolCalls[fragment.Index] = existing;
                  }
                  if (!string.IsNullOrEmpty(fragment.Id))
                     existing.Id = fragment.Id;
                  if (!string.IsNullOrEmpty(fragment.Function?.Name))
                     existing.Function!.Name = fragment.Function!.Name;
                  if (!string.IsNullOrEmpty(fragment.Function?.Arguments))
                     existing.Function!.Arguments += fragment.Function!.Arguments;
               }
            }
         }
      }

      Console.WriteLine();
      if (contentBuilder.Length > 0)
         Console.WriteLine();

      Console.WriteLine(
          $"Token Usage Prompt:{usage?.PromptTokens} Response:{usage?.CompletionTokens} Total:{usage?.TotalTokens}");

      var message = new ChatMessage
      {
         Role = MessageRole.Assistant,
         Content = contentBuilder.ToString()
      };
      if (toolCalls.Count > 0)
         message.ToolCalls = toolCalls.Values.ToList();

      return message;
   }

   private async Task ProcessToolCallsAsync(List<ToolCall> toolCalls, List<Tool> tools, int round = 1)
   {
      foreach (var toolCall in toolCalls)
      {
         if (toolCall.Function == null)
         {
            // Every tool call needs a reply or the next request is rejected
            _messages.Add(new ChatMessage
            {
               Role = MessageRole.Tool,
               Content = "Error: tool call had no function.",
               ToolCallId = toolCall.Id
            });
            continue;
         }

         if (!_toolFunctions.TryGetValue(toolCall.Function.Name, out var func))
         {
            _messages.Add(new ChatMessage
            {
               Role = MessageRole.Tool,
               Content = $"Error: unknown tool '{toolCall.Function.Name}'.",
               ToolCallId = toolCall.Id
            });
            Console.WriteLine($"\nTool {toolCall.Function.Name} not found");
         }
         else
         {
            try
            {
               // Parse arguments
               var result = await func(toolCall.Function.Arguments);
               var resultJson = JsonSerializer.Serialize(result);

               // Add tool response to messages
               _messages.Add(new ChatMessage
               {
                  Role = MessageRole.Tool,
                  Content = resultJson,
                  ToolCallId = toolCall.Id
               });

               Console.WriteLine($"\nTool {toolCall.Function.Name} executed successfully");
            }
            catch (Exception ex)
            {
               _messages.Add(new ChatMessage
               {
                  Role = MessageRole.Tool,
                  Content = $"Error: {ex.Message}",
                  ToolCallId = toolCall.Id
               });
               Console.WriteLine($"\nTool {toolCall.Function.Name} failed: {ex.Message}");
            }
         }
      }

      // Every tool call above has a reply, so stopping here leaves a valid history
      if (round >= MaxToolRounds)
      {
         Console.WriteLine($"\nStopped after {MaxToolRounds} rounds of tool calls without a final answer. Send a message to continue.");
         return;
      }

      // After processing tools, get another response from the model
      var request = new ChatCompletionRequest
      {
         Model = _model,
         Messages = _messages,
         Tools = tools,
         ToolChoice = ToolChoice.Auto,
         Temperature = _temperature
      };

      try
      {
         /*
         var response = await mistralService.CreateChatCompletionAsync(request);

         Console.WriteLine(GetText(response.Choices[0].Message.Content));

         Console.WriteLine($"Token Usage Prompt:{response.Usage?.PromptTokens} Response:{response.Usage?.CompletionTokens} Total:{response.Usage?.TotalTokens}");

         _messages.Add(response.Choices[0].Message);
         if (response.Choices[0].Message.ToolCalls is { Count: > 0 } nextToolCalls)* 
         */
         var message = await CreateStreamingChatMessageAsync(request);
         _messages.Add(message);

         if (message.ToolCalls is { Count: > 0 } nextToolCalls)
            await ProcessToolCallsAsync(nextToolCalls, tools, round + 1);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error getting response: {ex.Message}");
      }
   }

   #region File System Operations

   private static async Task<object> ScanFolderTreeAsync(string arguments)
   {
      // Values must be JsonElement: deserializing to object yields JsonElement, never string
      var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
      var folderPath = args != null && args.TryGetValue("folderPath", out var folderElement)
         && folderElement.ValueKind == JsonValueKind.String
            ? folderElement.GetString()
            : null;
      if (string.IsNullOrEmpty(folderPath))
         throw new ArgumentException("folderPath is required");

      Console.WriteLine($"Scanning folder tree at: {folderPath}");

      // Parse options with defaults
      var options = new ScanOptions();

      // Try to load default ignore lists from file
      try
      {
         if (File.Exists("scan_ignore.json"))
         {
            var ignoreJson = await File.ReadAllTextAsync("scan_ignore.json");
            var ignoreConfig = JsonSerializer.Deserialize<ScanOptions>(ignoreJson);

            if (ignoreConfig != null)
            {
               options.IgnoreFolders = ignoreConfig.IgnoreFolders ?? options.IgnoreFolders;
               options.IgnoreFiles = ignoreConfig.IgnoreFiles ?? options.IgnoreFiles;
               options.IgnoreExtensions = ignoreConfig.IgnoreExtensions ?? options.IgnoreExtensions;
            }
         }
      }
      catch (Exception ex)
      {
         // Continue with defaults, but don't hide the problem
         Console.WriteLine($"Warning: could not load scan_ignore.json - {ex.Message}");
      }

      // Override with any options from the arguments
      if (args != null)
      {
         if (args.TryGetValue("mode", out var modeObj) && modeObj.ValueKind == JsonValueKind.String)
         {
            var modeStr = modeObj.GetString()!;
            if (!Enum.TryParse<ScanMode>(modeStr, true, out var mode) || !Enum.IsDefined(mode))
               throw new ArgumentException($"Invalid scan mode: {modeStr}");
            options.Mode = mode;
         }
         if (args.TryGetValue("maxDepth", out var depthObj) && depthObj is JsonElement depthElement)
         {
            options.MaxDepth = depthElement.GetInt32();
         }
         if (args.TryGetValue("filePatterns", out var patternsObj) && patternsObj is JsonElement patternsElement)
         {
            options.FilePatterns = patternsElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
         }
         if (args.TryGetValue("ignoreFolders", out var ignoreFoldersObj) && ignoreFoldersObj is JsonElement ignoreFoldersElement)
         {
            var ignoreFolders = ignoreFoldersElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            options.IgnoreFolders = options.IgnoreFolders?.Concat(ignoreFolders).Distinct().ToArray() ?? ignoreFolders;
         }
         if (args.TryGetValue("ignoreFiles", out var ignoreFilesObj) && ignoreFilesObj is JsonElement ignoreFilesElement)
         {
            var ignoreFiles = ignoreFilesElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            options.IgnoreFiles = options.IgnoreFiles?.Concat(ignoreFiles).Distinct().ToArray() ?? ignoreFiles;
         }
         if (args.TryGetValue("ignoreExtensions", out var ignoreExtObj) && ignoreExtObj is JsonElement ignoreExtElement)
         {
            var ignoreExtensions = ignoreExtElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            options.IgnoreExtensions = options.IgnoreExtensions?.Concat(ignoreExtensions).Distinct().ToArray() ?? ignoreExtensions;
         }
         if (args.TryGetValue("caseSensitive", out var caseSensitiveObj) && caseSensitiveObj is JsonElement caseElement)
         {
            options.CaseSensitive = caseElement.GetBoolean();
         }
      }

      if (!Directory.Exists(folderPath))
         throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

      var (rootNode, totalFiles, totalDirs, totalSize) = BuildFileTree(folderPath, options);

      // Return a compact text tree instead of the full JSON node graph:
      // the JSON form (with paths, sizes and timestamps per node) can cost
      // tens of thousands of prompt tokens on a real repo, dominating latency.
      return FormatFileTree(folderPath, rootNode, totalFiles, totalDirs);
   }

   private const int MaxTreeEntries = 400;

   private static string FormatFileTree(
       string rootPath, FileSystemNode root, int totalFiles, int totalDirs)
   {
      var sb = new System.Text.StringBuilder();
      sb.AppendLine($"Root: {rootPath}");

      var shown = 0;
      var truncated = false;

      // Classic ASCII tree:
      //   +-- src/
      //   |   +-- MistralCLI/
      //   |       +-- Program.cs
      //   +-- README.md
      void Walk(FileSystemNode node, string prefix)
      {
         if (truncated || node.Children == null) return;
         for (var i = 0; i < node.Children.Count; i++)
         {
            var child = node.Children[i];
            if (shown >= MaxTreeEntries)
            {
               truncated = true;
               return;
            }

            var isLast = i == node.Children.Count - 1;
            sb.AppendLine(prefix + (isLast ? "+-- " : "|-- ") + (child.IsDirectory ? child.Name + "/" : child.Name));
            shown++;

            if (child.IsDirectory)
               Walk(child, prefix + (isLast ? "    " : "|   "));
         }
      }

      Walk(root, "");

      sb.AppendLine(
          $"{totalFiles} files, {totalDirs} directories."
          + (truncated
              ? $" Output truncated after {MaxTreeEntries} entries; call scan_folder on a subfolder to expand it."
              : string.Empty));

      return sb.ToString();
   }

   private static (FileSystemNode, int, int, long) BuildFileTree(string rootPath, ScanOptions options)
   {
      var rootNode = new FileSystemNode
      {
         Name = Path.GetFileName(rootPath) ?? rootPath,
         Path = rootPath,
         IsDirectory = true,
         LastModified = Directory.GetLastWriteTime(rootPath),
         Depth = 0
      };

      var totalFiles = 0;
      var totalDirs = 1; // Count the root directory
      var totalSize = 0L;

      if (options.Mode is ScanMode.Flat or ScanMode.BreadthFirst)
      {
         ProcessDirectoryBreadthFirst(rootPath, rootNode, options, ref totalFiles, ref totalDirs, ref totalSize);
      }
      else
      {
         ProcessDirectoryDepthFirst(rootPath, rootNode, options, ref totalFiles, ref totalDirs, ref totalSize);
      }

      return (rootNode, totalFiles, totalDirs, totalSize);
   }

   private static void ProcessDirectoryBreadthFirst(
       string rootPath,
       FileSystemNode rootNode,
       ScanOptions options,
       ref int totalFiles,
       ref int totalDirs,
       ref long totalSize)
   {
      var maxDepth = options.Mode == ScanMode.Flat ? 1 : options.MaxDepth;
      var queue = new Queue<(string Path, FileSystemNode Node, int CurrentDepth)>();
      queue.Enqueue((rootPath, rootNode, 0));

      while (queue.Count > 0)
      {
         var (currentPath, currentNode, currentDepth) = queue.Dequeue();

         if (currentDepth >= maxDepth) continue;

         try
         {
            if (options.IncludeDirectories)
            {
               foreach (var dir in Directory.GetDirectories(currentPath))
               {
                  if (ShouldIgnore(dir, true, options)) continue;

                  var dirInfo = new System.IO.DirectoryInfo(dir);
                  var dirNode = new FileSystemNode
                  {
                     Name = dirInfo.Name,
                     Path = dirInfo.FullName,
                     IsDirectory = true,
                     LastModified = dirInfo.LastWriteTime,
                     Depth = currentDepth + 1
                  };

                  if (currentNode.Children == null)
                     currentNode.Children = new List<FileSystemNode>();
                  currentNode.Children.Add(dirNode);
                  totalDirs++;

                  queue.Enqueue((dirInfo.FullName, dirNode, currentDepth + 1));
               }
            }

            if (options.IncludeFiles)
            {
               foreach (var file in Directory.GetFiles(currentPath))
               {
                  if (ShouldIgnore(file, false, options)) continue;
                  if (!MatchesPatterns(file, options.FilePatterns, options)) continue;

                  var fileInfo = new System.IO.FileInfo(file);
                  var fileNode = new FileSystemNode
                  {
                     Name = fileInfo.Name,
                     Path = fileInfo.FullName,
                     IsDirectory = false,
                     Size = fileInfo.Length,
                     LastModified = fileInfo.LastWriteTime,
                     Depth = currentDepth + 1
                  };

                  if (currentNode.Children == null)
                     currentNode.Children = new List<FileSystemNode>();
                  currentNode.Children.Add(fileNode);
                  totalFiles++;
                  totalSize += fileInfo.Length;
               }
            }
         }
         catch (UnauthorizedAccessException)
         {
            // Skip directories we don't have access to
         }
      }
   }

   private static void ProcessDirectoryDepthFirst(
       string currentPath,
       FileSystemNode currentNode,
       ScanOptions options,
       ref int totalFiles,
       ref int totalDirs,
       ref long totalSize)
   {
      if (currentNode.Depth >= options.MaxDepth) return;

      try
      {
         if (options.IncludeDirectories)
         {
            foreach (var dir in Directory.GetDirectories(currentPath))
            {
               if (ShouldIgnore(dir, true, options)) continue;

               var dirInfo = new System.IO.DirectoryInfo(dir);
               var dirNode = new FileSystemNode
               {
                  Name = dirInfo.Name,
                  Path = dirInfo.FullName,
                  IsDirectory = true,
                  LastModified = dirInfo.LastWriteTime,
                  Depth = currentNode.Depth + 1
               };

               if (currentNode.Children == null)
                  currentNode.Children = new List<FileSystemNode>();
               currentNode.Children.Add(dirNode);
               totalDirs++;

               // Recursively process subdirectories
               ProcessDirectoryDepthFirst(
                   dirInfo.FullName,
                   dirNode,
                   options,
                   ref totalFiles,
                   ref totalDirs,
                   ref totalSize);
            }
         }

         if (options.IncludeFiles)
         {
            foreach (var file in Directory.GetFiles(currentPath))
            {
               if (ShouldIgnore(file, false, options)) continue;
               if (!MatchesPatterns(file, options.FilePatterns, options)) continue;

               var fileInfo = new System.IO.FileInfo(file);
               var fileNode = new FileSystemNode
               {
                  Name = fileInfo.Name,
                  Path = fileInfo.FullName,
                  IsDirectory = false,
                  Size = fileInfo.Length,
                  LastModified = fileInfo.LastWriteTime,
                  Depth = currentNode.Depth + 1
               };

               if (currentNode.Children == null)
                  currentNode.Children = new List<FileSystemNode>();
               currentNode.Children.Add(fileNode);
               totalFiles++;
               totalSize += fileInfo.Length;
            }
         }
      }
      catch (UnauthorizedAccessException)
      {
         // Skip directories we don't have access to
      }
   }

   private static bool ShouldIgnore(string path, bool isDirectory, ScanOptions options)
   {
      // Check folder ignore list
      if (isDirectory && options.IgnoreFolders != null)
      {
         var folderName = Path.GetFileName(path);
         if (options.IgnoreFolders.Contains(folderName, options.CaseSensitive
             ? StringComparer.Ordinal
             : StringComparer.OrdinalIgnoreCase))
         {
            return true;
         }

         // Check if any parent folder is in ignore list
         var pathParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
         foreach (var ignoreFolder in options.IgnoreFolders)
         {
            if (pathParts.Contains(ignoreFolder, options.CaseSensitive
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase))
            {
               return true;
            }
         }
      }

      // Check file ignore list (supports ? and * wildcards, e.g. "*.suo")
      if (!isDirectory && options.IgnoreFiles != null)
      {
         var fileName = Path.GetFileName(path);
         foreach (var ignoreFile in options.IgnoreFiles)
         {
            if (ignoreFile.Contains('*') || ignoreFile.Contains('?'))
            {
               if (WildcardMatch(fileName, ignoreFile, options.CaseSensitive))
               {
                  return true;
               }
            }
            else if (string.Equals(fileName, ignoreFile, options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase))
            {
               return true;
            }
         }
      }

      // Check extension ignore list
      if (!isDirectory && options.IgnoreExtensions != null)
      {
         var ext = Path.GetExtension(path).TrimStart('.');
         if (options.IgnoreExtensions.Contains(ext, options.CaseSensitive
             ? StringComparer.Ordinal
             : StringComparer.OrdinalIgnoreCase))
         {
            return true;
         }
      }

      // Check exclude patterns (legacy support)
      if (options.ExcludePatterns != null && options.ExcludePatterns.Length > 0)
      {
         var pathParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
         foreach (var pattern in options.ExcludePatterns)
         {
            if (pathParts.Contains(pattern, options.CaseSensitive
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase))
            {
               return true;
            }
         }
      }

      return false;
   }

   /// <summary>
   /// Case-insensitive-by-default wildcard match supporting '*' (any run of chars)
   /// and '?' (any single char). Classic two-pointer algorithm, no regex allocation.
   /// </summary>
   private static bool WildcardMatch(string input, string pattern, bool caseSensitive)
   {
      var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      int s = 0, p = 0, starP = -1, starS = 0;

      while (s < input.Length)
      {
         if (p < pattern.Length && (pattern[p] == '?' || string.Equals(input[s].ToString(), pattern[p].ToString(), comparison)))
         {
            s++;
            p++;
         }
         else if (p < pattern.Length && pattern[p] == '*')
         {
            starP = p++;
            starS = s;
         }
         else if (starP != -1)
         {
            p = starP + 1;
            s = ++starS;
         }
         else
         {
            return false;
         }
      }

      while (p < pattern.Length && pattern[p] == '*') p++;
      return p == pattern.Length;
   }

   private static bool MatchesPatterns(string filePath, string[]? filePatterns, ScanOptions options)
   {
      if (filePatterns == null || filePatterns.Length == 0)
         return true;

      var fileName = Path.GetFileName(filePath);
      var ext = Path.GetExtension(filePath).TrimStart('.');

      foreach (var pattern in filePatterns)
      {
         try
         {
            if (pattern == "*") return true;

            // Handle extension patterns (e.g., "*.txt")
            if (pattern.StartsWith("*."))
            {
               var patternExt = pattern[2..]; // Remove "*." prefix
               if (ext.Equals(patternExt, options.CaseSensitive
                   ? StringComparison.Ordinal
                   : StringComparison.OrdinalIgnoreCase))
               {
                  return true;
               }
            }
            // Handle full file name patterns
            else if (fileName.Equals(pattern, options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase))
            {
               return true;
            }
            // Handle patterns with wildcards (simplified)
            else if (pattern.Contains('*'))
            {
               // Convert to regex pattern
               var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
               if (Regex.IsMatch(fileName, regexPattern, options.CaseSensitive
                   ? RegexOptions.None
                   : RegexOptions.IgnoreCase))
               {
                  return true;
               }
            }
         }
         catch
         {
            // If pattern matching fails, include the file
         }
      }
      return false;
   }

   /// <summary>
   /// Walks up from the start directory looking for a .sln or .git marker,
   /// returning the repository root or null if none is found.
   /// </summary>
   private static string? FindRepositoryRoot(string startDirectory)
   {
      var current = new System.IO.DirectoryInfo(startDirectory);
      while (current != null)
      {
         if (current.GetFiles("*.sln", SearchOption.TopDirectoryOnly).Length > 0
             || current.GetDirectories(".git", SearchOption.TopDirectoryOnly).Length > 0)
         {
            return current.FullName;
         }
         current = current.Parent;
      }
      return null;
   }

   private static async Task<object> ReadTextFileAsync(string arguments)
   {
      // Accept a single "filePath" or a "filePaths" array so the model can
      // fetch everything it needs in one tool call instead of one round trip per file.
      var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
      var paths = new List<string>();

      if (args != null && args.TryGetValue("filePaths", out var pathsElement)
          && pathsElement.ValueKind == JsonValueKind.Array)
      {
         paths.AddRange(pathsElement.EnumerateArray()
             .Select(e => e.GetString())
             .Where(s => !string.IsNullOrEmpty(s))!);
      }
      else if (args != null && args.TryGetValue("filePath", out var pathElement)
          && pathElement.ValueKind == JsonValueKind.String)
      {
         paths.Add(pathElement.GetString()!);
      }

      if (paths.Count == 0)
         throw new ArgumentException("filePath (string) or filePaths (array of strings) is required");

      Console.WriteLine($"Reading file(s): {string.Join(", ", paths)}");

      var results = new List<FileReadResult>();
      foreach (var filePath in paths)
      {
         if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

         results.Add(new FileReadResult
         {
            Content = await File.ReadAllTextAsync(filePath),
            FilePath = filePath
         });
      }

      return results.Count == 1 ? results[0] : results;
   }

   /// <summary>
   /// Extracts a string argument from a tool-call JSON payload. Deserializes to
   /// JsonElement (not string) so unexpected numeric/boolean arguments the model
   /// adds don't crash the whole tool call.
   /// </summary>
   private static string GetStringArgument(string arguments, string name)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
      return args is not null
          && args.TryGetValue(name, out var element)
          && element.ValueKind == JsonValueKind.String
              ? element.GetString()!
              : throw new ArgumentException($"{name} is required");
   }

   private static async Task<object> WriteTextFileAsync(string arguments)
   {
      var filePath = GetStringArgument(arguments, "filePath");
      var content = GetStringArgument(arguments, "content");

      Console.WriteLine($"Writing file at: {filePath}");

      var directory = Path.GetDirectoryName(filePath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
         Directory.CreateDirectory(directory);
      }

      await File.WriteAllTextAsync(filePath, content);
      return new FileWriteResult { Success = true, FilePath = filePath };
   }

   private static async Task<object> ScanFolderAsync(string arguments)
   {
      // JsonElement, not string: the model sometimes sends extra numeric
      // arguments (e.g. maxDepth) that would fail string deserialization
      var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
      var folderPath = args is not null
          && args.TryGetValue("folderPath", out var folderElement)
          && folderElement.ValueKind == JsonValueKind.String
              ? folderElement.GetString()
              : null;

      if (string.IsNullOrEmpty(folderPath))
         throw new ArgumentException("folderPath is required");

      Console.WriteLine($"Scanning folder tree at: {folderPath}");

      if (!Directory.Exists(folderPath))
         throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

      // ASCII listing, matching scan_folder_tree's style: directories first
      // with a trailing '/', then files with byte sizes.
      var sb = new System.Text.StringBuilder();
      sb.AppendLine($"Folder: {folderPath}");

      foreach (var dir in Directory.GetDirectories(folderPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
         sb.AppendLine($"+-- {new System.IO.DirectoryInfo(dir).Name}/");

      foreach (var file in Directory.GetFiles(folderPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
      {
         var fileInfo = new System.IO.FileInfo(file);
         sb.AppendLine($"     {fileInfo.Name} ({fileInfo.Length} bytes)");
      }

      sb.AppendLine($"{Directory.GetDirectories(folderPath).Length} directories, {Directory.GetFiles(folderPath).Length} files.");
      return sb.ToString();
   }

   private static async Task<object> DeleteFileAsync(string arguments)
   {
      var filePath = GetStringArgument(arguments, "filePath");

      Console.WriteLine($"Deleting file at: {filePath}");

      Console.Write($"Delete file: {filePath} y/n");
      if (Console.ReadLine()?.Trim().ToLower() != "y")
      {
         return new FileOperationResult { Success = false, Error = "Operation cancelled by user." };
      }

      if (!File.Exists(filePath))
         throw new FileNotFoundException($"File not found: {filePath}");

      File.Delete(filePath);
      return new FileOperationResult { Success = true };
   }

   private static async Task<object> DeleteFolderAsync(string arguments)
   {
      var folderPath = GetStringArgument(arguments, "folderPath");

      Console.Write($"Delete folder {folderPath} y/n");
      if (Console.ReadLine()?.Trim().ToLower() != "y")
      {
         return new FileOperationResult { Success = false, Error = "Operation cancelled by user." };
      }

      if (!Directory.Exists(folderPath))
         throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

      Directory.Delete(folderPath, true);
      return new FileOperationResult { Success = true };
   }

   private static async Task<object> CreateFolderAsync(string arguments)
   {
      var folderPath = GetStringArgument(arguments, "folderPath");

      Console.WriteLine($"Creating folder at: {folderPath}");

      Directory.CreateDirectory(folderPath);
      return new FileOperationResult { Success = true };
   }

   #endregion

   #region Schema and Tool Loading

   private async Task<List<Tool>> LoadToolsFromFileAsync(string fileName)
   {
      if (!File.Exists(fileName))
      {
         // Return default tools if file doesn't exist
         return CreateDefaultTools();
      }

      var json = await File.ReadAllTextAsync(fileName);

      List<Tool> tools;

      try
      {
         tools = JsonSerializer.Deserialize<List<Tool>>(json);
      }
      catch (JsonException ex)
      {
         Console.WriteLine($"Error parsing tools.json: {ex.Message}");
         return CreateDefaultTools();
      }

      return tools ?? CreateDefaultTools();
   }

   private List<Tool> CreateDefaultTools()
   {
      return new List<Tool>
        {
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "read_text_file",
                    Description = "Read the content of a text file",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["filePath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the file to read"
                            }
                        },
                        ["required"] = new[] { "filePath" }
                    }
                }
            },
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "write_text_file",
                    Description = "Write content to a text file",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["filePath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the file to write"
                            },
                            ["content"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The content to write to the file"
                            }
                        },
                        ["required"] = new[] { "filePath", "content" }
                    }
                }
            },
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "scan_folder",
                    Description = "Scan a folder and list its contents",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["folderPath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the folder to scan"
                            }
                        },
                        ["required"] = new[] { "folderPath" }
                    }
                }
            },
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "delete_file",
                    Description = "Delete a file",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["filePath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the file to delete"
                            }
                        },
                        ["required"] = new[] { "filePath" }
                    }
                }
            },
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "delete_folder",
                    Description = "Delete a folder and its contents",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["folderPath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the folder to delete"
                            }
                        },
                        ["required"] = new[] { "folderPath" }
                    }
                }
            },
            new()
            {
                Type = ToolType.Function,
                Function = new FunctionDefinition
                {
                    Name = "create_folder",
                    Description = "Create a new folder",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["folderPath"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The path to the folder to create"
                            }
                        },
                        ["required"] = new[] { "folderPath" }
                    }
                }
            }
        };
   }
   #endregion
}
