using glFTPd_Commander.FTP;
using glFTPd_Commander.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace glFTPd_Commander.Utils
{
    public class AppSettings
    {
        public Dictionary<string, WindowPlacementInfo>? WindowPlacement { get; set; }
        public List<CustomCommandSlot>? CustomCommandSlots { get; set; }
        public List<FtpConnection> FtpConnections { get; set; } = [];
    }

    public class WindowPlacementInfo
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? State { get; set; } // "Normal", "Maximized", etc.
    }

    public class FtpConnection
    {
        public string? Name { get; set; }         // Stored ENCRYPTED  
        public string? SslMode { get; set; }
        public string? Host { get; set; }         // Stored ENCRYPTED
        public string? Username { get; set; }     // Stored ENCRYPTED
        public string? Password { get; set; }     // Stored ENCRYPTED
        public string? Port { get; set; }         // Stored ENCRYPTED
        public string? Mode { get; set; }
    }

    public static class SettingsManager
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string OldCommandsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CustomCommandSlots.json");
        private static readonly string OldWindowFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MainWindow.windowstate.json");
        private static readonly string OldFtpConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftpconfig.txt");
        private static readonly JsonSerializerOptions PrettyPrintOptions = new() { WriteIndented = true };

        public static AppSettings Current { get; private set; } = new AppSettings();

        public static void Load()
        {
            // Load main settings file
            if (File.Exists(SettingsFile))
            {
                try
                {
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
                }
                catch
                {
                    Current = new AppSettings();
                }
            }
            else
            {
                Current = new AppSettings();
            }

            bool changed = false;

            // --- Merge Old CustomCommandSlots.json ---
            if (File.Exists(OldCommandsFile))
            {
                try
                {
                    var commands = JsonSerializer.Deserialize<List<CustomCommandSlot>>(File.ReadAllText(OldCommandsFile));
                    if (commands != null)
                    {
                        if (Current.CustomCommandSlots == null) Current.CustomCommandSlots = [];
                        Current.CustomCommandSlots.AddRange(commands);
                        changed = true;
                    }
                    File.Delete(OldCommandsFile);
                }
                catch { }
            }

            // --- Merge Old MainWindow.windowstate.json ---
            if (File.Exists(OldWindowFile))
            {
                try
                {
                    var placement = JsonSerializer.Deserialize<WindowPlacementInfo>(File.ReadAllText(OldWindowFile));
                    if (placement != null)
                    {
                        Current.WindowPlacement ??= [];
                        Current.WindowPlacement["MainWindow"] = placement;
                        changed = true;
                    }
                    File.Delete(OldWindowFile);
                }
                catch { }
            }

            // --- Merge Old ftpconfig.txt ---
            if (File.Exists(OldFtpConfigFile))
            {
                try
                {
                    var importedConnections = ParseOldFtpConfig(File.ReadAllLines(OldFtpConfigFile));
                    if (importedConnections.Count > 0)
                    {
                        Current.FtpConnections ??= [];
                        Current.FtpConnections.AddRange(importedConnections);
                        changed = true;
                    }
                    File.Delete(OldFtpConfigFile);
                    System.Diagnostics.Debug.WriteLine($"[SettingsManager] Imported {importedConnections.Count} connections from ftpconfig.txt");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Error importing ftpconfig.txt: " + ex);
                }
            }

            // --- DECRYPT ALL FTP CONNECTIONS ---
            if (Current.FtpConnections != null)
            {
                foreach (var conn in Current.FtpConnections)
                {
                    if (!string.IsNullOrWhiteSpace(conn.Name))
                        conn.Name = GlFtpdClient.TryDecryptString(conn.Name) ?? conn.Name;
                    if (!string.IsNullOrWhiteSpace(conn.Host))
                        conn.Host = GlFtpdClient.TryDecryptString(conn.Host) ?? conn.Host;
                    if (!string.IsNullOrWhiteSpace(conn.Port))
                        conn.Port = GlFtpdClient.TryDecryptString(conn.Port) ?? conn.Port;
                    if (!string.IsNullOrWhiteSpace(conn.Username))
                        conn.Username = GlFtpdClient.TryDecryptString(conn.Username) ?? conn.Username;
                    if (!string.IsNullOrWhiteSpace(conn.Password))
                        conn.Password = GlFtpdClient.TryDecryptString(conn.Password) ?? conn.Password;
                }
            }

            if (changed)
                Save();
        }

        public static void Save()
        {
            // Create a deep clone so we don't mutate the in-memory data
            var clone = JsonSerializer.Deserialize<AppSettings>(
                JsonSerializer.Serialize(Current)
            ) ?? new AppSettings();

            // ENCRYPT all FtpConnection fields
            if (clone.FtpConnections != null)
            {
                foreach (var conn in clone.FtpConnections)
                {
                    if (!string.IsNullOrWhiteSpace(conn.Name))
                        conn.Name = GlFtpdClient.EncryptString(conn.Name);
                    if (!string.IsNullOrWhiteSpace(conn.Host))
                        conn.Host = GlFtpdClient.EncryptString(conn.Host);
                    if (!string.IsNullOrWhiteSpace(conn.Port))
                        conn.Port = GlFtpdClient.EncryptString(conn.Port);
                    if (!string.IsNullOrWhiteSpace(conn.Username))
                        conn.Username = GlFtpdClient.EncryptString(conn.Username);
                    if (!string.IsNullOrWhiteSpace(conn.Password))
                        conn.Password = GlFtpdClient.EncryptString(conn.Password);
                }
            }
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(clone, PrettyPrintOptions));
        }

        // ----------- Window Placement ------------

        public static WindowPlacementInfo? GetMainWindowPlacement()
        {
            return Current.WindowPlacement != null &&
                   Current.WindowPlacement.TryGetValue("MainWindow", out var info)
                ? info
                : null;
        }

        public static void SetMainWindowPlacement(WindowPlacementInfo info)
        {
            Current.WindowPlacement ??= [];
            Current.WindowPlacement["MainWindow"] = info;
            Save();
        }

        // ----------- Custom Command Slots ------------

        public static List<CustomCommandSlot> GetCustomCommandSlots()
        {
            return Current.CustomCommandSlots ??= [];
        }

        public static void SetCustomCommandSlots(List<CustomCommandSlot> slots)
        {
            Current.CustomCommandSlots = slots;
            Save();
        }

        public static void AddCustomCommandSlot(CustomCommandSlot slot)
        {
            Current.CustomCommandSlots ??= [];
            Current.CustomCommandSlots.Add(slot);
            Save();
        }

        public static void RemoveCustomCommandSlot(string buttonText)
        {
            if (Current.CustomCommandSlots == null)
                return;

            Current.CustomCommandSlots.RemoveAll(slot =>
                string.Equals(slot.ButtonText, buttonText, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        // ----------- FTP Connections ------------

        public static List<FtpConnection> GetFtpConnections()
        {
            return Current.FtpConnections ??= [];
        }

        public static void SetFtpConnections(List<FtpConnection> connections)
        {
            Current.FtpConnections = connections;
            Save();
        }

        public static void AddFtpConnection(FtpConnection connection)
        {
            Current.FtpConnections ??= [];
            Current.FtpConnections.Add(connection);
            Save();
        }

        public static void RemoveFtpConnection(string name)
        {
            if (Current.FtpConnections == null)
                return;

            Current.FtpConnections.RemoveAll(conn =>
                string.Equals(conn.Name, name, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        // ----------- Import FTP Config Parser ------------

        private static List<FtpConnection> ParseOldFtpConfig(string[] lines)
        {
            var connections = new List<FtpConnection>();
            FtpConnection? current = null;

            foreach (var line in lines)
            {
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (current != null)
                        connections.Add(current);

                    current = new FtpConnection();
                    var encryptedName = line.Trim('[', ']');
                    current.Name = encryptedName;
                }
                else if (current != null && line.Contains('='))
                {
                    var idx = line.IndexOf('=');
                    var key = line[..idx].Trim();
                    var value = line[(idx + 1)..].Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "host":
                            current.Host = value;
                            break;
                        case "port":
                            current.Port = value;
                            break;
                        case "username":
                            current.Username = value;
                            break;
                        case "password":
                            current.Password = value;
                            break;
                        case "type":
                            current.Mode = value;
                            break;
                        case "sslmode":
                            current.SslMode = value;
                            break;
                    }
                }
            }
            if (current != null)
                connections.Add(current);

            return connections;
        }
    }
}
