using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace ArknightsSystemCursor
{
    public static class GameLocator
    {
        public static List<string> Find()
        {
            HashSet<string> matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(matches, AppDomain.CurrentDomain.BaseDirectory);
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    foreach (string suffix in new[] { @"Hypergryph Launcher\games\Arknights Game", @"Games\Hypergryph Launcher\games\Arknights Game", @"Program Files\Hypergryph Launcher\games\Arknights Game", @"Arknights Game" })
                    {
                        AddCandidate(matches, Path.Combine(drive.RootDirectory.FullName, suffix));
                    }
                }
            }
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey? uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall != null)
                            {
                                foreach (string name in uninstall.GetSubKeyNames())
                                {
                                    using (RegistryKey? entry = uninstall.OpenSubKey(name))
                                    {
                                        if (entry != null)
                                        {
                                            string display = Convert.ToString(entry.GetValue("DisplayName")) ?? "";
                                            if (display.IndexOf("Arknights", StringComparison.OrdinalIgnoreCase) >= 0 || display.Contains("明日方舟") || display.Contains("Hypergryph"))
                                            {
                                                string location = Convert.ToString(entry.GetValue("InstallLocation")) ?? "";
                                                AddCandidate(matches, location);
                                                if (location.Length > 0)
                                                {
                                                    AddCandidate(matches, Path.Combine(location, "games", "Arknights Game"));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return new List<string>(matches);
        }

        private static void AddCandidate(HashSet<string> matches, string path)
        {
            if (path.Length > 0 && File.Exists(Path.Combine(path, "Arknights.exe")) && Directory.Exists(Path.Combine(path, "Arknights_Data")))
            {
                matches.Add(Path.GetFullPath(path).TrimEnd('\\'));
            }
        }
    }
}
