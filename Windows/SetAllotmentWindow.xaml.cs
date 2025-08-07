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
        private readonly FtpClient? _ftpClient;
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

        public static async Task<bool> ShowAndSetAllotment(Window owner, GlFtpdClient ftp, FtpClient? ftpClient, string username)
        {
            var window = new SetAllotmentWindow(ftp, ftpClient!, username)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (window.ShowDialog() == true)
            {
                var amount = window.Amount;
                var section = window.Section;
                var unit = window.Unit;
                if (!decimal.TryParse(amount, out _))
                {
                    MessageBox.Show("Amount must be a number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (!int.TryParse(section, out _))
                {
                    MessageBox.Show("Section must be a number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                string command = $"SITE CHANGE {username} wkly_allotment {section},{amount}{unit}";
                await ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, ftpClient, ftp);
                    ftpClient = updatedClient;
                    if (result.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Failed to set allotment: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Amount), "Please enter an amount.", AmountTextBox)) return;

            DialogResult = true;
            Close();
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
