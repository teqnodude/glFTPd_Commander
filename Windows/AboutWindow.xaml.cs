using glFTPd_Commander.Windows;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using MessageBox = System.Windows.MessageBox;

namespace glFTPd_Commander.Windows
{
    public partial class AboutWindow : BaseWindow
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void CheckForUpdate()
        {
            try
            {
                using var client = new HttpClient();
                string jsonUrl = "https://raw.githubusercontent.com/teqnodude/glFTPd_Commander/master/version.json";
                string json = await client.GetStringAsync(jsonUrl);
        
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json);
                if (versionInfo == null) return;
        
                int cmp = CompareSemVer(MainWindow.Version, versionInfo.Version);
        
                if (cmp < 0)
                {
                    var result = MessageBox.Show(
                        $"A new version is available: {versionInfo.Version}\n\nChangelog:\n{versionInfo.Changelog}\n\nVisit download page?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = versionInfo.Url,
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    MessageBox.Show("You're running the latest version.", "No Update Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AboutWindow] Version check failed: {ex.Message}");
            }
        }



        private void CheckForUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdate();
        }

        private class VersionInfo
        {
            public string Version { get; set; } = "0.0.0";
            public string Changelog { get; set; } = "";
            public string Url { get; set; } = "";
        }

        private static int CompareSemVer(string a, string b)
        {
            // Strip +build metadata
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
        
            if (hasPreA && !hasPreB) return -1; // prerelease is lower
            if (!hasPreA && hasPreB) return 1;
            if (!hasPreA && !hasPreB) return 0;
        
            // Compare prerelease strings (lexical, with numeric fallback)
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
    }
}
