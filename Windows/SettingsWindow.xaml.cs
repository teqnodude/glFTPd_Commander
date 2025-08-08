using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Windows
{
    public partial class SettingsWindow : BaseWindow
    {
        private readonly ObservableCollection<FtpConnection> connections = [];
        private FtpConnection? currentConnection;

        public SettingsWindow()
        {
            InitializeComponent();
            ConnectionsComboBox.ItemsSource = connections;

            AttachPasswordReveal(PasswordBox, PasswordVisibleTextBox);

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

            RevealPassword(PasswordBox, PasswordVisibleTextBox, show: false);

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
        }
        
        private void AddNewConnection()
        {
            var newConn = new FtpConnection
            {
                Name = "",
                Host = "",
                Username = "",
                Password = "",
                Port = "",
                Mode = "PASV",
                SslMode = "Explicit"
            };
            connections.Add(newConn);
            currentConnection = newConn;
            ConnectionsComboBox.SelectedIndex = connections.Count - 1;
            DisplayConnection(newConn);
        }

        private void AddConnectionButton_Click(object sender, RoutedEventArgs e) => AddNewConnection();

        private void RemoveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsComboBox.SelectedIndex < 0 || ConnectionsComboBox.SelectedIndex >= connections.Count)
                return;
        
            var removed = connections[ConnectionsComboBox.SelectedIndex];
            connections.RemoveAt(ConnectionsComboBox.SelectedIndex);
            Debug.WriteLine($"[Settings] Removed connection: {removed?.Name}");
        
            // Persist only valid connections
            var validConnections = connections
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                            !string.IsNullOrWhiteSpace(c.Host) &&
                            !string.IsNullOrWhiteSpace(c.Username) &&
                            !string.IsNullOrWhiteSpace(c.Password) &&
                            !string.IsNullOrWhiteSpace(c.Port))
                .ToList();
        
            SettingsManager.SetFtpConnections(validConnections);
            Debug.WriteLine($"[Settings] Saved {validConnections.Count} valid connections.");
        
            if (connections.Count == 0)
            {
                // Always leave the UI bound to a concrete object so Save works.
                AddNewConnection();
        
                // Update MainWindow (and disconnect if the removed one was active)
                if (Owner is MainWindow mw)
                {
                    mw.PopulateConnectMenu();
                    string? currentEncrypted = mw.CurrentConnectionName;
                    bool stillExists = validConnections.Any(c =>
                        c.Name?.Equals(currentEncrypted, StringComparison.Ordinal) == true);
        
                    if (!stillExists)
                    {
                        mw.ForceDisconnect("The connection you were using has been removed.");
                        mw.PopulateConnectMenu();
                    }
                }
            }
            else
            {
                // Select a valid remaining item and sync currentConnection
                if (ConnectionsComboBox.SelectedIndex < 0)
                    ConnectionsComboBox.SelectedIndex = 0;
        
                currentConnection = connections[ConnectionsComboBox.SelectedIndex];
                DisplayConnection(currentConnection);
        
                Debug.WriteLine($"[Settings] Selected connection after removal: {currentConnection?.Name}");
            }
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentConnection == null) return;
        
            string newName = ConnectionNameTextBox.Text.Trim();
            string host = ServerTextBox.Text.Trim();
            string port = PortTextBox.Text.Trim();
            string username = UsernameTextBox.Text.Trim();
            string password = GetPassword(PasswordBox, PasswordVisibleTextBox);
            Control pwControl = (PasswordVisibleTextBox.Visibility == Visibility.Visible)
                ? (Control)PasswordVisibleTextBox
                : PasswordBox;

            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newName), "Please enter a connection name.", ConnectionNameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(username), "Please enter a username.", UsernameTextBox)) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(password), "Please enter a password.", pwControl)) return;
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