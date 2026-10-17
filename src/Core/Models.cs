using System;
using System.Collections.Generic;

namespace ArknightsSystemCursor
{
    public sealed class ResourceFile
    {
        public string OriginalPath { get; set; } = "";
        public string CurrentPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Length { get; set; }
        public bool Disabled { get; set; }
    }

    public sealed class CursorStatus
    {
        public string GameDirectory { get; set; } = "";
        public string Mode { get; set; } = "unsupported";
        public string Message { get; set; } = "";
        public string StateDirectory { get; set; } = "";
        public bool GameRunning { get; set; }
        public List<ResourceFile> Resources { get; set; } = new List<ResourceFile>();
    }

    public sealed class BackupEntry
    {
        public string RelativePath { get; set; } = "";
        public string DisabledRelativePath { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string BackupFileName { get; set; } = "";
        public string DisabledUtc { get; set; } = "";
    }

    public sealed class InstallState
    {
        public int SchemaVersion { get; set; } = 1;
        public string GameDirectory { get; set; } = "";
        public List<BackupEntry> Entries { get; set; } = new List<BackupEntry>();
    }

    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Action { get; set; } = "";
        public string Message { get; set; } = "";
        public int ChangedFiles { get; set; }
        public CursorStatus? Status { get; set; }
    }
}
