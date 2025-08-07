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
    public partial class SetAllotmentWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private FtpClient? _ftpClient;
        private readonly string _username;

        public string Section => SectionTextBox.Text.Trim();
        public string Amount => AmountTextBox.Text.Trim();
        public string? Unit => (UnitsComboBox.SelectedItem as UnitItem)?.Code;

        public SetAllotmentWindow(GlFtpdClient ftp, FtpClient ftpClient, string username)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;

            UnitsComboBox.ItemsSource = UnitProvider.SizeUnits;
            UnitsComboBox.SelectedIndex = 1; // Default to GiB
            SectionTextBox.Text = "0"; // Default section
            Loaded += (s, e) => AmountTextBox.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Amount), "Please enter an amount.", AmountTextBox)) return;

            string command = $"SITE CHANGE {_username} wkly_allotment {Section},{Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                _ftpClient = updatedClient;
                if (result.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to set allotment: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void AmountInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOnly(sender, e);
        }
    }
}
