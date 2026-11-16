using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;


public class FileTreeScanResult
{
   public required string RootPath { get; set; }
   public required ScanMode Mode { get; set; }
   public required int MaxDepth { get; set; }
   public required FileSystemNode RootNode { get; set; }
   public required int TotalFiles { get; set; }
   public required int TotalDirectories { get; set; }
   public required long TotalSize { get; set; }
}

public class ScanOptions
{
   public ScanMode Mode { get; set; } = ScanMode.BreadthFirst;
   public int MaxDepth { get; set; } = int.MaxValue;
   public bool IncludeFiles { get; set; } = true;
   public bool IncludeDirectories { get; set; } = true;
   public string[]? FilePatterns { get; set; } // e.g., ["*.txt", "*.json"]
   public string[]? ExcludePatterns { get; set; } // e.g., ["node_modules", ".git"]
}

public class FileReadResult
{
   public required string Content { get; set; }
   public required string FilePath { get; set; }
}

public class FileWriteResult
{
   public required bool Success { get; set; }
   public required string FilePath { get; set; }
   public string? Error { get; set; }
}

public class FolderScanResult
{
   public required string Path { get; set; }
   public required List<FileInfo> Files { get; set; }
   public required List<DirectoryInfo> Directories { get; set; }
}

public class FileInfo
{
   public required string Name { get; set; }
   public required string Path { get; set; }
   public required long Size { get; set; }
   public required DateTime LastModified { get; set; }
}

public class DirectoryInfo
{
   public required string Name { get; set; }
   public required string Path { get; set; }
   public required DateTime LastModified { get; set; }
}

public class FileOperationResult
{
   public required bool Success { get; set; }
   public string? Error { get; set; }
}

public enum ScanMode
{
   Flat,
   BreadthFirst,
   DepthFirst
}

public class FileSystemNode
{
   public required string Name { get; set; }
   public required string Path { get; set; }
   public required bool IsDirectory { get; set; }
   public long? Size { get; set; } // Only for files
   public DateTime? LastModified { get; set; }
   public List<FileSystemNode>? Children { get; set; }
   public int Depth { get; set; }
}

