using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using static glFTPd_Commander.Utils.UpdateChecker;
using Debug = System.Diagnostics.Debug;

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
            UpdateButton.Visibility = Visibility.Collapsed; // Always start hidden
        
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
                    var versionInfo = await UpdateChecker.GetLatestVersionInfo();
                    // Defensive: handle both Url and url for dynamic results
                    string? url = null;
                    if (versionInfo != null)
                    {
                        url = versionInfo.Url ?? versionInfo.url;
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        _updateUrl = url;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateButton.Visibility = Visibility.Visible;
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateButton.Visibility = Visibility.Collapsed;
                        });
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
