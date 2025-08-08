using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace glFTPd_Commander.Windows
{
    public partial class SetPasswordWindow : BaseWindow
    {
        public string Password => GetPassword(PasswordBox, PasswordVisibleTextBox);

        public SetPasswordWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) => PasswordBox.Focus();
            AttachPasswordReveal(PasswordBox, PasswordVisibleTextBox);
        }

        public static async Task<string?> SetPassword(Window owner, GlFtpdClient ftp, FtpClient? ftpClient, string username)
        {
            var setPasswordWindow = new SetPasswordWindow
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (setPasswordWindow.ShowDialog() == true)
            {
                string newPassword = setPasswordWindow.Password;

                Debug.WriteLine($"[SetPasswordWindow] Attempting CHPASS for user: {username}");

                await ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(
                        $"SITE CHPASS {username} {newPassword}", ftpClient, ftp);

                    Debug.WriteLine($"[SetPasswordWindow] CHPASS result: {result}");

                    if (result.Contains("Password not secure enough", StringComparison.OrdinalIgnoreCase))
                    {
                        string msg = ExtractServerMessage(result);
                        MessageBox.Show(owner, msg, "Password Too Weak", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }

                    return newPassword;
                }
                finally
                {
                    ftp.ConnectionLock.Release();
                }
            }
            return null;
        }

        private static string ExtractServerMessage(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return "Unknown error (no response from server).";

            return string.Join(Environment.NewLine,
                reply.Replace("\r", "")
                     .Split('\n')
                     .Select(l => l.Trim())
                     .Where(l => l.Length > 0)
                     .Select(StripFtpCode));
        }

        private static string StripFtpCode(string line)
        {
            if (line.Length >= 4 &&
                char.IsDigit(line[0]) && char.IsDigit(line[1]) && char.IsDigit(line[2]) &&
                (line[3] == ' ' || line[3] == '-'))
            {
                return line[4..].Trim();
            }
            return line;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPassword = GetPassword(PasswordBox, PasswordVisibleTextBox);
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(currentPassword), "Please enter a password",
                PasswordVisibleTextBox.Visibility == Visibility.Visible ? PasswordVisibleTextBox : PasswordBox))
                return;

            RevealPassword(PasswordBox, PasswordVisibleTextBox, show: false);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }
    }
}
