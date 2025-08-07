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

namespace glFTPd_Commander.FTP
{
    public partial class GlFtpdClient
    {
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public bool PassiveMode { get; set; } = false;
        public string SslMode { get; set; } = "Explicit";
        private static readonly HashSet<string> approvedInSession = [];
        private static readonly HashSet<string> promptingThumbprints = [];
        private static readonly HashSet<string> rejectedInSession = [];
        private static readonly object promptLock = new ();
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        public SemaphoreSlim ConnectionLock => _connectionLock;

        public GlFtpdClient()
        {
            EncryptionKeyManager.Initialize();
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
                throw new ArgumentException("Host address cannot be empty");

            // Basic host validation
            if (!Uri.CheckHostName(Host).Equals(UriHostNameType.Dns) &&
                !Uri.CheckHostName(Host).Equals(UriHostNameType.IPv4) &&
                !Uri.CheckHostName(Host).Equals(UriHostNameType.IPv6))
            {
                throw new ArgumentException("Invalid host address format");
            }

            string sslMode = SslMode ?? "Explicit";
            var encryptionMode = sslMode.Equals("Implicit", StringComparison.OrdinalIgnoreCase)
                ? FtpEncryptionMode.Implicit
                : FtpEncryptionMode.Explicit;

            var client = new FtpClient(Host)
            {
                Credentials = new NetworkCredential(Username, Password),
                Port = short.TryParse(Port, out short portNum) ? portNum : 21,
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
        
            CertificateStorage.AttachFtpCertificateValidation(client, promptLock, approvedInSession, rejectedInSession, promptingThumbprints, Host);
        
            return client;
        }

        public async Task<List<FtpUser>> GetUsers(FtpClient? client = null)
        {
            var (result, _) = await FtpBase.ExecuteWithConnectionAsync(
                client, this, async c =>
                {
                    // Synchronous parse, but wrapped in Task.Run for offloading.
                    return await Task.Run(() =>
                    {
                        var users = new List<FtpUser>();
                        var reply = c.Execute("SITE USERS");
                        if (!reply.Success) return users;
        
                        var rawInfo = reply.InfoMessages ?? string.Empty;
                        var lines = rawInfo
                            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim())
                            .Where(line => line.StartsWith("200-") &&
                                           !line.Contains("Detailed User Listing") &&
                                           !line.Contains("Uploaded:") &&
                                           !line.Contains("SITEOPS=") &&
                                           !line.Contains("Total members"))
                            .ToArray();
        
                        foreach (var line in lines)
                        {
                            var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                users.Add(new FtpUser
                                {
                                    Username = parts[1],
                                    Group = parts[2]
                                });
                            }
                        }
                        return users;
                    });
                }
            );
            return result ?? [];
        }

        public async Task<List<FtpGroup>> GetGroups(FtpClient? client = null)
        {
            var (result, _) = await FtpBase.ExecuteWithConnectionAsync(
                client, this, async c =>
                {
                    return await Task.Run(() =>
                    {
                        var groups = new List<FtpGroup>();
                        var reply = c.Execute("SITE GROUPS");
                        if (!reply.Success) return groups;
        
                        string info = reply.InfoMessages ?? string.Empty;
                        string message = reply.Message ?? string.Empty;
                        string fullResponse = info + "\n" + message;
        
                        return fullResponse
                            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim())
                            .Where(line => line.StartsWith("200- (") && line.Contains(')'))
                            .Select(line =>
                            {
                                var groupMatch = GroupLineRegex().Match(line);
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
                    });
                }
            );
            return result ?? [];
        }

        public async Task<List<(string Username, bool IsSiteOp, bool IsGroupAdmin)>> GetUsersInGroup(FtpClient? client, string groupName)
        {
            var (result, _) = await FtpBase.ExecuteWithConnectionAsync(
                client, this, async c =>
                {
                    return await Task.Run(() =>
                    {
                        var users = new List<(string, bool, bool)>();
                        var reply = c.Execute($"SITE GINFO {groupName}");
                        if (!reply.Success || string.IsNullOrWhiteSpace(reply.InfoMessages))
                            return users;
        
                        var lines = reply.InfoMessages
                            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
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
                                        bool isSiteOp = usernameWithPrefix.StartsWith('*');
                                        bool isGroupAdmin = usernameWithPrefix.StartsWith('+');
                                        string cleanUsername = usernameWithPrefix.TrimStart('*', '+');
                                        if (!string.IsNullOrWhiteSpace(cleanUsername))
                                            users.Add((cleanUsername, isSiteOp, isGroupAdmin));
                                    }
                                }
                            }
                        }
                        return users;
                    });
                }
            );
            return result ?? [];
        }

        public async Task<List<FtpUser>> GetDeletedUsers(FtpClient? client = null)
        {
            var (result, _) = await FtpBase.ExecuteWithConnectionAsync(
                client, this, async c =>
                {
                    return await Task.Run(() =>
                    {
                        var users = new List<FtpUser>();
                        var reply = c.Execute("SITE USERS DELETED");
                        if (!reply.Success) return users;
        
                        var rawInfo = reply.InfoMessages ?? string.Empty;
                        var lines = rawInfo
                            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim())
                            .Where(line => line.StartsWith("200-") &&
                                           !line.Contains("Detailed User Listing") &&
                                           !line.Contains("Uploaded:") &&
                                           !line.Contains("SITEOPS=") &&
                                           !line.Contains("Total members"))
                            .ToArray();
        
                        foreach (var line in lines)
                        {
                            var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                users.Add(new FtpUser
                                {
                                    Username = parts[1],
                                    Group = parts[2]
                                });
                            }
                        }
                        return users;
                    });
                }
            );
            return result ?? [];
        }

        public async Task<FtpUserDetails?> GetUserDetails(string username, FtpClient? client = null)
        {
            var (result, _) = await FtpBase.ExecuteWithConnectionAsync(
                client, this, async c =>
                {
                    // Use ExecuteFtpCommandWithReconnectAsync instead of direct execution
                    var (response, _) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(
                        $"SITE USER {username}", 
                        c, 
                        this);
                    
                    return await Task.Run(() => ParseUserDetails(response, username));
                }
            );
            return result;
        }
        
        private static FtpUserDetails ParseUserDetails(string fullResponse, string username)
        {
            var details = new FtpUserDetails { Username = username };
        
            var usernameMatch = UsernameRegex().Match(fullResponse);
            if (usernameMatch.Success)
                details.Username = usernameMatch.Groups[1].Value.Trim();
        
            var flagsMatch = FlagsRegex().Match(fullResponse);
            if (flagsMatch.Success)
                details.Flags = flagsMatch.Groups[1].Value.Trim();
        
            var groupsMatch = GroupsRegex().Match(fullResponse);
            if (groupsMatch.Success)
                details.Groups.AddRange(groupsMatch.Groups[1].Value.Trim()
                    .Split([' '], StringSplitOptions.RemoveEmptyEntries));
        
            details.IpRestrictions.Clear();
            var ipLines = fullResponse.Split('\n')
                .Where(line => line.Contains("| IP") && line.Contains(':'))
                .ToList();
        
            foreach (var line in ipLines)
                ProcessIpLine(line, details);
        
            return details;
        }

        private static void ProcessIpLine(string line, FtpUserDetails details)
        {
            // This will match each IP field correctly, even for IPv6 and with multiple fields per line
            foreach (Match m in GlftpdIpRegex().Matches(line))        
            {
                var ipNumber = m.Groups["num"].Value.Trim();
                var ipValue = m.Groups["val"].Value.Trim();
        
                if (!string.IsNullOrWhiteSpace(ipValue))
                {
                    Debug.WriteLine($"[ProcessIpLine] Found IP{ipNumber}: '{ipValue}'");
                    details.AddIpRestriction($"{ipNumber}", ipValue);
                }
            }
        }

        public static async Task<FtpClient?> EnsureConnectedWithUiAsync(GlFtpdClient? ftp, FtpClient? client)
        {
            if (ftp == null)
                return null;

            client = await FtpBase.EnsureConnectedAsync(client, ftp);
            return (client?.IsConnected == true) ? client : null;
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
            public Dictionary<string, string> IpRestrictions { get; } = [];
            public string Flags { get; set; } = string.Empty;
            public List<string> Groups { get; } = [];

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

        [GeneratedRegex(@"200-\s+\(\s*(\d+)\)\s+(\S+)\s+(.*)$")]
        private static partial Regex GroupLineRegex();

        [GeneratedRegex(@"Username:\s+(\S+)")]
        private static partial Regex UsernameRegex();
        
        [GeneratedRegex(@"Flags:\s+([0-9A-Z]+)")]
        private static partial Regex FlagsRegex();
        
        [GeneratedRegex(@"Groups:\s+([^\|]+)")]
        private static partial Regex GroupsRegex();

        [GeneratedRegex(@"IP(?<num>\d+):\s*(?<val>.*?)(?=(IP\d+:|[|]|$))", RegexOptions.Compiled)]
        private static partial Regex GlftpdIpRegex();


    }
}