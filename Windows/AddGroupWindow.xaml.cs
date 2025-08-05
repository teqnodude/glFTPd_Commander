using FluentFTP;
using glFTPd_Commander.Services;
using System;
using System.Windows;

namespace glFTPd_Commander.Windows
{
    public partial class AddGroupWindow : BaseWindow
    {
        private readonly FTP _ftp;
        private FtpClient? _ftpClient;

        public string GroupName => txtGroupName.Text.Trim();
        public string Description => txtDescription.Text.Trim();
        public bool AddSuccessful { get; private set; } = false;

        public AddGroupWindow(FTP ftp, FtpClient ftpClient)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            Loaded += (s, e) => txtGroupName.Focus();
        }

        private void InputField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => btnAdd.IsEnabled = !string.IsNullOrWhiteSpace(GroupName) && !string.IsNullOrWhiteSpace(Description);

        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GroupName))
            {
                MessageBox.Show("Please enter a group name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtGroupName.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(Description))
            {
                MessageBox.Show("Please enter a description.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtDescription.Focus();
                return;
            }

            btnAdd.IsEnabled = false;

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await _ftp.AddGroup(_ftpClient, _ftp, GroupName, Description);
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
                    MessageBox.Show(result ?? "Unknown error", "Failed to Add Group", MessageBoxButton.OK, MessageBoxImage.Error);
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
