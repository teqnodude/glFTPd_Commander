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
    public partial class SetMaxAllotWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private readonly FtpClient? _ftpClient;
        private readonly string _group;

        public string Amount => AmountTextBox.Text.Trim();
        public string? Unit => (UnitsComboBox.SelectedItem as UnitItem)?.Code;

        public SetMaxAllotWindow(GlFtpdClient ftp, FtpClient ftpClient, string group)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _group = group;

            UnitsComboBox.ItemsSource = UnitProvider.SizeUnits;
            UnitsComboBox.SelectedIndex = 1; // Default to GiB
            Loaded += (s, e) => AmountTextBox.Focus();
        }

        public static async Task<bool> ShowAndSetMaxAllot(Window owner, GlFtpdClient ftp, FtpClient? ftpClient, string group)
        {
            var win = new SetMaxAllotWindow(ftp, ftpClient!, group)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (win.ShowDialog() == true)
            {
                string amount = win.Amount;
                string? unit = win.Unit;
                if (string.IsNullOrWhiteSpace(amount) || string.IsNullOrWhiteSpace(unit))
                    return false;
        
                string command = $"SITE GRPCHANGE {group} max_allot_size {amount}{unit}";
                await ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, ftpClient, ftp);
                    if (result.Contains("Error", System.StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Failed to set max allot size: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            // Optionally check for unit
            if (string.IsNullOrWhiteSpace(Unit))
            {
                MessageBox.Show("Please select a unit.", "Missing Unit", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void AmountInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOnly(sender, e);
        }
    }
}
