using FluentFTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Input;


namespace glFTPd_Commander.Windows
{
    public partial class SetAllotmentWindow : BaseWindow
    {
        private readonly FTP _ftp;
        private readonly FtpClient _ftpClient;
        private readonly string _username;

        public string Section => sectionText.Text.Trim();
        public string Amount => amountText.Text.Trim();
        public string? Unit => (unitsComboBox.SelectedItem as UnitItem)?.Code;

        public SetAllotmentWindow(FTP ftp, FtpClient ftpClient, string username)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;

            unitsComboBox.ItemsSource = UnitProvider.SizeUnits;
            unitsComboBox.SelectedIndex = 1; // Default to GiB
            sectionText.Text = "0"; // Default section
            Loaded += (s, e) => amountText.Focus();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Section) || string.IsNullOrWhiteSpace(Amount) || Unit == null)
            {
                MessageBox.Show("Please fill in all fields.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = $"SITE CHANGE {_username} wkly_allotment {Section},{Amount}{Unit}";

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand(command, _ftpClient));
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
