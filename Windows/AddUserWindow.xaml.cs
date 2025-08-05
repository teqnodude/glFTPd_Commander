using FluentFTP;
using glFTPd_Commander.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace glFTPd_Commander.Windows
{
    public partial class AddUserWindow : BaseWindow
    {
        private readonly FTP _ftp;
        private FtpClient? _ftpClient;

        public string Username => txtNewUsername.Text.Trim();
        public string Password => txtNewPassword.Visibility == Visibility.Visible
            ? txtNewPassword.Password
            : txtPasswordVisible.Text;
        public string SelectedGroup => cmbGroups.SelectedItem?.ToString() ?? string.Empty;
        public string IPAddress => txtIP.Text.Trim();

        public bool AddSuccessful { get; private set; } = false;

        public AddUserWindow(FTP ftp, FtpClient ftpClient)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;

            this.Loaded += async (s, e) =>
            {
                txtNewUsername.Focus();
                await LoadGroupsAsync();
            };
            txtNewPassword.GotFocus += (s, e) => RevealPassword(true);
            txtPasswordVisible.LostFocus += (s, e) => RevealPassword(false);
        }

        private async Task LoadGroupsAsync()
        {
            cmbGroups.ItemsSource = null;
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (groups, updatedClient) = await FtpBase.ExecuteWithConnectionAsync(
                    _ftpClient, _ftp, c => Task.Run(() => (List<FTP.FtpGroup>?)_ftp.GetGroups(c)));
                _ftpClient = updatedClient;
                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server.", "Connection Lost",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    DialogResult = false;
                    Close();
                    return;
                }
                cmbGroups.ItemsSource = groups?.OrderBy(g => g.Group).Select(g => g.Group).ToList() ?? Enumerable.Empty<string>();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void InputField_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddButtonState();
        private void CmbGroups_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAddButtonState();

        private void UpdateAddButtonState()
        {
            btnAdd.IsEnabled = !string.IsNullOrWhiteSpace(Username)
                               && !string.IsNullOrWhiteSpace(Password)
                               && cmbGroups.SelectedItem != null
                               && !string.IsNullOrWhiteSpace(IPAddress);
        }

        private void RevealPassword(bool show)
        {
            if (show)
            {
                txtPasswordVisible.Text = txtNewPassword.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                txtNewPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Focus();
                txtPasswordVisible.SelectAll();
            }
            else
            {
                txtNewPassword.Password = txtPasswordVisible.Text;
                txtNewPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter a username.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewUsername.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show("Please enter a password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewPassword.Focus();
                return;
            }
            if (cmbGroups.SelectedItem == null || string.IsNullOrWhiteSpace(SelectedGroup))
            {
                MessageBox.Show("Please select a group.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbGroups.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(IPAddress))
            {
                MessageBox.Show("Please enter an IP address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtIP.Focus();
                return;
            }

            btnAdd.IsEnabled = false;

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await _ftp.AddUser(_ftpClient, _ftp, Username, Password, SelectedGroup, IPAddress);
                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server.", "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Error);
                    btnAdd.IsEnabled = true;
                    return;
                }
                if (!string.IsNullOrEmpty(result) && !result.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    AddSuccessful = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(result ?? "Unknown error", "Failed to Add User", MessageBoxButton.OK, MessageBoxImage.Error);
                    btnAdd.IsEnabled = true;
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
