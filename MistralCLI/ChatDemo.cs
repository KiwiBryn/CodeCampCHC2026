using MistralAi.Agents.Models;
using MistralAi.Agents.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SampleConsoleApp;

public class ChatDemo(IMistralAiService mistralService)
{
   //private readonly Dictionary<string, Func<string, Task<object>>> _toolFunctions = new();
   private List<ChatMessage> _messages = new();
   private string _systemPrompt = "You are a helpful assistant with file system access and memory. You can read, write, and manage files and folders, and remember conversation context.";
   private string _model = "mistral-large-latest";
   private float _temperature = 0.7f;
   private const string ContextFileName = "context.json";

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


   public async Task RunAsync()
   {
      try
      {
         // Load tools from JSON file
         var tools = await LoadToolsFromFileAsync("tools.json");

         // Try to load previous context
         await LoadContextAsync();

         Console.WriteLine("\n=== AI Assistant with Context ===");
         Console.WriteLine("Available commands:");
         Console.WriteLine("- /exit - Exit the application");
         Console.WriteLine("- /save - Save current conversation context");
         Console.WriteLine("- /clear - Clear conversation context (brain wipe)");
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
            var response = await mistralService.CreateChatCompletionAsync(request);

            if (response.Choices[0].Message.ToolCalls is { Count: > 0 })
            {
               // Handle tool calls
               await ProcessToolCallsAsync(response.Choices[0].Message.ToolCalls, tools);
            }
            else
            {
               // Regular response
               Console.WriteLine($"\nAssistant: {response.Choices[0].Message.Content}");
               _messages.Add(response.Choices[0].Message);
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
      var cmd = command.Trim().ToLower();

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

         case "/help":
            Console.WriteLine("\nAvailable commands:");
            Console.WriteLine("- /exit - Exit the application");
            Console.WriteLine("- /save - Save current conversation context");
            Console.WriteLine("- /clear - Clear conversation context (brain wipe)");
            Console.WriteLine("- /help - Show this help message");
            Console.WriteLine("\nType your message to continue chatting...");
            break;

         default:
            Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
            break;
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

   private async Task ProcessToolCallsAsync(List<ToolCall> toolCalls, List<Tool> tools)
   {
      foreach (var toolCall in toolCalls)
      {
         if (toolCall.Function == null) continue;

         if (_toolFunctions.TryGetValue(toolCall.Function.Name, out var func))
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
         var response = await mistralService.CreateChatCompletionAsync(request);
         Console.WriteLine($"\nAssistant: {response.Choices[0].Message.Content}");
         _messages.Add(response.Choices[0].Message);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error getting response: {ex.Message}");
      }
   }

   private async Task ExampleWithSchemaFromFileAsync()
   {
      Console.WriteLine("\n=== Example with Schema from File ===");

      // Load a specific schema for structured output
      var schema = await LoadSchemaFromFileAsync("output_schema.json");
      if (schema == null) return;

      var request = new ChatCompletionRequest
      {
         Model = "mistral-large-latest",
         Messages =
          [
              new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = "You are a data analyst. Return responses in the specified JSON format."
                },
                new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = "Analyze the following data: Sales increased by 15% in Q2. Expenses decreased by 5%."
                }
          ],
         ResponseFormat = new ResponseFormat { Type = ResponseFormatType.JsonObject },
         Temperature = 0.3f
      };

      // If we had a way to pass schema to the model, we would do it here
      // For now, we'll just show the schema was loaded
      Console.WriteLine($"Using schema: {schema}");
   }

   #region File System Operations

   private static async Task<object> ScanFolderTreeAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments);
      var folderPath = args?["folderPath"] as string ?? throw new ArgumentException("folderPath is required");

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
      catch
      {
         // If we can't load the ignore file, just continue with defaults
      }

      // Override with any options from the arguments
      if (args != null)
      {
         if (args.TryGetValue("mode", out var modeObj) && modeObj is string modeStr)
         {
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

      return new FileTreeScanResult
      {
         RootPath = folderPath,
         Mode = options.Mode,
         MaxDepth = options.MaxDepth,
         RootNode = rootNode,
         TotalFiles = totalFiles,
         TotalDirectories = totalDirs,
         TotalSize = totalSize
      };
   }
   private Task<object> GetFileTreeAsync(string arguments) => ScanFolderTreeAsync(arguments);

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

      // Check file ignore list
      if (!isDirectory && options.IgnoreFiles != null)
      {
         var fileName = Path.GetFileName(path);
         if (options.IgnoreFiles.Contains(fileName, options.CaseSensitive
             ? StringComparer.Ordinal
             : StringComparer.OrdinalIgnoreCase))
         {
            return true;
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

   private static async Task<object> ReadTextFileAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var filePath = args?["filePath"] ?? throw new ArgumentException("filePath is required");

      if (!File.Exists(filePath))
         throw new FileNotFoundException($"File not found: {filePath}");

      var content = await File.ReadAllTextAsync(filePath);
      return new FileReadResult { Content = content, FilePath = filePath };
   }

   private static async Task<object> WriteTextFileAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var filePath = args?["filePath"] ?? throw new ArgumentException("filePath is required");
      var content = args?["content"] ?? throw new ArgumentException("content is required");

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
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var folderPath = args?["folderPath"] ?? throw new ArgumentException("folderPath is required");

      if (!Directory.Exists(folderPath))
         throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

      var files = new List<MistralAi.Agents.Models.FileInfo>();
      var directories = new List<MistralAi.Agents.Models.DirectoryInfo>();

      foreach (var file in Directory.GetFiles(folderPath))
      {
         var fileInfo = new System.IO.FileInfo(file);
         files.Add(new MistralAi.Agents.Models.FileInfo
         {
            Name = fileInfo.Name,
            Path = fileInfo.FullName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTime
         });
      }

      foreach (var dir in Directory.GetDirectories(folderPath))
      {
         var dirInfo = new System.IO.DirectoryInfo(dir);
         directories.Add(new MistralAi.Agents.Models.DirectoryInfo
         {
            Name = dirInfo.Name,
            Path = dirInfo.FullName,
            LastModified = dirInfo.LastWriteTime
         });
      }

      return new FolderScanResult
      {
         Path = folderPath,
         Files = files,
         Directories = directories
      };
   }

   private static async Task<object> DeleteFileAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var filePath = args?["filePath"] ?? throw new ArgumentException("filePath is required");

      if (!File.Exists(filePath))
         throw new FileNotFoundException($"File not found: {filePath}");

      File.Delete(filePath);
      return new FileOperationResult { Success = true };
   }

   private static async Task<object> DeleteFolderAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var folderPath = args?["folderPath"] ?? throw new ArgumentException("folderPath is required");

      if (!Directory.Exists(folderPath))
         throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

      Directory.Delete(folderPath, true);
      return new FileOperationResult { Success = true };
   }

   private static async Task<object> CreateFolderAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
      var folderPath = args?["folderPath"] ?? throw new ArgumentException("folderPath is required");

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

   private async Task<string?> LoadSchemaFromFileAsync(string fileName)
   {
      if (!File.Exists(fileName)) return null;
      return await File.ReadAllTextAsync(fileName);
   }

   #endregion
}
