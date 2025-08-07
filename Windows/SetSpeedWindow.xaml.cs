using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Input;


namespace glFTPd_Commander.Windows
{
    public partial class SetSpeedWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private readonly FtpClient? _ftpClient;
        private readonly string _username;
        private readonly string _commandField;

        public string Speed => SpeedTextBox.Text.Trim();
        public string? Unit => (UnitsComboBox.SelectedItem as UnitItem)?.Code;
        private string ActionVerb => _commandField.Contains("ul", StringComparison.OrdinalIgnoreCase) ? "Upload" : "Download";

        public SetSpeedWindow(GlFtpdClient ftp, FtpClient ftpClient, string username, string commandField)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _commandField = commandField;

            UnitsComboBox.ItemsSource = UnitProvider.SpeedUnits;
            UnitsComboBox.SelectedIndex = 1;


            this.Title = $"Set {ActionVerb} Speed";
            Loaded += (s, e) => SpeedTextBox.Focus();
        }

        public static async Task<bool> ShowAndSetSpeed(Window owner, GlFtpdClient ftp, FtpClient? ftpClient, string username, string speedType)
        {
            var win = new SetSpeedWindow(ftp, ftpClient!, username, speedType)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (win.ShowDialog() == true)
            {
                string speed = win.Speed;
                string? unit = win.Unit;
                if (string.IsNullOrWhiteSpace(speed) || string.IsNullOrWhiteSpace(unit))
                    return false;
        
                string command = $"SITE CHANGE {username} {speedType} {speed}{unit}";
                await ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, ftpClient, ftp);
                    if (result.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Failed to set speed: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                    return true;
                }
                finally
                {
                    ftp.ConnectionLock.Release();
                }
            }
            return false;
        }



        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Speed), "Please enter a speed.", SpeedTextBox)) return;
            if (string.IsNullOrWhiteSpace(Unit))
            {
                MessageBox.Show("Please select a unit.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                UnitsComboBox.Focus();
                return;
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SpeedInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOnly(sender, e);
        }
    }
}
