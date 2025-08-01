using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace glFTPd_Commander.Utils
{
    public static class UpdateChecker
    {
        private class VersionInfo
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = "0.0.0";
        
            [JsonPropertyName("changelog")]
            public string Changelog { get; set; } = "";
        
            [JsonPropertyName("url")]
            public string Url { get; set; } = "";
        }

        public enum UpdateCheckResult
        {
            UpToDate,
            UpdateAvailable,
            Error
        }

        public static async Task<UpdateCheckResult> CheckForUpdateSilently(bool showMessage = false)
        {
            try
            {
                using var client = new HttpClient();
                string jsonUrl = "https://raw.githubusercontent.com/teqnodude/glFTPd_Commander/master/version.json";
                string json = await client.GetStringAsync(jsonUrl);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json);
                if (versionInfo == null)
                    return UpdateCheckResult.Error;

                int cmp = CompareSemVer(MainWindow.Version, versionInfo.Version);
                /*Debug.WriteLine($"[UpdateCheck] App Version = '{MainWindow.Version}'");
                Debug.WriteLine($"[UpdateCheck] Remote Version = '{versionInfo.Version}'");
                Debug.WriteLine($"[UpdateCheck] SemVer Compare = {CompareSemVer(MainWindow.Version, versionInfo.Version)}");*/

                if (cmp < 0)
                {
                    if (showMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = MessageBox.Show(
                                $"A new version ({versionInfo.Version}) is available!\n\nDo you want to update it now?",
                                "Update Available",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);
                
                            if (result == MessageBoxResult.Yes)
                            {
                                _ = DownloadAndApplyUpdate(versionInfo.Url);
                            }
                        });
                    }
                
                    return UpdateCheckResult.UpdateAvailable;
                }
                else
                {
                    if (showMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("You're running the latest version.", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                
                    return UpdateCheckResult.UpToDate;
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Silent version check failed: {ex.Message}");
                return UpdateCheckResult.Error;
            }
        }

        private static int CompareSemVer(string a, string b)
        {
            string versionA = a.Split('+')[0];
            string versionB = b.Split('+')[0];

            string[] partsA = versionA.Split('-', 2);
            string[] partsB = versionB.Split('-', 2);

            string[] mainA = partsA[0].Split('.');
            string[] mainB = partsB[0].Split('.');

            for (int i = 0; i < Math.Max(mainA.Length, mainB.Length); i++)
            {
                int va = i < mainA.Length && int.TryParse(mainA[i], out int ai) ? ai : 0;
                int vb = i < mainB.Length && int.TryParse(mainB[i], out int bi) ? bi : 0;
                int cmp = va.CompareTo(vb);
                if (cmp != 0) return cmp;
            }

            bool hasPreA = partsA.Length > 1;
            bool hasPreB = partsB.Length > 1;

            if (hasPreA && !hasPreB) return -1;
            if (!hasPreA && hasPreB) return 1;
            if (!hasPreA && !hasPreB) return 0;

            string preA = partsA[1];
            string preB = partsB[1];

            var aTokens = Regex.Split(preA, @"[\.\-]");
            var bTokens = Regex.Split(preB, @"[\.\-]");

            for (int i = 0; i < Math.Max(aTokens.Length, bTokens.Length); i++)
            {
                var tokA = i < aTokens.Length ? aTokens[i] : "";
                var tokB = i < bTokens.Length ? bTokens[i] : "";

                bool isNumA = int.TryParse(tokA, out int intA);
                bool isNumB = int.TryParse(tokB, out int intB);

                if (isNumA && isNumB)
                {
                    int cmp = intA.CompareTo(intB);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    int cmp = string.Compare(tokA, tokB, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                }
            }

            return 0;
        }

        public static async Task DownloadAndApplyUpdate(string zipUrl)
        {
            try
            {
                string tempZip = Path.Combine(Path.GetTempPath(), "glftp_update.zip");
                string extractPath = Path.Combine(Path.GetTempPath(), "glftp_update_extracted");
        
                // Cleanup previous temp files
                if (File.Exists(tempZip)) File.Delete(tempZip);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        
                // Download the update .zip
                using var client = new HttpClient();
                byte[] zipBytes = await client.GetByteArrayAsync(zipUrl);
                File.WriteAllBytes(tempZip, zipBytes);
        
                // Extract update package
                ZipFile.ExtractToDirectory(tempZip, extractPath);
        
                // Launch external Updater.exe
                string updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater.exe");
                if (!File.Exists(updaterPath))
                {
                    MessageBox.Show("Updater.exe not found.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
        
                int parentPid = Process.GetCurrentProcess().Id;
        
                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{extractPath}\" {parentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
        
                Process.Start(startInfo);
        
                // Exit current app
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed:\n{ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public static async Task CheckAndPromptForUpdate()
        {
            var result = await CheckForUpdateSilently(showMessage: false);
            System.Diagnostics.Debug.WriteLine($"[UpdateChecker] CheckAndPromptForUpdate result: {result}");
        
            if (result == UpdateCheckResult.UpdateAvailable)
            {
                // UI code (MessageBox) must be run on UI thread, async logic outside
                MessageBoxResult dialogResult = MessageBoxResult.No;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    dialogResult = MessageBox.Show(
                        "A new version is available! Do you want to download and update now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                });
        
                if (dialogResult == MessageBoxResult.Yes)
                {
                    // Async download logic outside Dispatcher!
                    var versionInfo = await GetLatestVersionInfo();
                    string? url = null;
                    if (versionInfo != null)
                    {
                        // Defensive: try both Url and url
                        url = (versionInfo.Url as string) ?? (versionInfo.url as string) ?? null;
                    }
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        await DownloadAndApplyUpdate(url);
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                "Could not retrieve the update URL.",
                                "Update Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                        });
                    }
                }
            }
        }
        
        public static async Task<dynamic?> GetLatestVersionInfo()
        {
            try
            {
                using var client = new HttpClient();
                string jsonUrl = "https://raw.githubusercontent.com/teqnodude/glFTPd_Commander/master/version.json";
                string json = await client.GetStringAsync(jsonUrl);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
        
                return new
                {
                    Version = root.TryGetProperty("version", out var verProp) ? verProp.GetString() : null,
                    Changelog = root.TryGetProperty("changelog", out var chProp) ? chProp.GetString() : null,
                    Url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateChecker] GetLatestVersionInfo failed: {ex}");
                return null;
            }
        }


    }
}
