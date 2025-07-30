using FluentFTP;
using glFTPd_Commander.Services;
using System.Configuration;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Windows;
using Debug = System.Diagnostics.Debug;

namespace glFTPd_Commander.Services
{
    public class FTP
    {
        public string Host { get; private set; } = string.Empty;
        public string Username { get; private set; } = string.Empty;
        public string Password { get; private set; } = string.Empty;
        public string Port { get; private set; } = string.Empty;
        public bool PassiveMode { get; private set; } = false;
        public string SslMode { get; private set; } = "Explicit";

        
        private static readonly HashSet<string> approvedInSession = new HashSet<string>();
        private static readonly HashSet<string> promptingThumbprints = new HashSet<string>();
        private static readonly HashSet<string> rejectedInSession = new HashSet<string>();
        private static readonly object promptLock = new object();
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        public SemaphoreSlim ConnectionLock => _connectionLock;

        public static bool EnsureConnected(ref FtpClient ftpClient, FTP config)
        {
            if (ftpClient == null || !ftpClient.IsConnected)
            {
                try
                {
                    ftpClient?.Dispose();
                    ftpClient = config.CreateClient();
                    ftpClient.Connect();
                    Debug.WriteLine("[FTP] Disposed old client and created a new one.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FTP] Recreate/connect failed: {ex.Message}");
                    return false;
                }
            }
            return ftpClient != null && ftpClient.IsConnected;
        }


        public FTP()
        {
            EncryptionKeyManager.Initialize();
            LoadSettings();
        }

        public void LoadSettings(string? encryptedConnectionName = null)
        {
            var connections = GetAllConnections();
            Dictionary<string, string>? config = null;
        
            if (encryptedConnectionName != null)
            {
                config = connections.FirstOrDefault(c => 
                    c["Name"].Equals(encryptedConnectionName, StringComparison.Ordinal));
            }
        
            if (config == null)
            {
                config = connections.FirstOrDefault();
            }
        
            if (config != null)
            {
                if (config.TryGetValue("Host", out var host))
                    Host = TryDecryptString(host) ?? host;

                if (config.TryGetValue("Port", out var port))
                    Port = TryDecryptString(port) ?? port;

                if (config.TryGetValue("Username", out var user))
                    Username = TryDecryptString(user) ?? user;

                if (config.TryGetValue("Password", out var pass))
                    Password = TryDecryptString(pass) ?? pass;

                if (config.TryGetValue("Type", out var type))
                    PassiveMode = type.Equals("PASV", StringComparison.OrdinalIgnoreCase);
                else
                    PassiveMode = true; // default to PASV

                if (config.TryGetValue("SslMode", out var ssl))
                    SslMode = ssl;
                else
                    SslMode = "Explicit"; // default fallback
            }
        }

        public static List<Dictionary<string, string>> GetAllConnections()
        {
            string configPath = ConfigurationManager.AppSettings["FtpConfigPath"] ?? "ftpconfig.txt";
            var connections = new List<Dictionary<string, string>>();
        
            if (!File.Exists(configPath))
                return connections;
        
            var lines = File.ReadAllLines(configPath);
            Dictionary<string, string>? currentConnection = null;
        
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;
        
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    if (currentConnection != null)
                    {
                        connections.Add(currentConnection);
                    }
                    currentConnection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var encryptedName = trimmedLine.Trim('[', ']');
                    currentConnection["Name"] = encryptedName;
                    currentConnection["Host"] = encryptedName;
                }
                else if (currentConnection != null && trimmedLine.Contains("="))
                {
                    var parts = trimmedLine.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        currentConnection[key] = value;
                    }
                }
            }
        
            if (currentConnection != null)
            {
                connections.Add(currentConnection);
            }
        
            return connections;
        }

        public void ReloadSettings()
        {
            approvedInSession.Clear();
            promptingThumbprints.Clear();
            rejectedInSession.Clear();
            LoadSettings();
        }

        public static string? TryDecryptString(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
                return cipherText;
        
            try
            {
                return DecryptString(cipherText);
            }
            catch
            {
                return null;
            }
        }

        public static string EncryptString(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = EncryptionKeyManager.Key;
            aes.IV = EncryptionKeyManager.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
                sw.Write(plainText);
            
            return Convert.ToBase64String(ms.ToArray());
        }

        public static string? DecryptString(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
                return string.Empty;

            try
            {
                var buffer = Convert.FromBase64String(cipherText);
                using var aes = Aes.Create();
                aes.Key = EncryptionKeyManager.Key;
                aes.IV = EncryptionKeyManager.IV;

                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(buffer);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        public FtpClient CreateClient()
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new ArgumentException("Host address cannot be empty");
            }

            // Basic host validation
            if (!Uri.CheckHostName(Host).Equals(UriHostNameType.Dns) &&
                !Uri.CheckHostName(Host).Equals(UriHostNameType.IPv4) &&
                !Uri.CheckHostName(Host).Equals(UriHostNameType.IPv6))
            {
                throw new ArgumentException("Invalid host address format");
            }

            // Default to Explicit unless otherwise configured
            string sslMode = "Explicit";

            // Try to load from config if available
            var connections = GetAllConnections();
            var config = connections.FirstOrDefault(c =>
                TryDecryptString(c["Host"]) == Host &&
                TryDecryptString(c["Username"]) == Username);

            if (config != null && config.TryGetValue("SslMode", out var mode))
            {
                sslMode = mode;
            }

            var encryptionMode = sslMode.Equals("Implicit", StringComparison.OrdinalIgnoreCase)
                ? FtpEncryptionMode.Implicit
                : FtpEncryptionMode.Explicit;

            var client = new FtpClient(Host)
            {
                Credentials = new NetworkCredential(Username, Password),
                Port = Convert.ToInt16(Port),
                Config = {
                    EncryptionMode = encryptionMode,
                    DataConnectionEncryption = true,
                    DataConnectionType = PassiveMode
                        ? FtpDataConnectionType.AutoPassive
                        : FtpDataConnectionType.AutoActive,
                    ConnectTimeout = 5000,
                    RetryAttempts = 2
                }
            };
        
            client.ValidateCertificate += (sender, e) => 
            {
                if (e.Certificate == null)
                {
                    e.Accept = false;
                    return;
                }
        
                try
                {
                    string thumbprint;
                    string subject;
        
                    try
                    {
                        using var cert2 = new X509Certificate2(e.Certificate);
                        thumbprint = cert2.Thumbprint;
                        subject = cert2.Subject;
                    }
                    catch (CryptographicException)
                    {
                        thumbprint = e.Certificate.GetCertHashString();
                        subject = e.Certificate.Subject;
                    }
        
                    lock (promptLock)
                    {
                        if (rejectedInSession.Contains(thumbprint))
                        {
                            e.Accept = false;
                            return;
                        }
        
                        if (approvedInSession.Contains(thumbprint)) 
                        {
                            e.Accept = true;
                            return;
                        }
        
                        if (CertificateStorage.IsCertificateApproved(thumbprint, Host))
                        {
                            approvedInSession.Add(thumbprint);
                            e.Accept = true;
                            return;
                        }
        
                        while (promptingThumbprints.Contains(thumbprint))
                            Monitor.Wait(promptLock);
        
                        if (approvedInSession.Contains(thumbprint))
                        {
                            e.Accept = true;
                            return;
                        }
        
                        promptingThumbprints.Add(thumbprint);
                    }
        
                    try
                    {
                        bool? dialogResult = null;
                        bool rememberDecision = false;
        
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var certWindow = new CertificateWindow(e.Certificate);
                            dialogResult = certWindow.ShowDialog();
                            rememberDecision = certWindow.RememberDecision;
                        });
        
                        lock (promptLock)
                        {
                            if (dialogResult == true)
                            {
                                approvedInSession.Add(thumbprint);
                                CertificateStorage.ApproveCertificate(thumbprint, subject, rememberDecision, Host);
                                e.Accept = true;
                            }
                            else
                            {
                                rejectedInSession.Add(thumbprint);
                                e.Accept = false;
                                client.Disconnect();
                            }
                        }
                    }
                    finally
                    {
                        lock (promptLock)
                        {
                            promptingThumbprints.Remove(thumbprint);
                            Monitor.PulseAll(promptLock);
                        }
                    }
                }
                catch
                {
                    e.Accept = false;
                }
            };
        
            return client;
        }

        public List<FtpUser> GetUsers(FtpClient? client = null)
        {
            var result = new List<FtpUser>();
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute("SITE USERS");
                if (!reply.Success) return result;

                var rawInfo = reply.InfoMessages ?? string.Empty;
                var lines = rawInfo
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("200-") &&
                                 !line.Contains("Detailed User Listing") &&
                                 !line.Contains("Uploaded:") &&
                                 !line.Contains("SITEOPS=") &&
                                 !line.Contains("Total members"))
                    .ToArray();

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        result.Add(new FtpUser
                        {
                            Username = parts[1],
                            Group = parts[2]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Error getting users", ex);
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
            return result;
        }

        public List<FtpGroup> GetGroups(FtpClient? client = null)
        {
            var result = new List<FtpGroup>();
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute("SITE GROUPS");
                if (!reply.Success) return result;

                string info = reply.InfoMessages ?? string.Empty;
                string message = reply.Message ?? string.Empty;
                string fullResponse = info + "\n" + message;

                return fullResponse
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("200- (") && line.Contains(")"))
                    .Select(line =>
                    {
                        var groupMatch = Regex.Match(line, @"200-\s+\(\s*(\d+)\)\s+(\S+)\s+(.*)$");
                        if (groupMatch.Success)
                        {
                            return new FtpGroup
                            {
                                Group = groupMatch.Groups[2].Value,
                                Description = groupMatch.Groups[3].Value.Trim(),
                                UserCount = int.Parse(groupMatch.Groups[1].Value)
                            };
                        }
                        return null;
                    })
                    .Where(g => g != null)
                    .Where(g => !string.IsNullOrWhiteSpace(g!.Group))
                    .DistinctBy(g => g!.Group)
                    .ToList()!;
            }
            catch (Exception ex)
            {
                ShowError("Error getting groups", ex);
                return new List<FtpGroup>();
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public List<(string Username, bool IsSiteOp, bool IsGroupAdmin)> GetUsersInGroup(FtpClient? client, string groupName)
        {
            var users = new List<(string, bool, bool)>();
            bool shouldDispose = client == null;
            
            try
            {
                //Debug.WriteLine($"[FTP] Fetching users for group: {groupName}");
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE GINFO {groupName}");
                //Debug.WriteLine($"[FTP] GINFO reply for {groupName}: {reply.Success} - {reply.Message}");

                if (!reply.Success || string.IsNullOrWhiteSpace(reply.InfoMessages))
                    return users;

                var lines = reply.InfoMessages
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim());

                foreach (var line in lines)
                {
                    if (line.StartsWith("200- |") && line.Count(c => c == '|') >= 7)
                    {
                        var parts = line.Split('|');
                        if (parts.Length > 2)
                        {
                            var usernameWithPrefix = parts[1].Trim();
                            if (!string.IsNullOrWhiteSpace(usernameWithPrefix) &&
                                !usernameWithPrefix.Equals("Username", StringComparison.OrdinalIgnoreCase))
                            {
                                bool isSiteOp = usernameWithPrefix.StartsWith("*");
                                bool isGroupAdmin = usernameWithPrefix.StartsWith("+");
                                string cleanUsername = usernameWithPrefix.TrimStart('*', '+');
                                if (!string.IsNullOrWhiteSpace(cleanUsername))
                                users.Add((cleanUsername, isSiteOp, isGroupAdmin));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error retrieving users for group {groupName}: {ex.Message}");
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
            return users;
        }

        public string AddUser(FtpClient? client, string username, string password, string group, string ipAddress)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                string command = $"SITE GADDUSER {group} {username} {password} {ipAddress}";
                var reply = client.Execute(command);
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string AddGroup(FtpClient? client, string group, string description)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                string command = $"SITE GRPADD {group} {description}";
                var reply = client.Execute(command);
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public List<FtpUser> GetDeletedUsers(FtpClient? client = null)
        {
            var result = new List<FtpUser>();
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute("SITE USERS DELETED");
                if (!reply.Success) return result;

                var rawInfo = reply.InfoMessages ?? string.Empty;
                var lines = rawInfo
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("200-") &&
                                 !line.Contains("Detailed User Listing") &&
                                 !line.Contains("Uploaded:") &&
                                 !line.Contains("SITEOPS=") &&
                                 !line.Contains("Total members"))
                    .ToArray();

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        result.Add(new FtpUser
                        {
                            Username = parts[1],
                            Group = parts[2]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Error getting deleted users", ex);
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
            return result;
        }

        public FtpUserDetails? GetUserDetails(string username, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE USER {username}");
                var details = new FtpUserDetails { Username = username };

                string fullResponse = (reply.InfoMessages ?? string.Empty) + "\n" + (reply.Message ?? string.Empty);

                var usernameMatch = Regex.Match(fullResponse, @"Username:\s+(\S+)");
                if (usernameMatch.Success)
                    details.Username = usernameMatch.Groups[1].Value.Trim();

                var flagsMatch = Regex.Match(fullResponse, @"Flags:\s+([0-9A-Z]+)");
                if (flagsMatch.Success)
                    details.Flags = flagsMatch.Groups[1].Value.Trim();

                var groupsMatch = Regex.Match(fullResponse, @"Groups:\s+([^\|]+)");
                if (groupsMatch.Success)
                    details.Groups.AddRange(groupsMatch.Groups[1].Value.Trim()
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                details.IpRestrictions.Clear();
                var ipLines = fullResponse.Split('\n')
                    .Where(line => line.Contains("| IP") && line.Contains(":"))
                    .ToList();

                foreach (var line in ipLines)
                    ProcessIpLine(line, details);

                return details;
            }
            catch (Exception ex)
            {
                ShowError("Error getting user details", ex);
                return null;
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string ExecuteCommand(string command, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute(command);
                return !string.IsNullOrWhiteSpace(reply.InfoMessages) 
                    ? reply.InfoMessages 
                    : reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string UpdateUserFlags(string username, string flag, bool addFlag, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                string operation = addFlag ? "+" : "-";
                var reply = client.Execute($"SITE CHANGE {username} flags {operation}{flag}");
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string ExecuteDelIpCommand(string username, string ipNumber, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE DELIP {username} {ipNumber}");
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string AddIpRestriction(string username, string ipAddress, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE ADDIP {username} {ipAddress}");
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string DelUser(string username, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE DELUSER {username}");
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        public string PurgeUser(string username, FtpClient? client = null)
        {
            bool shouldDispose = client == null;
            
            try
            {
                client ??= CreateClient();
                if (!client.IsConnected) client.Connect();

                var reply = client.Execute($"SITE PURGE {username}");
                return reply.Message;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (shouldDispose) client?.Dispose();
            }
        }

        private void ProcessIpLine(string line, FtpUserDetails details)
        {
            var ipFields = line.Split(new[] { "IP" }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(f => f.Contains(":"))
                              .ToList();

            foreach (var field in ipFields)
            {
                var parts = field.Split(':');
                if (parts.Length >= 2)
                {
                    var ipNumber = parts[0].Trim();
                    var ipValue = parts[1].Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                         .FirstOrDefault()?.Trim();

                    if (!string.IsNullOrWhiteSpace(ipValue) && ipValue != "|")
                    {
                        details.AddIpRestriction(ipNumber, ipValue);
                    }
                }
            }
        }

        private void ShowError(string title, Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                System.Windows.MessageBox.Show($"{title}: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public class FtpUser
        {
            public string Username { get; set; } = string.Empty;
            public string Group { get; set; } = string.Empty;
            public override bool Equals(object? obj)
            {
                if (obj is FtpUser other)
                    return Username.Equals(other.Username, StringComparison.OrdinalIgnoreCase);
                return false;
            }
            
            public override int GetHashCode() => Username.GetHashCode();

        }

        public class FtpGroup
        {
            public string Group { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int UserCount { get; set; } = 0;
            public override bool Equals(object? obj)
            {
                if (obj is FtpGroup other)
                    return Group.Equals(other.Group, StringComparison.OrdinalIgnoreCase);
                return false;
            }
            
            public override int GetHashCode() => Group.GetHashCode();
        }

        public class FtpUserDetails
        {
            public string Username { get; set; } = string.Empty;
            public Dictionary<string, string> IpRestrictions { get; } = new Dictionary<string, string>();
            public string Flags { get; set; } = string.Empty;
            public List<string> Groups { get; } = new List<string>();

            public void AddIpRestriction(string ipNumber, string ipValue)
            {
                var cleanedNumber = ipNumber.TrimStart('0');
                if (string.IsNullOrEmpty(cleanedNumber))
                    cleanedNumber = "0";
                
                IpRestrictions[$"IP{cleanedNumber}"] = ipValue;
            }
        }
        public static void ClearSessionCaches()
        {
            approvedInSession.Clear();
            promptingThumbprints.Clear();
            rejectedInSession.Clear();
        }
    }
}