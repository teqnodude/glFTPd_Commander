using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;

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

        private void ProjectUrlHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void AboutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateButton.Visibility = Visibility.Collapsed;

            UpdateStatusTextBlock.Visibility = Visibility.Visible;
            UpdateStatusTextBlock.Text = "Checking for updates...";
            UpdateStatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);

            var result = await UpdateChecker.CheckForUpdateSilently(showMessage: false);

            switch (result)
            {
                case UpdateChecker.UpdateCheckResult.UpToDate:
                    UpdateStatusTextBlock.Text = "You're running the latest version.";
                    UpdateStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    break;

                case UpdateChecker.UpdateCheckResult.UpdateAvailable:
                    UpdateStatusTextBlock.Text = "A new version is available.";
                    UpdateStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    var versionInfo = await UpdateChecker.GetLatestVersionInfo();
                    string? url = versionInfo?.Url ?? versionInfo?.url;

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
                    UpdateStatusTextBlock.Text = "Could not check for updates.";
                    UpdateStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    break;

                default:
                    UpdateStatusTextBlock.Text = "Unknown update result.";
                    UpdateStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
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
