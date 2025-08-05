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

        private void ConnectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        private void AddConnection_Click(object sender, RoutedEventArgs e) => AddNewConnection();

        private void RemoveConnection_Click(object sender, RoutedEventArgs e)
        {
            if (connectionComboBox.SelectedIndex >= 0)
            {
                // Remove selected connection from the ObservableCollection
                connections.RemoveAt(connectionComboBox.SelectedIndex);
            
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
                    btnSave.IsEnabled = false;
            
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
                    connectionComboBox.SelectedIndex = 0; // Select first remaining item
                    // You don't need to call Save_Click again—it is all handled above
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
        
            if (connections.Any(c => c != currentConnection && c.Name?.Equals(newName, StringComparison.OrdinalIgnoreCase) == true))
            {
                MessageBox.Show("A connection with this name already exists.", "Duplicate Name", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        
            currentConnection.Name = newName;
            currentConnection.Host = host;
            currentConnection.Username = username;
            currentConnection.Password = password;
            currentConnection.Port = port;
            currentConnection.Mode = (cmbMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "PASV";
            currentConnection.SslMode = (cmbSslMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "Explicit";
        
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


        private void InputField_TextChanged(object sender, RoutedEventArgs e)
        {
            bool allFilled = !string.IsNullOrWhiteSpace(txtConnectionName.Text) &&
                             !string.IsNullOrWhiteSpace(txtIP.Text) &&
                             !string.IsNullOrWhiteSpace(txtPort.Text) &&
                             !string.IsNullOrWhiteSpace(txtUsername.Text);

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


        private void Cancel_Click(object sender, RoutedEventArgs e)
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