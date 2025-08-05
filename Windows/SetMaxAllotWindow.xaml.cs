using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Input;


namespace glFTPd_Commander.Windows
{
    public partial class SetMaxAllotWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private readonly FtpClient _ftpClient;
        private readonly string _group;

        public string Amount => amountText.Text.Trim();
        public string? Unit => (unitsComboBox.SelectedItem as UnitItem)?.Code;

        public SetMaxAllotWindow(GlFtpdClient ftp, FtpClient ftpClient, string group)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _group = group;

            unitsComboBox.ItemsSource = UnitProvider.SizeUnits;
            unitsComboBox.SelectedIndex = 1; // Default to GiB
            Loaded += (s, e) => amountText.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Amount) || Unit == null)
            {
                MessageBox.Show("Please fill in all fields.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = $"SITE GRPCHANGE {_group} max_allot_size {Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand(command, _ftpClient));
                if (result.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to set max allot size: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
