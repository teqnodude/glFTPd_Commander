using FluentFTP;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace glFTPd_Commander.Services
{
    public static class CertificateStorage
    {
        private static readonly Lock fileLock = new();
        private static Dictionary<string, Dictionary<string, string>> _certificates = [];
        private static readonly JsonSerializerOptions s_writeIndentedOptions = new() { WriteIndented = true };

        
        static CertificateStorage()
        {
            LoadCertificates();
        }
        
        private static string GetCertificatesFilePath()
        {
            return "accepted_certs.json";
        }

        private static void LoadCertificates()
        {
            lock (fileLock)
            {
                try
                {
                    if (!File.Exists(GetCertificatesFilePath()))
                    {
                        _certificates = [];
                        return;
                    }
                    
                    string json = File.ReadAllText(GetCertificatesFilePath());
                    if (!string.IsNullOrWhiteSpace(json) && json != "{}")
                    {
                        var loadedCerts = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) 
                                        ?? [];
                        
                        // Migrate any unencrypted connection names to encrypted format
                        _certificates = [];
                        foreach (var pair in loadedCerts)
                        {
                            string encryptedKey;
                            try
                            {
                                // If the key can be decrypted, it was already encrypted
                                var decrypted = DecryptString(pair.Key);
                                encryptedKey = pair.Key; // Keep the encrypted version
                            }
                            catch
                            {
                                // If decryption fails, this was an unencrypted key - encrypt it
                                encryptedKey = EncryptString(pair.Key);
                            }
                            
                            _certificates[encryptedKey] = pair.Value;
                        }
                    }
                    else
                    {
                        _certificates = [];
                    }
                }
                catch
                {
                    _certificates = [];
                }
            }
        }
        
        public static bool IsCertificateApproved(string thumbprint, string? connectionName = null)
        {
            lock (fileLock)
            {
                // Encrypt the connection name for lookup
                var encryptedConnectionName = string.IsNullOrEmpty(connectionName) 
                    ? "" 
                    : EncryptString(connectionName);
        
                // Check connection-specific first
                if (!string.IsNullOrEmpty(encryptedConnectionName) 
                    && _certificates.TryGetValue(encryptedConnectionName, out var connCerts))
                {
                    foreach (var encryptedThumbprint in connCerts.Keys)
                    {
                        var decryptedThumbprint = TryDecryptString(encryptedThumbprint);
                        if (decryptedThumbprint == thumbprint)
                            return true;
                    }
                }
                
                // Check global certificates
                if (_certificates.TryGetValue("", out var globalCerts))
                {
                    foreach (var encryptedThumbprint in globalCerts.Keys)
                    {
                        var decryptedThumbprint = TryDecryptString(encryptedThumbprint);
                        if (decryptedThumbprint == thumbprint)
                            return true;
                    }
                }
                
                return false;
            }
        }

        public static void AttachFtpCertificateValidation(
            FtpClient client,
            object promptLock,
            HashSet<string> approvedInSession,
            HashSet<string> rejectedInSession,
            HashSet<string> promptingThumbprints,
            string host)
        {
            client.ValidateCertificate += (sender, e) =>
            {
                if (e.Certificate == null)
                {
                    e.Accept = false;
                    return;
                }
        
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
        
                    if (IsCertificateApproved(thumbprint, host))
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
                            ApproveCertificate(thumbprint, subject, rememberDecision, host);
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
            };
        }

        
        public static void ApproveCertificate(string thumbprint, string subject, bool remember, string? connectionName = null)
        {
            if (!remember) return;
        
            lock (fileLock)
            {
                // Encrypt the connection name if provided
                var encryptedConnectionName = string.IsNullOrEmpty(connectionName) 
                    ? "" 
                    : EncryptString(connectionName);
                
                if (!_certificates.TryGetValue(encryptedConnectionName, out var value))
                {
                    value = [];
                    _certificates[encryptedConnectionName] = value;
                }
                
                // Encrypt both thumbprint and subject
                var encryptedThumbprint = EncryptString(thumbprint);
                var encryptedSubject = EncryptString(subject);
                value[encryptedThumbprint] = encryptedSubject;
        
                File.WriteAllText(GetCertificatesFilePath(), 
                    JsonSerializer.Serialize(_certificates, s_writeIndentedOptions));
            }
        }

        public static string? GetCertificateSubject(string thumbprint, string? connectionName = null)
        {
            lock (fileLock)
            {
                // Encrypt the connection name for lookup
                var encryptedConnectionName = string.IsNullOrEmpty(connectionName) 
                    ? "" 
                    : EncryptString(connectionName);
        
                // Check connection-specific first
                if (!string.IsNullOrEmpty(encryptedConnectionName) 
                    && _certificates.TryGetValue(encryptedConnectionName, out var connCerts))
                {
                    foreach (var pair in connCerts)
                    {
                        var decryptedThumbprint = TryDecryptString(pair.Key);
                        if (decryptedThumbprint == thumbprint)
                        {
                            return TryDecryptString(pair.Value);
                        }
                    }
                }
                
                // Check global certificates
                if (_certificates.TryGetValue("", out var globalCerts))
                {
                    foreach (var pair in globalCerts)
                    {
                        var decryptedThumbprint = TryDecryptString(pair.Key);
                        if (decryptedThumbprint == thumbprint)
                        {
                            return TryDecryptString(pair.Value);
                        }
                    }
                }
                
                return null;
            }
        }

        private static string? TryDecryptString(string cipherText)
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

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;
        
            try
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
            catch
            {
                return plainText; // Return original if encryption fails
            }
        }

        private static string DecryptString(string cipherText)
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
    }

    public static class EncryptionKeyManager
    {
        private const string KeyFilePath = "keyinfo.dat";
        private static readonly System.Threading.Lock _lock = new();
        public static byte[] Key { get; private set; } = [];
        public static byte[] IV { get; private set; } = [];

        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(KeyFilePath))
                        LoadKeys();
                    else
                        GenerateAndStoreKeys();
                }
                catch
                {
                    GenerateAndStoreKeys();
                }
            }
        }

        private static void GenerateAndStoreKeys()
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();

            Key = aes.Key;
            IV = aes.IV;

            try
            {
                var dir = Path.GetDirectoryName(KeyFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                using var fs = new FileStream(KeyFilePath, FileMode.Create);
                fs.Write(Key, 0, Key.Length);
                fs.Write(IV, 0, IV.Length);
                File.SetAttributes(KeyFilePath, File.GetAttributes(KeyFilePath) | FileAttributes.Hidden);
            }
            catch
            {
                throw;
            }
        }

        private static void LoadKeys()
        {
            try
            {
                using var fs = new FileStream(KeyFilePath, FileMode.Open);
                Key = new byte[32];
                IV = new byte[16];
                
                if (fs.Read(Key, 0, Key.Length) != Key.Length || 
                    fs.Read(IV, 0, IV.Length) != IV.Length)
                    throw new InvalidOperationException("Invalid key file format");
            }
            catch
            {
                GenerateAndStoreKeys();
            }
        }
    }
}