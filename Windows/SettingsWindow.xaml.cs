using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Windows
{
    public partial class SettingsWindow : BaseWindow
    {
        private bool isPasswordVisible = false;
        private readonly ObservableCollection<FtpConnection> connections = [];
        private FtpConnection? currentConnection;

        public SettingsWindow()
        {
            InitializeComponent();
            ConnectionsComboBox.ItemsSource = connections;
            PasswordBox.GotFocus += (s, e) => RevealPassword(true);
            PasswordVisibleTextBox.LostFocus += (s, e) => RevealPassword(false);
            LoadConnections();

            if (connections.Count > 0)
            {
                ConnectionsComboBox.SelectedIndex = 0;
            }
            else
            {
                AddNewConnection();
            }
            Loaded += (s, e) => ConnectionNameTextBox.Focus();
        }
        
        private void LoadConnections()
        {
            connections.Clear();
            foreach (var conn in SettingsManager.GetFtpConnections())
            {
                connections.Add(new FtpConnection
                {
                    Name = conn.Name,
                    Host = conn.Host,
                    Port = conn.Port,
                    Username = conn.Username,
                    Password = conn.Password,
                    Mode = conn.Mode,
                    SslMode = conn.SslMode
                });
            }
        }

        private void ConnectionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectionsComboBox.SelectedIndex >= 0 && ConnectionsComboBox.SelectedIndex < connections.Count)
            {
                currentConnection = connections[ConnectionsComboBox.SelectedIndex];
                DisplayConnection(currentConnection);
            }
        }

        private void DisplayConnection(FtpConnection connection)
        {
            ConnectionNameTextBox.Text = connection.Name;
            ServerTextBox.Text = connection.Host;
            PortTextBox.Text = connection.Port;
            UsernameTextBox.Text = connection.Username;
            PasswordBox.Password = connection.Password;
            PasswordVisibleTextBox.Text = connection.Password;
        
            foreach (ComboBoxItem item in ConnectionTypeComboBox.Items)
            {
                if (item.Tag is string tag && tag.Equals(connection.Mode, StringComparison.OrdinalIgnoreCase))
                {
                    ConnectionTypeComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in SslModeComboBox.Items)
            {
                if (item.Tag is string tag && tag.Equals(connection.SslMode, StringComparison.OrdinalIgnoreCase))
                {
                    SslModeComboBox.SelectedItem = item;
                    break;
                }
            }
            PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
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
            ConnectionsComboBox.SelectedIndex = connections.Count - 1;
            DisplayConnection(currentConnection);
        }

        private void AddConnectionButton_Click(object sender, RoutedEventArgs e) => AddNewConnection();

        private void RemoveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsComboBox.SelectedIndex >= 0)
            {
                // Remove selected connection from the ObservableCollection
                connections.RemoveAt(ConnectionsComboBox.SelectedIndex);
            
                // Save the new list to settings.json via SettingsManager
                var validConnections = connections
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                                !string.IsNullOrWhiteSpace(c.Host) &&
                                !string.IsNullOrWhiteSpace(c.Username) &&
                                !string.IsNullOrWhiteSpace(c.Password) &&
                                !string.IsNullOrWhiteSpace(c.Port))
                    .ToList();
            
                SettingsManager.SetFtpConnections(validConnections);
            
                if (connections.Count == 0)
                {
                    currentConnection = null;
                    DisplayConnection(new FtpConnection()); // Clear fields in UI
            
                    // Optionally update the Connect menu in MainWindow and handle disconnect
                    if (Owner is MainWindow mainWindow)
                    {
                        mainWindow.PopulateConnectMenu();
            
                        // If current connection was removed, force disconnect
                        string? currentEncrypted = mainWindow.CurrentConnectionName;
                        bool stillExists = validConnections.Any(c =>
                            c.Name?.Equals(currentEncrypted, StringComparison.Ordinal) == true);
            
                        if (!stillExists)
                        {
                            mainWindow.ForceDisconnect("The connection you were using has been removed.");
                            mainWindow.PopulateConnectMenu();
                        }
                    }
                }
                else
                {
                    ConnectionsComboBox.SelectedIndex = 0; // Select first remaining item
                    // You don't need to call Save_Click again—it is all handled above
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentConnection == null) return;
        
            string newName = ConnectionNameTextBox.Text.Trim();
            string host = ServerTextBox.Text.Trim();
            string port = PortTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string password = isPasswordVisible ? PasswordVisibleTextBox.Text : PasswordBox.Password;
        
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newName), "Please enter a connection name.", ConnectionNameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(username), "Please enter a username.", UsernameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(password), "Please enter a password.", isPasswordVisible ? PasswordVisibleTextBox : (Control)PasswordBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(host), "Please enter a server.", ServerTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(port), "Please enter a port.", PortTextBox)) return;
        
            if (connections.Any(c => c != currentConnection && c.Name?.Equals(newName, StringComparison.OrdinalIgnoreCase) == true))
            {
                MessageBox.Show("A connection with this name already exists.", "Duplicate Name",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ConnectionNameTextBox.Focus();
                ConnectionNameTextBox.SelectAll();
                return;
            }
        
            currentConnection.Name = newName;
            currentConnection.Host = host;
            currentConnection.Username = username;
            currentConnection.Password = password;
            currentConnection.Port = port;
            currentConnection.Mode = (ConnectionTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "PASV";
            currentConnection.SslMode = (SslModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Explicit";
        
            // Save all connections back to SettingsManager
            var validConnections = connections
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                            !string.IsNullOrWhiteSpace(c.Host) &&
                            !string.IsNullOrWhiteSpace(c.Username) &&
                            !string.IsNullOrWhiteSpace(c.Password) &&
                            !string.IsNullOrWhiteSpace(c.Port))
                .ToList();
        
            SettingsManager.SetFtpConnections(validConnections);
            this.DialogResult = true;
            Close();
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.PopulateConnectMenu();
            }
        }

        private void ServerInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.IpAddressInputFilter(sender, e);
        }

        private void AmountInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOnly(sender, e);
        }

    }
}