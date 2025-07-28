using FluentFTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace glFTPd_Commander.Windows
{
    public partial class CreditAdjustWindow : BaseWindow
    {
        private readonly FTP _ftp;
        private readonly FtpClient _ftpClient;
        private readonly string _username;
        private readonly string _operation; // GIVE or TAKE
        public string OperationName => _operation.Equals("GIVE", StringComparison.OrdinalIgnoreCase) ? "Add" : "Remove";


        public string Amount => amountText.Text.Trim();
        public string? Unit => (unitsComboBox.SelectedItem as UnitItem)?.Code;

        public List<UnitItem> Units { get; } = new()
        {
            new UnitItem { Display = "MiB", Code = "M" },
            new UnitItem { Display = "GiB", Code = "G" },
            new UnitItem { Display = "TiB", Code = "T" }
        };

        public CreditAdjustWindow(FTP ftp, FtpClient ftpClient, string username, string operation)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _operation = operation.ToUpperInvariant(); // GIVE or TAKE

            unitsComboBox.ItemsSource = Units;
            unitsComboBox.SelectedIndex = 1; // default to GiB

            this.Title = $"{OperationName} Credits";
            this.Loaded += (s, e) => amountText.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Amount) || Unit == null)
            {
                MessageBox.Show("Please enter an amount and select a unit.", "Missing Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = $"SITE {_operation} {_username} {Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var reply = await Task.Run(() => _ftp.ExecuteCommand(command, _ftpClient));
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
            e.Handled = !e.Text.All(char.IsDigit);
        }
    }
}
