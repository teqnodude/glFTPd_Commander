using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace glFTPd_Commander.Windows
{
    public partial class SettingsWindow : BaseWindow
    {
        private readonly string settingsPath = ConfigurationManager.AppSettings["FtpConfigPath"] ?? "ftpconfig.txt";
        private bool isPasswordVisible = false;
        private ObservableCollection<FtpConnection> connections = new();
        private FtpConnection? currentConnection;

        public SettingsWindow()
        {
            InitializeComponent();
            connectionComboBox.ItemsSource = connections;
            txtPassword.GotFocus += (s, e) => RevealPassword(true);
            txtPasswordVisible.LostFocus += (s, e) => RevealPassword(false);
            LoadConnections();

            if (connections.Count > 0)
            {
                connectionComboBox.SelectedIndex = 0;
            }
            else
            {
                AddNewConnection();
            }
            Loaded += (s, e) => txtConnectionName.Focus();
        }

        public class FtpConnection
        {
            public string? Name { get; set; }
            public string? SslMode { get; set; }
            public string? Host { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? Port { get; set; }
            public string? Mode { get; set; }
        }

        private void LoadConnections()
        {
            connections.Clear();
        
            if (!File.Exists(settingsPath))
                return;
        
            var lines = File.ReadAllLines(settingsPath);
            FtpConnection? current = null;
        
            foreach (var line in lines)
            {
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (current != null)
                    {
                        connections.Add(current);
                    }
                    current = new FtpConnection();
                    var encryptedName = line.Trim('[', ']');
                    current.Name = FTP.TryDecryptString(encryptedName) ?? encryptedName;
                }
                else if (current != null)
                {
                    if (line.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
                    {
                        var encryptedHost = line.Substring("Host=".Length).Trim();
                        current.Host = FTP.TryDecryptString(encryptedHost) ?? encryptedHost;
                    }
                    else if (line.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                    {
                        var encryptedPort = line.Substring("Port=".Length).Trim();
                        current.Port = FTP.TryDecryptString(encryptedPort) ?? encryptedPort;
                    }
                    else if (line.StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
                    {
                        var encryptedUsername = line.Substring("Username=".Length).Trim();
                        current.Username = FTP.TryDecryptString(encryptedUsername) ?? encryptedUsername;
                    }
                    else if (line.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                    {
                        var encryptedPassword = line.Substring("Password=".Length).Trim();
                        current.Password = FTP.TryDecryptString(encryptedPassword) ?? encryptedPassword;
                    }
                    else if (line.StartsWith("Type=", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Mode = line.Substring("Type=".Length).Trim();
                    }
                    else if (line.StartsWith("SslMode=", StringComparison.OrdinalIgnoreCase))
                    {
                        current.SslMode = line.Substring("SslMode=".Length).Trim();
                    }
                }
            }
        
            if (current != null)
            {
                connections.Add(current);
            }
        
        }

        private void connectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (connectionComboBox.SelectedIndex >= 0 && connectionComboBox.SelectedIndex < connections.Count)
            {
                currentConnection = connections[connectionComboBox.SelectedIndex];
                DisplayConnection(currentConnection);
            }
        }

        private void DisplayConnection(FtpConnection connection)
        {
            txtConnectionName.Text = connection.Name;
            txtIP.Text = connection.Host;
            txtPort.Text = connection.Port;
            txtUsername.Text = connection.Username;
            txtPassword.Password = connection.Password;
            txtPasswordVisible.Text = connection.Password;
        
            foreach (ComboBoxItem item in cmbMode.Items)
            {
                if (item.Tag is string tag && tag.Equals(connection.Mode, StringComparison.OrdinalIgnoreCase))
                {
                    cmbMode.SelectedItem = item;
                    break;
                }
            }

            foreach (ComboBoxItem item in cmbSslMode.Items)
            {
                if (item.Tag is string tag && tag.Equals(connection.SslMode, StringComparison.OrdinalIgnoreCase))
                {
                    cmbSslMode.SelectedItem = item;
                    break;
                }
            }
            
            // Ensure password is hidden by default
            txtPasswordVisible.Visibility = Visibility.Collapsed;
            txtPassword.Visibility = Visibility.Visible;
            isPasswordVisible = false;
        }
        
        private void AddNewConnection()
        {
            currentConnection = new FtpConnection
            {
                Name = "",
                Host = "",
                Username = "",
                Password = "",
                Port = "",
                Mode = "PASV",
                SslMode = "Explicit"
            };
            connections.Add(currentConnection);
            connectionComboBox.SelectedIndex = connections.Count - 1;
            DisplayConnection(currentConnection);
        }

        private void btnAddConnection_Click(object sender, RoutedEventArgs e) => AddNewConnection();

        private void btnRemoveConnection_Click(object sender, RoutedEventArgs e)
        {
            if (connectionComboBox.SelectedIndex >= 0)
            {
                connections.RemoveAt(connectionComboBox.SelectedIndex);
        
                if (connections.Count == 0)
                {
                    currentConnection = null;
                    if (File.Exists(settingsPath))
                        File.Delete(settingsPath);
        
                    DisplayConnection(new FtpConnection()); // Clear fields in UI
                    btnSave.IsEnabled = false;

                    if (Owner is MainWindow mainWindow)
                    {
                        mainWindow.PopulateConnectMenu();
                    
                        // Disconnect if the removed connection is the one in use
                        string? currentEncrypted = mainWindow.CurrentConnectionName;
                        bool stillExists = FTP.GetAllConnections().Any(c =>
                            c["Name"].Equals(currentEncrypted, StringComparison.Ordinal));
                    
                        if (!stillExists)
                        {
                            mainWindow.ForceDisconnect("The connection you were using has been removed.");
                        }
                    }
                }
                else
                {
                    connectionComboBox.SelectedIndex = 0; // Select first remaining item
                    Save_Click(null!, null!); // Persist updated list
                }
            }
        }


        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (currentConnection == null) return;
        
            string newName = txtConnectionName.Text.Trim();
            string host = txtIP.Text.Trim();
            string port = txtPort.Text.Trim();
            string username = txtUsername.Text.Trim();
            string password = isPasswordVisible ? txtPasswordVisible.Text : txtPassword.Password;
        
            // Check for duplicate names (excluding current connection)
            if (connections.Any(c => c != currentConnection && c.Name?.Equals(newName, StringComparison.OrdinalIgnoreCase) == true))
            {
                MessageBox.Show("A connection with this name already exists.", "Duplicate Name", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        
            // Update current connection with new values (don't encrypt yet)
            currentConnection.Name = newName;
            currentConnection.Host = host;
            currentConnection.Username = username;
            currentConnection.Password = password;
            currentConnection.Port = port;
            currentConnection.Mode = (cmbMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "PASV";
            currentConnection.SslMode = (cmbSslMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "Explicit";
        
            try
            {
                var validConnections = connections
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                                !string.IsNullOrWhiteSpace(c.Host) &&
                                !string.IsNullOrWhiteSpace(c.Username) &&
                                !string.IsNullOrWhiteSpace(c.Password) &&
                                !string.IsNullOrWhiteSpace(c.Port))
                    .ToList();
                
                if (validConnections.Count == 0)
                {
                    if (File.Exists(settingsPath))
                        File.Delete(settingsPath);
                
                    DialogResult = true;
                    Close();
                    return;
                }

                var configContent = new StringBuilder();

                foreach (var conn in connections)
                {
                    // Encrypt all sensitive fields before saving
                    var encryptedName = FTP.EncryptString(conn.Name ?? "");
                    var encryptedPort = FTP.EncryptString(conn.Port ?? "");
                    var encryptedHost = FTP.EncryptString(conn.Host ?? "");
                    var encryptedUsername = FTP.EncryptString(conn.Username ?? "");
                    var encryptedPassword = FTP.EncryptString(conn.Password ?? "");

        
                    configContent.AppendLine($"[{encryptedName}]");
                    configContent.AppendLine($"Host={encryptedHost}");
                    configContent.AppendLine($"Port={encryptedPort}");
                    configContent.AppendLine($"Username={encryptedUsername}");
                    configContent.AppendLine($"Password={encryptedPassword}");
                    configContent.AppendLine($"Type={conn.Mode}");
                    configContent.AppendLine($"SslMode={conn.SslMode}");
                    configContent.AppendLine();
                }

        
                File.WriteAllText(settingsPath, configContent.ToString());
                this.DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving connections: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InputField_TextChanged(object sender, RoutedEventArgs e)
        {
            bool allFilled = !string.IsNullOrWhiteSpace(txtConnectionName.Text) &&
                             !string.IsNullOrWhiteSpace(txtIP.Text) &&
                             !string.IsNullOrWhiteSpace(txtPort.Text) &&
                             !string.IsNullOrWhiteSpace(txtUsername.Text) &&
                             !string.IsNullOrWhiteSpace((isPasswordVisible ? txtPasswordVisible.Text : txtPassword.Password));
        
            btnSave.IsEnabled = allFilled;
        }

        private void RevealPassword(bool show)
        {
            if (show)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Focus();
                txtPasswordVisible.SelectAll();
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
            }
        }


        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}