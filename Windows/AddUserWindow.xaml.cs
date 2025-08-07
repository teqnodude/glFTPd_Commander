using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace glFTPd_Commander.Windows
{
    public partial class AddUserWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private FtpClient? _ftpClient;

        public string Username => UsernameTextBox.Text.Trim();
        public string Password => PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password
            : PasswordVisibleTextBox.Text;
        public string SelectedGroup => GroupsComboBox.SelectedItem?.ToString() ?? string.Empty;
        public string IpAddress => IpTextBox.Text.Trim();

        public bool AddSuccessful { get; private set; } = false;

        public AddUserWindow(GlFtpdClient ftp, FtpClient ftpClient)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;

            Loaded += async (s, e) =>
            {
                UsernameTextBox.Focus();
                await LoadGroupsAsync();
            };
            PasswordBox.GotFocus += (s, e) => RevealPassword(true);
            PasswordVisibleTextBox.LostFocus += (s, e) => RevealPassword(false);
        }

        private async Task LoadGroupsAsync()
        {
            GroupsComboBox.ItemsSource = null;
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (groups, updatedClient) = await FtpBase.ExecuteWithConnectionAsync(
                    _ftpClient, _ftp, c => _ftp.GetGroups(c));
                _ftpClient = updatedClient;
                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server.", "Connection Lost",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    DialogResult = false;
                    Close();
                    return;
                }
                GroupsComboBox.ItemsSource = groups?.OrderBy(g => g.Group).Select(g => g.Group).ToList() ?? Enumerable.Empty<string>();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void RevealPassword(bool show)
        {
            if (show)
            {
                PasswordVisibleTextBox.Text = PasswordBox.Password;
                PasswordVisibleTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordVisibleTextBox.Focus();
                PasswordVisibleTextBox.SelectAll();
            }
            else
            {
                PasswordBox.Password = PasswordVisibleTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Username), "Please enter a username.", UsernameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Password), "Please enter a password.", PasswordBox)) return;
            if (InputUtils.ValidateAndWarn(GroupsComboBox.SelectedItem == null || string.IsNullOrWhiteSpace(SelectedGroup), "Please select a group.", GroupsComboBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(IpAddress), "Please enter an IP address.", IpTextBox)) return;
            if (InputUtils.ValidateAndWarn(!InputUtils.IsValidGlftpdIp(IpAddress),
                "Invalid IP restriction format. Examples:\n*@127.0.0.1\n*@127.0.0.*\n*@2001:db8::*\nident@127.0.0.1\nident@2001:db8::1\n*@*", IpTextBox)) return;

            // --- Duplicate check ---
            if (await ExistenceChecks.UsernameExistsAsync(_ftp, _ftpClient, Username))
            {
                MessageBox.Show("A user with this name already exists. Please choose another username.",
                                "Duplicate Username", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Focus();
                UsernameTextBox.SelectAll();
                Debug.WriteLine($"[AddUserWindow] Prevented adding existing username '{Username}'");
                AddButton.IsEnabled = true;
                return;
            }

            AddButton.IsEnabled = false;

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var command = $"SITE GADDUSER {SelectedGroup} {Username} {Password} {IpAddress}";
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);

                _ftpClient = updatedClient;

                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server.", "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddButton.IsEnabled = true;
                    return;
                }
                if (!string.IsNullOrEmpty(result) && !result.StartsWith("Error", System.StringComparison.OrdinalIgnoreCase))
                {
                    AddSuccessful = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(result ?? "Unknown error", "Failed to Add User", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddButton.IsEnabled = true;
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
    }
}
