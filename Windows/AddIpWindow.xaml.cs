using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Windows;


namespace glFTPd_Commander.Windows
{
    public partial class AddIpWindow : BaseWindow
    {
        public string IPAddress => IpTextBox.Text;

        public AddIpWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) => IpTextBox.Focus();
        }

        public static async Task<string?> ShowAndAddIp(Window owner, GlFtpdClient ftp, FtpClient? ftpClient, string username)
        {
            var addIpWindow = new AddIpWindow
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (addIpWindow.ShowDialog() == true)
            {
                string ipAddress = addIpWindow.IPAddress;
                await ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(
                        $"SITE ADDIP {username} {ipAddress}", ftpClient, ftp);
        
                    if (InputUtils.IsGlftpdIpAddError(result))
                    {
                        MessageBox.Show(result, "IP Add Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }
                    else if (result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(result, "Error Adding IP", MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }
                    return ipAddress;
                }
                finally
                {
                    ftp.ConnectionLock.Release();
                }
            }
            return null;
        }


        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(IpTextBox.Text), "Please enter an IP address", IpTextBox)) return;
            if (InputUtils.ValidateAndWarn(!InputUtils.IsValidGlftpdIp(IpTextBox.Text),
                "Invalid IP restriction format. Examples:\n*@127.0.0.1\n*@127.0.0.*\n*@2001:db8::*\nident@127.0.0.1\nident@2001:db8::1\n*@*", IpTextBox)) return;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
