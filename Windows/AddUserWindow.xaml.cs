using FluentFTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace glFTPd_Commander.Windows
{
    public partial class AddUserWindow : BaseWindow
    {
        public string Username => txtNewUsername.Text;
        public string Password => txtNewPassword.Visibility == Visibility.Visible
            ? txtNewPassword.Password
            : txtPasswordVisible.Text;
        public string SelectedGroup => cmbGroups.SelectedItem?.ToString() ?? string.Empty;
        public string IPAddress => txtIP.Text;
        private FTP _ftp;
        private FtpClient _ftpClient;

        public AddUserWindow(FTP ftp, FtpClient ftpClient)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            this.Loaded += async (s, e) =>
            {
                txtNewUsername.Focus();
                await LoadGroups();
            };
            txtNewPassword.GotFocus += (s, e) => RevealPassword(true);
            txtPasswordVisible.LostFocus += (s, e) => RevealPassword(false);
        }

        private async Task LoadGroups()
        {
            // Synchronous connection check:
            if (!FTP.EnsureConnected(ref _ftpClient, _ftp))
            {
                MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var groups = await Task.Run(() => _ftp.GetGroups(_ftpClient));

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    cmbGroups.ItemsSource = groups
                        .OrderBy(g => g.Group)
                        .Select(g => g.Group)
                        .ToList();
                });
            }
            catch
            {
                MessageBox.Show("Failed to load groups from FTP server", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void InputField_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            btnAdd.IsEnabled = !string.IsNullOrWhiteSpace(txtNewUsername.Text) &&
                              !string.IsNullOrWhiteSpace(txtNewPassword.Password) &&
                              cmbGroups.SelectedItem != null &&
                              !string.IsNullOrWhiteSpace(txtIP.Text);
        }

        private void CmbGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNewUsername.Text))
            {
                MessageBox.Show("Please enter a username",
                              "Validation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                txtNewUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(txtNewPassword.Password))
            {
                MessageBox.Show("Please enter a password",
                              "Validation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                txtNewPassword.Focus();
                return;
            }

            if (cmbGroups.SelectedItem == null || string.IsNullOrWhiteSpace(SelectedGroup))
            {
                MessageBox.Show("Please select a group",
                              "Validation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                cmbGroups.Focus();
                return;
            }

            DialogResult = true;
            Close(); // Explicitly close the window
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


        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close(); // Explicitly close the window
        }
    }
}