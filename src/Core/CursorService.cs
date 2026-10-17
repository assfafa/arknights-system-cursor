using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ArknightsSystemCursor
{
    public sealed class CursorService
    {
        public const string DisabledSuffix = ".system-cursor-disabled-";
        public const string LegacySuffix = ".disabled-by-codex";
        private readonly string stateRoot;
        private readonly Func<bool> gameRunning;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public CursorService(string? stateRoot = null, Func<bool>? gameRunning = null)
        {
            if (stateRoot != null)
            {
                this.stateRoot = Path.GetFullPath(stateRoot);
            }
            else
            {
                this.stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArknightsSystemCursor", "installations");
            }
            if (gameRunning != null)
            {
                this.gameRunning = gameRunning;
            }
            else
            {
                this.gameRunning = IsGameRunning;
            }
        }

        public static bool IsGameRunning()
        {
            Process[] processes = Process.GetProcessesByName("Arknights");
            bool running = processes.Length > 0;
            foreach (Process process in processes)
            {
                process.Dispose();
            }
            return running;
        }

        public static string NormalizeGameDirectory(string input)
        {
            string full = Path.GetFullPath(input.Trim().Trim('"'));
            if (File.Exists(full))
            {
                full = Path.GetDirectoryName(full)!;
            }
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(full, "Arknights.exe")) && Directory.Exists(Path.Combine(full, "Arknights_Data")))
            {
                return full;
            }
            else
            {
                throw new InvalidOperationException("请选择包含 Arknights.exe 和 Arknights_Data 的明日方舟 PC 客户端目录。");
            }
        }

        public CursorStatus Inspect(string gameDirectory)
        {
            string root = NormalizeGameDirectory(gameDirectory);
            CursorStatus status = new CursorStatus
            {
                GameDirectory = root,
                StateDirectory = GetStateDirectory(root),
                GameRunning = gameRunning()
            };
            string[] resourceRoots =
            {
                Path.Combine(root, "Arknights_Data", "StreamingAssets", "AB", "Windows"),
                Path.Combine(root, "Arknights_Data", "PersistentData", "Bundles")
            };
            foreach (string resourceRoot in resourceRoots)
            {
                List<string> directories = new List<string> { Path.Combine(resourceRoot, "anon"), Path.Combine(resourceRoot, "mouse") };
                foreach (string directory in directories)
                {
                    if (Directory.Exists(directory))
                    {
                        AssertOrdinaryPath(root, directory);
                        foreach (string path in Directory.EnumerateFiles(directory).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                        {
                            string fileName = Path.GetFileName(path);
                            bool candidate = fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                                || fileName.EndsWith(".ab", StringComparison.OrdinalIgnoreCase)
                                || fileName.Contains(DisabledSuffix)
                                || fileName.EndsWith(LegacySuffix, StringComparison.OrdinalIgnoreCase);
                            if (candidate)
                            {
                                AssertOrdinaryPath(root, path);
                                if (BundleInspector.IsMouseBundle(path))
                                {
                                    string original = GetOriginalPath(path);
                                    status.Resources.Add(new ResourceFile
                                    {
                                        OriginalPath = original,
                                        CurrentPath = path,
                                        RelativePath = MakeRelative(root, original),
                                        Sha256 = HashFile(path),
                                        Length = new FileInfo(path).Length,
                                        Disabled = !String.Equals(path, original, StringComparison.OrdinalIgnoreCase)
                                    });
                                }
                            }
                        }
                    }
                }
            }
            int activeCount = status.Resources.Count(resource => !resource.Disabled);
            if (activeCount > 0)
            {
                status.Mode = "game";
                status.Message = "游戏指针资源存在，可以启用系统指针。";
            }
            else if (status.Resources.Count > 0)
            {
                status.Mode = "system";
                status.Message = "游戏鼠标资源已停用，启动游戏后使用系统指针。";
            }
            else
            {
                InstallState state = LoadState(root);
                if (state.Entries.Count > 0)
                {
                    status.Mode = "missing";
                    status.Message = "未找到资源，存在备份记录。可尝试恢复。";
                }
                else
                {
                    status.Message = "没有识别到鼠标资源。当前游戏版本可能暂不支持。";
                }
            }
            return status;
        }

        public OperationResult Enable(string gameDirectory)
        {
            string root = NormalizeGameDirectory(gameDirectory);
            using (FileStream operationLock = AcquireOperationLock(root))
            {
                EnsureGameClosed();
                CursorStatus status = Inspect(root);
                List<ResourceFile> active = status.Resources.Where(resource => !resource.Disabled).ToList();
                if (active.Count > 0)
                {
                    InstallState state = LoadState(root);
                    List<BackupEntry> plans = new List<BackupEntry>();
                    foreach (ResourceFile resource in active)
                    {
                        string disabled = resource.OriginalPath + DisabledSuffix + resource.Sha256.Substring(0, 12);
                        if (File.Exists(disabled))
                        {
                            throw new IOException("停用目标已存在，已保留原文件。请先检查：" + disabled);
                        }
                        BackupEntry entry = new BackupEntry
                        {
                            RelativePath = resource.RelativePath,
                            DisabledRelativePath = MakeRelative(root, disabled),
                            Sha256 = resource.Sha256,
                            BackupFileName = resource.Sha256 + ".bin",
                            DisabledUtc = DateTime.UtcNow.ToString("o")
                        };
                        EnsureBackup(root, resource.CurrentPath, entry);
                        plans.Add(entry);
                    }
                    List<BackupEntry> completed = new List<BackupEntry>();
                    try
                    {
                        EnsureGameClosed();
                        foreach (BackupEntry entry in plans)
                        {
                            string original = Within(root, entry.RelativePath);
                            AssertHash(original, entry.Sha256);
                            File.Move(original, Within(root, entry.DisabledRelativePath));
                            completed.Add(entry);
                        }
                        foreach (BackupEntry entry in plans)
                        {
                            state.Entries.RemoveAll(old => String.Equals(old.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase));
                            state.Entries.Add(entry);
                        }
                        SaveState(root, state);
                    }
                    catch
                    {
                        foreach (BackupEntry entry in completed.AsEnumerable().Reverse())
                        {
                            string original = Within(root, entry.RelativePath);
                            string disabled = Within(root, entry.DisabledRelativePath);
                            if (!File.Exists(original) && File.Exists(disabled))
                            {
                                File.Move(disabled, original);
                            }
                        }
                        throw;
                    }
                    return Result("enable", "系统指针已启用。请正常启动游戏验证。", completed.Count, root);
                }
                else if (status.Mode == "system")
                {
                    return Result("enable", "已经启用系统指针，无需重复操作。", 0, root);
                }
                else
                {
                    throw new InvalidOperationException(status.Message);
                }
            }
        }

        public OperationResult Restore(string gameDirectory)
        {
            string root = NormalizeGameDirectory(gameDirectory);
            using (FileStream operationLock = AcquireOperationLock(root))
            {
                EnsureGameClosed();
                CursorStatus status = Inspect(root);
                InstallState state = LoadState(root);
                List<BackupEntry> plans = PlanRestore(root, status, state);
                // Validate every source before moving any file.
                foreach (BackupEntry entry in plans)
                {
                    string disabled = Within(root, entry.DisabledRelativePath);
                    if (File.Exists(disabled))
                    {
                        AssertHash(disabled, entry.Sha256);
                        AssertMouseBundle(disabled);
                    }
                    else
                    {
                        AssertHash(GetBackupPath(root, entry), entry.Sha256);
                        AssertMouseBundle(GetBackupPath(root, entry));
                    }
                }
                List<BackupEntry> completed = new List<BackupEntry>();
                HashSet<string> copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    EnsureGameClosed();
                    foreach (BackupEntry entry in plans)
                    {
                        string original = Within(root, entry.RelativePath);
                        string disabled = Within(root, entry.DisabledRelativePath);
                        if (File.Exists(disabled))
                        {
                            File.Move(disabled, original);
                        }
                        else
                        {
                            File.Copy(GetBackupPath(root, entry), original, false);
                            copied.Add(entry.RelativePath);
                        }
                        completed.Add(entry);
                    }
                }
                catch
                {
                    foreach (BackupEntry entry in completed.AsEnumerable().Reverse())
                    {
                        string original = Within(root, entry.RelativePath);
                        if (copied.Contains(entry.RelativePath))
                        {
                            AssertHash(original, entry.Sha256);
                            File.Delete(original);
                        }
                        else
                        {
                            File.Move(original, Within(root, entry.DisabledRelativePath));
                        }
                    }
                    throw;
                }
                string message;
                if (completed.Count > 0)
                {
                    message = "游戏指针已恢复，原始资源校验通过。";
                }
                else if (status.Mode == "game")
                {
                    message = "原始资源已存在，没有覆盖或替换任何文件。";
                }
                else
                {
                    throw new InvalidOperationException("没有找到可恢复的资源或备份。");
                }
                return Result("restore", message, completed.Count, root);
            }
        }

        private List<BackupEntry> PlanRestore(string root, CursorStatus status, InstallState state)
        {
            Dictionary<string, BackupEntry> entries = new Dictionary<string, BackupEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (BackupEntry entry in state.Entries)
            {
                Within(root, entry.RelativePath);
                Within(root, entry.DisabledRelativePath);
                entries[entry.RelativePath] = entry;
            }
            foreach (IGrouping<string, ResourceFile> group in status.Resources.Where(resource => resource.Disabled).GroupBy(resource => resource.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!entries.ContainsKey(group.Key))
                {
                    if (group.Count() == 1)
                    {
                        ResourceFile resource = group.First();
                        entries[group.Key] = new BackupEntry
                        {
                            RelativePath = group.Key,
                            DisabledRelativePath = MakeRelative(root, resource.CurrentPath),
                            Sha256 = resource.Sha256,
                            BackupFileName = resource.Sha256 + ".bin"
                        };
                    }
                    else if (!File.Exists(Within(root, group.Key)))
                    {
                        throw new InvalidOperationException("发现多个历史版本，无法确定应恢复哪一个。请保留备份并检查：" + group.Key);
                    }
                }
            }
            List<BackupEntry> plans = new List<BackupEntry>();
            foreach (BackupEntry entry in entries.Values)
            {
                string original = Within(root, entry.RelativePath);
                if (File.Exists(original))
                {
                    // A game update or repair may have supplied a newer original. Never overwrite it.
                }
                else
                {
                    plans.Add(entry);
                }
            }
            return plans;
        }

        private void EnsureBackup(string root, string source, BackupEntry entry)
        {
            string destination = GetBackupPath(root, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                AssertHash(destination, entry.Sha256);
            }
            else
            {
                File.Copy(source, destination, false);
                AssertHash(destination, entry.Sha256);
            }
        }

        private InstallState LoadState(string root)
        {
            string path = Path.Combine(GetStateDirectory(root), "manifest.json");
            if (File.Exists(path))
            {
                InstallState state = json.Deserialize<InstallState>(File.ReadAllText(path));
                if (state != null && state.SchemaVersion == 1 && state.Entries != null
                    && String.Equals(state.GameDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (BackupEntry entry in state.Entries)
                    {
                        ValidateEntry(root, entry);
                    }
                    return state;
                }
                else
                {
                    throw new InvalidDataException("备份记录无效，未修改游戏文件。");
                }
            }
            else
            {
                return new InstallState { GameDirectory = root };
            }
        }

        private void SaveState(string root, InstallState state)
        {
            string directory = GetStateDirectory(root);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "manifest.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json.Serialize(state), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        private void ValidateEntry(string root, BackupEntry entry)
        {
            bool allowedResource = entry.RelativePath.StartsWith("Arknights_Data\\StreamingAssets\\AB\\Windows\\anon\\", StringComparison.OrdinalIgnoreCase)
                || entry.RelativePath.StartsWith("Arknights_Data\\PersistentData\\Bundles\\anon\\", StringComparison.OrdinalIgnoreCase)
                || entry.RelativePath.StartsWith("Arknights_Data\\StreamingAssets\\AB\\Windows\\mouse\\", StringComparison.OrdinalIgnoreCase)
                || entry.RelativePath.StartsWith("Arknights_Data\\PersistentData\\Bundles\\mouse\\", StringComparison.OrdinalIgnoreCase);
            bool hashValid = entry.Sha256 != null && entry.Sha256.Length == 64 && entry.Sha256.All(Uri.IsHexDigit);
            if (allowedResource && hashValid && entry.BackupFileName == entry.Sha256 + ".bin"
                && GetOriginalPath(Within(root, entry.DisabledRelativePath)) == Within(root, entry.RelativePath))
            {
                Within(root, entry.RelativePath);
            }
            else
            {
                throw new InvalidDataException("备份记录包含不允许的路径或校验值。");
            }
        }

        private OperationResult Result(string action, string message, int changed, string root)
        {
            return new OperationResult { Success = true, Action = action, Message = message, ChangedFiles = changed, Status = Inspect(root) };
        }

        private void EnsureGameClosed()
        {
            if (gameRunning())
            {
                throw new InvalidOperationException("请完全退出明日方舟客户端后再操作。");
            }
        }

        private FileStream AcquireOperationLock(string root)
        {
            string directory = GetStateDirectory(root);
            Directory.CreateDirectory(directory);
            return new FileStream(Path.Combine(directory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private string GetStateDirectory(string root)
        {
            byte[] key = Encoding.UTF8.GetBytes(root.ToUpperInvariant());
            return Path.Combine(stateRoot, HashBytes(key).Substring(0, 24));
        }

        private string GetBackupPath(string root, BackupEntry entry)
        {
            if (Path.GetFileName(entry.BackupFileName) == entry.BackupFileName && entry.BackupFileName == entry.Sha256 + ".bin")
            {
                return Path.Combine(GetStateDirectory(root), "backups", entry.BackupFileName);
            }
            else
            {
                throw new InvalidDataException("Invalid backup filename.");
            }
        }

        private static string GetOriginalPath(string path)
        {
            int suffixIndex = path.LastIndexOf(DisabledSuffix, StringComparison.Ordinal);
            if (suffixIndex >= 0)
            {
                return path.Substring(0, suffixIndex);
            }
            else if (path.EndsWith(LegacySuffix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(0, path.Length - LegacySuffix.Length);
            }
            else
            {
                return path;
            }
        }

        private static string MakeRelative(string root, string path)
        {
            string prefix = root.TrimEnd('\\') + "\\";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(prefix.Length);
            }
            else
            {
                throw new InvalidDataException("Resource is outside the selected game directory.");
            }
        }

        private static string Within(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            MakeRelative(root, full);
            AssertOrdinaryPath(root, full);
            return full;
        }

        private static void AssertOrdinaryPath(string root, string path)
        {
            MakeRelative(root, path);
            string? current = path;
            bool checking = true;
            while (checking && current != null)
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("请使用实际游戏目录，资源路径不能包含符号链接或目录联接：" + current);
                    }
                }
                current = Path.GetDirectoryName(current);
                checking = current != null && !String.Equals(current, root, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            }
        }

        private static void AssertHash(string path, string expected)
        {
            if (File.Exists(path) && String.Equals(HashFile(path), expected, StringComparison.OrdinalIgnoreCase))
            {
                // The checked content is the version recorded in the backup manifest.
            }
            else
            {
                throw new InvalidDataException("文件不存在或 SHA-256 校验失败，已停止操作：" + path);
            }
        }

        private static void AssertMouseBundle(string path)
        {
            if (BundleInspector.IsMouseBundle(path))
            {
                // Independently validate the source even when a manifest is present.
            }
            else
            {
                throw new InvalidDataException("备份不是已识别的鼠标资源，已停止恢复：" + path);
            }
        }

        private static string HashBytes(byte[] data)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
            }
        }
    }
}
