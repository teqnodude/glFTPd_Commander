using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using static glFTPd_Commander.Utils.UpdateChecker;

namespace glFTPd_Commander.Windows
{
    public partial class AboutWindow : BaseWindow
    {
        private string? _updateUrl = null;

        public AboutWindow()
        {
            InitializeComponent();
            Loaded += AboutWindow_Loaded;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void AboutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusText.Visibility = Visibility.Visible;
            UpdateStatusText.Text = "Checking for updates...";
            UpdateStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        
            var result = await UpdateChecker.CheckForUpdateSilently(showMessage: false);
        
            switch (result)
            {
                case UpdateChecker.UpdateCheckResult.UpToDate:
                    UpdateStatusText.Text = "You're running the latest version.";
                    UpdateStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    break;
        
                case UpdateChecker.UpdateCheckResult.UpdateAvailable:
                    UpdateStatusText.Text = "A new version is available.";
                    UpdateStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    // Fetch version info again to get the update URL
                    var versionInfo = await GetLatestVersionInfo();
                    if (!string.IsNullOrEmpty(versionInfo?.Url))
                    {
                        _updateUrl = versionInfo?.Url;
                        UpdateButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        UpdateButton.Visibility = Visibility.Collapsed;
                    }
                    break;
        
                case UpdateChecker.UpdateCheckResult.Error:
                    UpdateStatusText.Text = "Could not check for updates.";
                    UpdateStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    break;
        
                default:
                    UpdateStatusText.Text = "Unknown update result.";
                    UpdateStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    break;
            }
        }

        private async Task<dynamic?> GetLatestVersionInfo()
        {
            try
            {
                using var client = new HttpClient();
                string jsonUrl = "https://raw.githubusercontent.com/teqnodude/glFTPd_Commander/master/version.json";
                string json = await client.GetStringAsync(jsonUrl);
        
                // You can use JsonDocument or dynamic parsing, for simplicity:
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
        
                return new
                {
                    Version = root.GetProperty("version").GetString(),
                    Changelog = root.GetProperty("changelog").GetString(),
                    Url = root.GetProperty("url").GetString()
                };
            }
            catch
            {
                return null;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_updateUrl))
            {
                await UpdateChecker.DownloadAndApplyUpdate(_updateUrl);
            }
            else
            {
                MessageBox.Show("No update URL found.", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
