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
    public partial class CreditAdjustWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private FtpClient? _ftpClient;
        private readonly string _username;
        private readonly string _operation; // GIVE or TAKE
        public string OperationName => _operation.Equals("GIVE", StringComparison.OrdinalIgnoreCase) ? "Add" : "Remove";
        public string Amount => AmountTextBox.Text.Trim();
        public string? Unit => (UnitsComboBox.SelectedItem as UnitItem)?.Code;

        public CreditAdjustWindow(GlFtpdClient ftp, FtpClient ftpClient, string username, string operation)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _operation = operation.ToUpperInvariant(); // GIVE or TAKE

            UnitsComboBox.ItemsSource = UnitProvider.SizeUnits;
            UnitsComboBox.SelectedIndex = 1; // default to GiB

            this.Title = $"{OperationName} Credits";
            this.Loaded += (s, e) => AmountTextBox.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Amount), "Please enter a credit amount.", AmountTextBox)) return;

            string command = $"SITE {_operation} {_username} {Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (reply, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                _ftpClient = updatedClient;
                if (reply.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to {_operation.ToLower()} credits: {reply}", "Error",
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

        private void AmountInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOnly(sender, e);
        }
    }
}
