using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using System.Diagnostics;
using System.Windows;

namespace glFTPd_Commander.Windows
{
    public partial class AddGroupWindow : BaseWindow
    {
        private readonly GlFtpdClient _ftp;
        private FtpClient? _ftpClient;
        public string GroupName => GroupNameTextBox.Text.Trim();
        public string Description => DescriptionTextBox.Text.Trim();
        public bool AddSuccessful { get; private set; } = false;

        public AddGroupWindow(GlFtpdClient ftp, FtpClient ftpClient)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            Loaded += (s, e) => GroupNameTextBox.Focus();
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(GroupName), "Please enter a group name.", GroupNameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(Description), "Please enter a group description.", DescriptionTextBox)) return;

            // --- Duplicate group check ---
            if (await ExistenceChecks.GroupExistsAsync(_ftp, _ftpClient, GroupName))
            {
                MessageBox.Show("A group with this name already exists. Please choose another group name.",
                                "Duplicate Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                GroupNameTextBox.Focus();
                GroupNameTextBox.SelectAll();
                Debug.WriteLine($"[AddGroupWindow] Prevented adding existing group '{GroupName}'");
                return;
            }

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GRPADD {GroupName} {Description}", _ftpClient, _ftp);

                _ftpClient = updatedClient;
                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server.", "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(result ?? "Unknown error", "Failed to Add Group", MessageBoxButton.OK, MessageBoxImage.Error);
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
