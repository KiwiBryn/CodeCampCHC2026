using MistralAi.Agents.Models;
using MistralAi.Agents.Services;
using System.Text.Json;

namespace SampleConsoleApp;

public class ChatDemo(IMistralAiService mistralService)
{
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

         // Basic chat with file operations
         await InteractiveChatWithToolsAsync(tools);

         // Example of loading schema from file
         await ExampleWithSchemaFromFileAsync();
      }
      catch (Exception ex)
      {
         Console.WriteLine($"Error: {ex.Message}");
      }
   }

   private async Task InteractiveChatWithToolsAsync(List<Tool> tools)
   {
      Console.WriteLine("\n=== Interactive Chat with File Operations ===");
      Console.WriteLine("Type 'exit' to quit or 'help' for available commands.");

      var messages = new List<ChatMessage>
        {
            new() { Role = MessageRole.System, Content = "You are a helpful assistant with file system access. You can read, write, and manage files and folders." }
        };

      while (true)
      {
         Console.Write("\n> ");
         var input = Console.ReadLine();

         if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
            break;

         if (string.Equals(input, "help", StringComparison.OrdinalIgnoreCase))
         {
            Console.WriteLine("Available commands:");
            Console.WriteLine("- List files: 'list files in [path]'");
            Console.WriteLine("- Read file: 'read [filename]'");
            Console.WriteLine("- Write file: 'write to [filename] content [text]'");
            Console.WriteLine("- Create folder: 'create folder [path]'");
            Console.WriteLine("- Delete file: 'delete file [filename]'");
            Console.WriteLine("- Delete folder: 'delete folder [path]'");
            continue;
         }

         messages.Add(new ChatMessage { Role = MessageRole.User, Content = input });

         var request = new ChatCompletionRequest
         {
            Model = "mistral-large-latest",
            Messages = messages,
            Tools = tools,
            ToolChoice = ToolChoice.Auto,
            Temperature = 0.7f
         };

         try
         {
            var response = await mistralService.CreateChatCompletionAsync(request);

            if (response.Choices[0].Message.ToolCalls is { Count: > 0 })
            {
               messages.Add(response.Choices[0].Message);
               await ProcessToolCallsAsync(
                   response.Choices[0].Message.ToolCalls, messages);
            }
            else
            {
               // Regular response
               Console.WriteLine($"\nAssistant: {response.Choices[0].Message.Content}");
               messages.Add(response.Choices[0].Message);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error: {ex.Message}");
         }
      }
   }

   private async Task ProcessToolCallsAsync(List<ToolCall>? toolCalls, List<ChatMessage> messages)
   {
      if (toolCalls is not { Count: > 0 }) return;

      foreach (var toolCall in toolCalls)
      {
         if (toolCall.Function == null) continue;

         if (_toolFunctions.TryGetValue(toolCall.Function.Name, out var func))
         {
            try
            {
               // Parse arguments (simplified - in production use proper JSON parsing)
               var result = await func(toolCall.Function.Arguments);
               var resultJson = JsonSerializer.Serialize(result);

               // Add tool response to messages
               messages.Add(new ChatMessage
               {
                  Role = MessageRole.Tool,
                  Content = resultJson,
                  ToolCallId = toolCall.Id
               });

               Console.WriteLine($"\nTool {toolCall.Function.Name} executed successfully");
            }
            catch (Exception ex)
            {
               messages.Add(new ChatMessage
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
         Model = "mistral-large-latest",
         Messages = messages,
         Tools = await LoadToolsFromFileAsync("tools.json"),
         ToolChoice = ToolChoice.Auto,
         Temperature = 0.7f
      };

      var messagesText = JsonSerializer.Serialize(messages);

      var response = await mistralService.CreateChatCompletionAsync(request);
      Console.WriteLine($"\nAssistant: {response.Choices[0].Message.Content}");
      messages.Add(response.Choices[0].Message);
      await ProcessToolCallsAsync(response.Choices[0].Message.ToolCalls, messages);
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

   private static Task<object> ScanFolderTreeAsync(string arguments)
   {
      var args = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments);
      var folderPath = args?["folderPath"] as string ?? throw new ArgumentException("folderPath is required");

      // Parse options with defaults
      var options = new ScanOptions();
      if (args != null)
      {
         if (args.TryGetValue("mode", out var modeObj) && modeObj is string modeStr &&
             Enum.TryParse<ScanMode>(modeStr, true, out var mode))
         {
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
         if (args.TryGetValue("excludePatterns", out var excludeObj) && excludeObj is JsonElement excludeElement)
         {
            options.ExcludePatterns = excludeElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
         }
      }

            if (!Directory.Exists(folderPath))
               throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

            var (rootNode, totalFiles, totalDirs, totalSize) = BuildFileTree(folderPath, options);

            return Task.FromResult<object>(new FileTreeScanResult
            {
               RootPath = folderPath,
               Mode = options.Mode,
               MaxDepth = options.MaxDepth,
               RootNode = rootNode,
               TotalFiles = totalFiles,
               TotalDirectories = totalDirs,
               TotalSize = totalSize
            });
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

            if (options.Mode == ScanMode.Flat)
            {
               // Simple flat scan
               ProcessDirectoryFlat(rootPath, rootNode, options, ref totalFiles, ref totalDirs, ref totalSize);
            }
            else if (options.Mode == ScanMode.BreadthFirst)
            {
               // Breadth-first scan
               ProcessDirectoryBreadthFirst(rootPath, rootNode, options, ref totalFiles, ref totalDirs, ref totalSize);
            }
            else // DepthFirst
            {
               // Depth-first scan
               ProcessDirectoryDepthFirst(rootPath, rootNode, options, ref totalFiles, ref totalDirs, ref totalSize);
            }

            return (rootNode, totalFiles, totalDirs, totalSize);
         }

         private static void ProcessDirectoryFlat(
             string currentPath,
             FileSystemNode currentNode,
             ScanOptions options,
             ref int totalFiles,
             ref int totalDirs,
             ref long totalSize)
         {
            try
      {
         if (options.IncludeDirectories)
         {
            foreach (var dir in Directory.GetDirectories(currentPath))
            {
               if (ShouldExclude(dir, options.ExcludePatterns)) continue;

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
            }
         }

         if (options.IncludeFiles)
         {
            foreach (var file in Directory.GetFiles(currentPath))
            {
               if (!MatchesPatterns(file, options.FilePatterns)) continue;

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

   private static void ProcessDirectoryBreadthFirst(
       string rootPath,
       FileSystemNode rootNode,
       ScanOptions options,
       ref int totalFiles,
       ref int totalDirs,
       ref long totalSize)
   {
      var queue = new Queue<(string Path, FileSystemNode Node, int CurrentDepth)>();
      queue.Enqueue((rootPath, rootNode, 0));

      while (queue.Count > 0)
      {
         var (currentPath, currentNode, currentDepth) = queue.Dequeue();

         if (currentDepth >= options.MaxDepth) continue;

         try
         {
            if (options.IncludeDirectories)
            {
               foreach (var dir in Directory.GetDirectories(currentPath))
               {
                  if (ShouldExclude(dir, options.ExcludePatterns)) continue;

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
                  if (!MatchesPatterns(file, options.FilePatterns)) continue;

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
               if (ShouldExclude(dir, options.ExcludePatterns)) continue;

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
               if (!MatchesPatterns(file, options.FilePatterns)) continue;

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

   private static bool ShouldExclude(string path, string[]? excludePatterns)
   {
      if (excludePatterns == null || excludePatterns.Length == 0)
         return false;

      var pathParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
      foreach (var pattern in excludePatterns)
      {
         if (pathParts.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
         {
            return true;
         }
      }
      return false;
   }

   private static bool MatchesPatterns(string filePath, string[]? filePatterns)
   {
      if (filePatterns == null || filePatterns.Length == 0)
         return true;

      var fileName = Path.GetFileName(filePath);
      foreach (var pattern in filePatterns)
      {
         if (pattern == "*") return true;

         try
         {
            // Simple pattern matching (for *.ext patterns)
            if (pattern.StartsWith("*."))
            {
               var ext = pattern[1..];
               if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                  return true;
            }
            else if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
               return true;
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
