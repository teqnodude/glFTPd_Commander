using FluentFTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Input;


namespace glFTPd_Commander.Windows
{
    public partial class SetSpeedWindow : BaseWindow
    {
        private readonly FTP _ftp;
        private readonly FtpClient _ftpClient;
        private readonly string _username;
        private readonly string _commandField;

        public string Amount => speedText.Text.Trim();
        public string? Unit => (unitsComboBox.SelectedItem as UnitItem)?.Code;
        private string ActionVerb => _commandField.Contains("ul", StringComparison.OrdinalIgnoreCase) ? "Upload" : "Download";

        public SetSpeedWindow(FTP ftp, FtpClient ftpClient, string username, string commandField)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _commandField = commandField;

            unitsComboBox.ItemsSource = UnitProvider.SpeedUnits;
            unitsComboBox.SelectedIndex = 1;


            this.Title = $"Set {ActionVerb} Speed";
            Loaded += (s, e) => speedText.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Amount) || Unit == null)
            {
                MessageBox.Show("Please enter a speed and select a unit.", "Missing Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = $"SITE CHANGE {_username} {_commandField} {Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                string result = await Task.Run(() => _ftp.ExecuteCommand(command, _ftpClient));
                if (result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to set speed: {result}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    DialogResult = true;
                    Close();
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
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
