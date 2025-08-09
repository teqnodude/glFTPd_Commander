using FluentFTP;
using FluentFTP.Exceptions;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static glFTPd_Commander.FTP.GlFtpdClient;
using Debug = System.Diagnostics.Debug;

namespace glFTPd_Commander
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private GlFtpdClient? _ftp;
        private FtpClient? _ftpClient;
        private string? _currentConnectionEncryptedName;
        public string? CurrentConnectionName => _currentConnectionEncryptedName;
        private DateTime connectionStartTime;
        private DispatcherTimer? connectionTimer;
        private string? baseTitle;
        private bool _isLoading = false;
        private bool _popupOpen = false;
        private readonly HashSet<string> expandedKeys = [];
        public bool IsConnected => _ftpClient != null && _ftpClient.IsConnected;
        public ObservableCollection<FtpTreeItem> RootItems { get; } = [];
        public static string Version => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Split('+')[0] ?? "Unknown";

        public static string Author => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Unknown";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        private readonly List<string> _commandHistory = [];
        public ObservableCollection<CustomCommandSlot> CustomCommandSlots { get; } = [];
        public ICommand CustomCommandSlotClickCommand { get; }
        public ICommand RemoveCustomCommandCommand { get; }
        private bool _isReconnecting = false;
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 5;

        public class FtpTreeItem : INotifyPropertyChanged
        {
            private string? _name;
            private bool _isDeletedUser;
            private bool _isRoot;
            private bool _isSiteOp;
            private bool _isGroupAdmin;
            private bool _isExpanded;
        
            public string? Name
            {
                get => _name;
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        OnPropertyChanged(nameof(Name));
                    }
                }
            }
        
            public ObservableCollection<FtpTreeItem> Children { get; set; } = [];
        
            public FtpUser? User { get; set; }
            public FtpGroup? Group { get; set; }
        
            public bool IsUser => User != null && !IsDeletedUser;
        
            public bool IsDeletedUser
            {
                get => _isDeletedUser;
                set
                {
                    if (_isDeletedUser != value)
                    {
                        _isDeletedUser = value;
                        OnPropertyChanged(nameof(IsDeletedUser));
                    }
                }
            }
        
            public bool IsGroup => Group != null;
        
            public bool IsRoot
            {
                get => _isRoot;
                set
                {
                    if (_isRoot != value)
                    {
                        _isRoot = value;
                        OnPropertyChanged(nameof(IsRoot));
                    }
                }
            }
        
            public bool IsSiteOp
            {
                get => _isSiteOp;
                set
                {
                    if (_isSiteOp != value)
                    {
                        _isSiteOp = value;
                        OnPropertyChanged(nameof(IsSiteOp));
                        OnPropertyChanged(nameof(Icon)); 
                    }
                }
            }
        
            public bool IsGroupAdmin
            {
                get => _isGroupAdmin;
                set
                {
                    if (_isGroupAdmin != value)
                    {
                        _isGroupAdmin = value;
                        OnPropertyChanged(nameof(IsGroupAdmin));
                        OnPropertyChanged(nameof(Icon)); 
                    }
                }
            }
        
            public string? Icon
            {
                get
                {
                    if (IsRoot) return "pack://application:,,,/Resources/Icons/server.png";
                    if (IsGroup) return "pack://application:,,,/Resources/Icons/group.png";
                    if (IsDeletedUser) return "pack://application:,,,/Resources/Icons/deleted.png";
                    if (IsSiteOp) return "pack://application:,,,/Resources/Icons/siteop.png";
                    if (IsGroupAdmin) return "pack://application:,,,/Resources/Icons/groupadmin.png";
                    if (IsUser) return "pack://application:,,,/Resources/Icons/user.png";
                    if (Name != null)
                    {
                        if (Name.StartsWith("Users (")) return "pack://application:,,,/Resources/Icons/users.png";
                        if (Name.StartsWith("Active Users")) return "pack://application:,,,/Resources/Icons/activeusers.png";
                        if (Name.StartsWith("Deleted Users")) return "pack://application:,,,/Resources/Icons/deletedusers.png";
                        if (Name.StartsWith("Groups (")) return "pack://application:,,,/Resources/Icons/groups.png";
                    }
                    return "pack://application:,,,/Resources/Icons/unknown.png";
                }
            }
        
            public bool IsExpanded
            {
                get => _isExpanded;
                set
                {
                    if (_isExpanded != value)
                    {
                        _isExpanded = value;
                        OnPropertyChanged(nameof(IsExpanded));
                    }
                }
            }
        
            public string UniqueKey
            {
                get
                {
                    if (IsUser && User != null)
                        return $"USER:{User.Username.ToLowerInvariant()}";
                    if (IsDeletedUser && User != null)
                        return $"DELUSER:{User.Username.ToLowerInvariant()}";
                    if (IsGroup && Group != null)
                        return $"GROUP:{Group.Group.ToLowerInvariant()}";
                    if (IsRoot)
                        return "ROOT";
                    // For folder nodes
                    if (Name != null)
                    {
                        if (Name.StartsWith("Active Users"))
                            return "ACTIVE_USERS_FOLDER";
                        if (Name.StartsWith("Deleted Users"))
                            return "DELETED_USERS_FOLDER";
                        if (Name.StartsWith("Users ("))
                            return "USERS_ROOT";
                        if (Name.StartsWith("Groups ("))
                            return "GROUPS_ROOT";
                    }
                    return Name ?? Guid.NewGuid().ToString();
                }
            }
        
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        public MainWindow()
        {
            InitializeComponent();
            PopulateConnectMenu(); 
            this.DataContext = this;
            this.Title = $"glFTPd Commander v{Version} by {Author} - Not connected";
            DisconnectMenuItem.IsEnabled = false;
            UsersGroupsMenuItem.Visibility = Visibility.Collapsed;
            Loaded += async (s, e) => await glFTPd_Commander.Utils.UpdateChecker.CheckAndPromptForUpdate();

            // Load slots from SettingsManager (keep your ObservableCollection as UI source)
            CustomCommandSlotClickCommand = new RelayCommand<CustomCommandSlot>(OnCustomCommandSlotClicked);
            RemoveCustomCommandCommand = new RelayCommand<CustomCommandSlot>(OnRemoveCustomCommand);
            
            // Load slots from SettingsManager (fill your ObservableCollection)
            CustomCommandSlots.Clear();
            var loadedSlots = SettingsManager.GetCustomCommandSlots();
            
            // Always 20 slots for UI, fill or pad as needed
            for (int i = 0; i < 20; i++)
            {
                if (i < loadedSlots.Count)
                    CustomCommandSlots.Add(loadedSlots[i]);
                else
                    CustomCommandSlots.Add(new CustomCommandSlot());
            }

            // Restore the placement of app on startup
            var placement = SettingsManager.GetMainWindowPlacement();
            if (placement != null)
            {
                this.Left = placement.Left;
                this.Top = placement.Top;
                this.Width = placement.Width;
                this.Height = placement.Height;
                if (placement.State == "Maximized")
                    this.WindowState = WindowState.Maximized;
                else if (placement.State == "Minimized")
                    this.WindowState = WindowState.Minimized;
                else
                    this.WindowState = WindowState.Normal;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CloseFtpClient("Main window closed");

            // Save the placement of app on close
            var info = new WindowPlacementInfo
            {
                Left = this.Left,
                Top = this.Top,
                Width = this.Width,
                Height = this.Height,
                State = this.WindowState.ToString()
            };
            SettingsManager.SetMainWindowPlacement(info);
            base.OnClosed(e);
        }

        private async void LoadFtpData()
        {
            if (_isLoading) return;
            _isLoading = true;
       
            await (_ftp?.ConnectionLock?.WaitAsync() ?? Task.CompletedTask);
        
            try
            {
                if (_ftp == null)
                {
                    MessageBox.Show("FTP connection is not initialized.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                CloseFtpClient("Reloading FTP data");
                
                // Create ONE new connection for this batch
                _ftpClient = await FtpBase.EnsureConnectedAsync(null, _ftp);
                if (_ftpClient == null) {
                    MessageBox.Show("Failed to connect to FTP server.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ftp?.ConnectionLock.Release();
                    _isLoading = false;
                    return;
                }
        
                // Start tasks in parallel using the new async GlFtpdClient API
                var usersTask = _ftp.GetUsers(_ftpClient);
                var groupsTask = _ftp.GetGroups(_ftpClient);
                var deletedUsersTask = _ftp.GetDeletedUsers(_ftpClient);
        
                await Task.WhenAll(usersTask, groupsTask, deletedUsersTask);
        
                var users = usersTask.Result ?? [];
                var groups = groupsTask.Result ?? [];
                var deletedUsers = deletedUsersTask.Result ?? [];
        
                var root = new FtpTreeItem { Name = $"FTP Server: {_currentConnectionEncryptedName}", IsRoot = true };
        
                // USERS
                var usersNode = new FtpTreeItem { Name = $"Users ({users.Count + deletedUsers.Count})" };
        
                // --- Optimized: Build user role map while building group nodes ---
                var userRoleMap = new Dictionary<string, (bool isSiteOp, bool isGroupAdmin)>(StringComparer.OrdinalIgnoreCase);
        
                var groupsNode = new FtpTreeItem { Name = $"Groups ({groups.Count})" };
                foreach (var group in groups.OrderBy(g => g.Group, StringComparer.OrdinalIgnoreCase))
                {
                    string label = string.IsNullOrWhiteSpace(group.Description)
                        ? $"{group.Group} ({group.UserCount})"
                        : $"{group.Group} - {group.Description} ({group.UserCount})";
        
                    var groupNode = new FtpTreeItem
                    {
                        Name = label,
                        Group = group
                    };
        
                    var groupUsers = await _ftp.GetUsersInGroup(_ftpClient, group.Group);
                    foreach (var (userName, isSiteOp, isGroupAdmin) in groupUsers.OrderBy(u => u.Username))
                    {
                        groupNode.Children.Add(new FtpTreeItem
                        {
                            Name = (isSiteOp ? "*" : isGroupAdmin ? "+" : "") + userName,
                            User = new GlFtpdClient.FtpUser { Username = userName, Group = group.Group },
                            IsSiteOp = isSiteOp,
                            IsGroupAdmin = isGroupAdmin
                        });
        
                        // Aggregate roles for user
                        if (!userRoleMap.TryGetValue(userName, out var flags))
                            userRoleMap[userName] = (isSiteOp, isGroupAdmin);
                        else
                            userRoleMap[userName] = (flags.isSiteOp || isSiteOp, flags.isGroupAdmin || isGroupAdmin);
                    }
                    groupsNode.Children.Add(groupNode);
                }
                root.Children.Add(usersNode);
        
                // Now, for active users:
                var activeUsersNode = new FtpTreeItem { Name = $"Active Users ({users.Count})" };
                foreach (var user in users.OrderBy(u => u.Username))
                {
                    var (isSiteOp, isGroupAdmin) = userRoleMap.TryGetValue(user.Username, out var roles) ? roles : (false, false);
                    activeUsersNode.Children.Add(new FtpTreeItem
                    {
                        Name = (isSiteOp ? "*" : isGroupAdmin ? "+" : "") + user.Username,
                        User = user,
                        IsSiteOp = isSiteOp,
                        IsGroupAdmin = isGroupAdmin
                    });
                }
                usersNode.Children.Add(activeUsersNode);
        
                var deletedUsersNode = new FtpTreeItem { Name = $"Deleted Users ({deletedUsers.Count})" };
                foreach (var user in deletedUsers.OrderBy(u => u.Username))
                {
                    deletedUsersNode.Children.Add(new FtpTreeItem
                    {
                        Name = user.Username,
                        User = user,
                        IsDeletedUser = true
                    });
                }
                usersNode.Children.Add(deletedUsersNode);
                root.Children.Add(groupsNode);
        
                // INCREMENTAL UPDATE:
                if (RootItems.Count == 0)
                {
                    RootItems.Add(root);
                    // Expand all nodes only on first load
                    foreach (var item in RootItems)
                        SetExpandedRecursive(item, true);
                }
                else
                {
                    // Save expanded state before update
                    expandedKeys.Clear();
                    SaveExpandedNodes(RootItems);
        
                    // Update the tree while preserving structure
                    UpdateTree(RootItems, [root]);
        
                    // Restore expanded state after update
                    RestoreExpandedNodes(RootItems);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Error in LoadFtpData: {ex}");
            
                // Try to reconnect if not already trying
                if (!_isReconnecting && _ftp != null)
                {
                    _isReconnecting = true;
                    bool success = await TryReconnectAsync();
                    _isReconnecting = false;
            
                    if (success)
                    {
                        Debug.WriteLine("[MainWindow] Reconnected! Reloading TreeView...");
                        await Dispatcher.InvokeAsync(() => LoadFtpData());
                        return;
                    }
                    else
                    {
                        MessageBox.Show(
                            "Connection lost and automatic reconnect failed.\n" +
                            "Please check your network/server and click 'Reconnect' to try again.",
                            "Connection Error",
                            MessageBoxButton.OK, MessageBoxImage.Error
                        );
                        // Optionally disable the UI, or show a reconnect button.
                    }
                }
                else
                {
                    MessageBox.Show($"Error loading FTP data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _ftp?.ConnectionLock.Release();
                _isLoading = false;
            }
        }

        private void FtpTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_popupOpen) return;
            try
            {
                _popupOpen = true;

                if (e.NewValue is FtpTreeItem item)
                {
                    if (item.IsUser || item.IsDeletedUser)
                    {
                        var userInfoView = new Views.UserInfoView(_ftp!, _ftpClient!, item.User!.Username, GetCurrentLoggedInUsername());
                        userInfoView.GroupChanged += () => LoadFtpData();
                        userInfoView.UserDeleted += () => LoadFtpData();
                        userInfoView.RequestClose += () => UserGroupInfoContent.Content = null;
                        userInfoView.UserChanged += (username) => LoadFtpDataAndReselectUser(username);

                        UserGroupInfoContent.Content = userInfoView;
                        RightTabControl.SelectedIndex = 0;
                    }
                    else if (item.IsGroup)
                    {
                        var groupInfoView = new Views.GroupInfoView(_ftp!, _ftpClient!, item.Group!.Group, GetCurrentLoggedInUsername());
                        groupInfoView.GroupChanged += () =>
                        {
                            CloseFtpClient("Group changed; reloading tree");
                            Dispatcher.InvokeAsync(async () =>
                            {
                                await Task.Delay(100); // 100ms is usually enough
                                LoadFtpData();
                            });
                        };
                        groupInfoView.RequestClose += () =>
                        {
                            UserGroupInfoContent.Content = null;
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                await System.Threading.Tasks.Task.Delay(500);
                                Dispatcher.Invoke(() =>
                                {
                                    UserGroupInfoContent.Content = null;
                                    LoadFtpData();
                                });
                            });
                        };

                        UserGroupInfoContent.Content = groupInfoView;
                        RightTabControl.SelectedIndex = 0;
                    }
                }
            }
            finally
            {
                _popupOpen = false;
            }
        }

        private async void LoadFtpDataAndReselectUser(string usernameToSelect)
        {
            await Task.Delay(200); // <-- Give UI time to update after reload
            _ = Dispatcher.InvokeAsync(() =>
            {
                var match = FindUserNodeByUsername(RootItems, usernameToSelect);
                if (match != null)
                {
                    ExpandAndSelectTreeViewItem(FtpTreeView, match);
                }
            });
        }

        private static FtpTreeItem? FindUserNodeByUsername(IEnumerable<FtpTreeItem> nodes, string username)
        {
            foreach (var node in nodes)
            {
                if (node.User?.Username.Equals(username, StringComparison.OrdinalIgnoreCase) == true)
                    return node;
        
                var childMatch = FindUserNodeByUsername(node.Children, username);
                if (childMatch != null)
                    return childMatch;
            }
            return null;
        }

        private static void ExpandAndSelectTreeViewItem(ItemsControl parent, object targetItem)
        {
            foreach (object item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                    continue;

                if (item == targetItem)
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                    return;
                }
        
                if (container.Items.Count > 0)
                {
                    container.IsExpanded = true; // Ensure children are generated
                    container.UpdateLayout(); // Important!
        
                    ExpandAndSelectTreeViewItem(container, targetItem);
                }
            }
        }

        private string GetCurrentLoggedInUsername()
        {
            return _ftp?.Username ?? string.Empty;
        }

        private void SiteManagerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                CloseFtpClient("Settings updated; resetting connection");
                PopulateConnectMenu();
            }
        }

        private void ConnectionTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - connectionStartTime;
            this.Title = $"{baseTitle} for {elapsed:hh\\:mm\\:ss}";
        }

        private void DisconnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_ftpClient != null && _ftpClient.IsConnected)
                {
                    CloseFtpClient("User clicked Disconnect");

                    RootItems.Clear();
                    UserGroupInfoContent.Content = null;
                    CommandOutputTextBox.Clear();

                    connectionTimer?.Stop();
                    baseTitle = "glFTPd Commander {version} by Teqno - Not connected";
                    this.Title = baseTitle;
                    DisconnectMenuItem.IsEnabled = false;
                    UsersGroupsMenuItem.Visibility = Visibility.Collapsed;
                    OnPropertyChanged(nameof(IsConnected));
                }
                else
                {
                    MessageBox.Show("Not currently connected to any server", "Disconnect",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error disconnecting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            aboutWindow.ShowDialog();
        }

        private void UserGuideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            helpWindow.ShowDialog();
        }

       private void AddUserMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var addUserWindow = new AddUserWindow(_ftp!, _ftpClient!)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (addUserWindow.ShowDialog() == true && addUserWindow.AddSuccessful)
            {
                LoadFtpData();
            }
        }
        
        private void AddGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var addGroupWindow = new AddGroupWindow(_ftp!, _ftpClient!)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (addGroupWindow.ShowDialog() == true && addGroupWindow.AddSuccessful)
            {
                LoadFtpData();
            }
        }

        private static void UpdateTree(ObservableCollection<FtpTreeItem> target, List<FtpTreeItem> source)
        {
            // Remove items not in source
            for (int i = target.Count - 1; i >= 0; i--)
            {
                var t = target[i];
                if (!source.Any(s => s.UniqueKey == t.UniqueKey))
                    target.RemoveAt(i);
            }
        
            // Add/update items
            for (int i = 0; i < source.Count; i++)
            {
                var s = source[i];
                var existing = target.FirstOrDefault(t => t.UniqueKey == s.UniqueKey);
                if (existing == null)
                {
                    // New node
                    target.Insert(i, s);
                }
                else
                {
                    // Update display properties (Name, etc.)
                    existing.Name = s.Name;
                    existing.IsGroupAdmin = s.IsGroupAdmin;
                    existing.IsSiteOp = s.IsSiteOp;
                    existing.IsDeletedUser = s.IsDeletedUser;
                    existing.User = s.User;
                    existing.Group = s.Group;
                    // Recursive update on children
                    UpdateTree(existing.Children, [.. s.Children]);
                    // Move to correct position if needed
                    if (target.IndexOf(existing) != i)
                    {
                        target.Move(target.IndexOf(existing), i);
                    }
                }
            }
        }
        
        private void SaveExpandedNodes(IEnumerable<FtpTreeItem> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsExpanded)
                    expandedKeys.Add(node.UniqueKey);
                SaveExpandedNodes(node.Children);
            }
        }
        
        private void RestoreExpandedNodes(IEnumerable<FtpTreeItem> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = expandedKeys.Contains(node.UniqueKey);
                RestoreExpandedNodes(node.Children);
            }
        }

        private void ExpandAllTextBlock_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in RootItems)
                SetExpandedRecursive(item, true);
        }
        
        private void CollapseAllTextBlock_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in RootItems)
                SetExpandedRecursive(item, false);
        }
        
        private static void SetExpandedRecursive(FtpTreeItem node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
                SetExpandedRecursive(child, expanded);
        }

        private async void CommandInputComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteCustomCommandAsync();
                e.Handled = true;
            }
        }
        
        private async Task ExecuteCustomCommandAsync()
        {
            string command = CommandInputComboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(command)) return;
        
            // Store in history if unique
            if (!_commandHistory.Contains(command))
            {
                _commandHistory.Insert(0, command);
                if (_commandHistory.Count > 10)
                    _commandHistory.RemoveAt(0);
        
                CommandInputComboBox.ItemsSource = null;
                CommandInputComboBox.ItemsSource = _commandHistory;
            }
        
            CommandInputComboBox.Text = string.Empty;
        
            if (_ftp == null)
            {
                MessageBox.Show("FTP connection is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            CommandOutputTextBox.AppendText($"> {command}\n");
        
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                _ftpClient = updatedClient;
        
                if (_ftpClient == null)
                {
                    MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
        
                CommandOutputTextBox.AppendText(result + "\n");
            }
            finally
            {
                _ftp.ConnectionLock.Release();
                CommandInputComboBox.Text = string.Empty;
                CommandOutputTextBox.ScrollToEnd();
            }
        }

        public void PopulateConnectMenu()
        {
            ConnectMenuItem.Items.Clear();
        
            var connections = SettingsManager.GetFtpConnections();
            if (connections.Count == 0)
            {
                var none = new MenuItem
                {
                    Header = "(No connections found)",
                    IsEnabled = false,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                ConnectMenuItem.Items.Add(none);
                return;
            }
        
            foreach (var conn in connections)
            {
                var item = new MenuItem
                {
                    Header = conn.Name,
                    Tag = conn,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                item.Click += ConnectToSelectedConnection_Click;
                ConnectMenuItem.Items.Add(item);
            }
        }


        private void ConnectToSelectedConnection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is FtpConnection conn)
            {
                AttemptConnection(conn);
            }
        }

        private async void AttemptConnection(FtpConnection conn)
        {
            try
            {
                UserGroupInfoContent.Content = null;

                CloseFtpClient("Opening new connection");
                GlFtpdClient.ClearSessionCaches();
        
                // Validate connection fields (null, empty, or whitespace)
                if (string.IsNullOrWhiteSpace(conn.Host) ||
                    string.IsNullOrWhiteSpace(conn.Port) ||
                    string.IsNullOrWhiteSpace(conn.Username) ||
                    string.IsNullOrWhiteSpace(conn.Password))
                {
                    MessageBox.Show("Selected connection has incomplete settings", "Connection Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
        
                // Build FTP object from connection values
                _ftp = new GlFtpdClient
                {
                    Host = conn.Host,
                    Port = conn.Port,
                    Username = conn.Username,
                    Password = conn.Password,
                    SslMode = conn.SslMode ?? "Explicit",
                    PassiveMode = (conn.Mode ?? "PASV").Equals("PASV", StringComparison.OrdinalIgnoreCase)
                };
        
                _ftpClient = await FtpBase.EnsureConnectedAsync(_ftpClient, _ftp);
                if (_ftpClient == null)
                {
                    MessageBox.Show("Failed to connect to FTP server.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
        
                LoadFtpData();
        
                baseTitle = $"glFTPd Commander v{Version} by {Author} - Connected to {conn.Name}";
                this.Title = baseTitle;
                DisconnectMenuItem.IsEnabled = true;
                UsersGroupsMenuItem.Visibility = Visibility.Visible;
                _currentConnectionEncryptedName = conn.Name;
                OnPropertyChanged(nameof(IsConnected));
                connectionStartTime = DateTime.Now;
        
                connectionTimer ??= new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                connectionTimer.Tick += ConnectionTimer_Tick;
                connectionTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task<bool> TryReconnectAsync()
        {
            if (_ftp == null)
                return false;

            while (_reconnectAttempts < MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                try
                {
                    _ftpClient = await FtpBase.EnsureConnectedAsync(_ftpClient, _ftp);
                    if (_ftpClient != null && _ftpClient.IsConnected)
                    {
                        Debug.WriteLine($"[Reconnect] Successful after {_reconnectAttempts} attempts");
                        _reconnectAttempts = 0;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Reconnect] Attempt {_reconnectAttempts} failed: {ex.Message}");
                }
                await Task.Delay(2000 * _reconnectAttempts); // Exponential backoff: 2s, 4s, 6s, ...
            }
            return false;
        }

        public void ForceDisconnect(string reason)
        {
            CloseFtpClient(reason);
            _ftp = null;
            RootItems.Clear();
            UserGroupInfoContent.Content = null;
        
            connectionTimer?.Stop();
            this.Title = $"glFTPd Commander v{Version} by {Author} - Not connected";
            DisconnectMenuItem.IsEnabled = false;
            UsersGroupsMenuItem.Visibility = Visibility.Collapsed;
            _currentConnectionEncryptedName = null;
            OnPropertyChanged(nameof(IsConnected));
        
            MessageBox.Show(reason, "Disconnected", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void OnCustomCommandSlotClicked(CustomCommandSlot slot)
        {
            if (!slot.IsConfigured)
            {
                var dlg = new glFTPd_Commander.Windows.CustomCommandConfigWindow { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    slot.Command = dlg.SiteCommand;
                    slot.ButtonText = string.IsNullOrWhiteSpace(dlg.CustomLabel) ? dlg.SiteCommand : dlg.CustomLabel;
                    Debug.WriteLine($"[CustomCmd] Configured slot with: {slot.Command}");
                    SettingsManager.SetCustomCommandSlots([.. CustomCommandSlots]);
                }
            }
            else
            {
                if (_ftp == null)
                {
                    MessageBox.Show("You must be connected to an FTP server.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(slot.Command))
                {
                    MessageBox.Show("No command configured for this slot.", "Command Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Debug.WriteLine($"[CustomCmd] Executing: {slot.Command}");
                CommandOutputTextBox.AppendText($"> {slot.Command}\n");
                await _ftp.ConnectionLock.WaitAsync();
                try
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(slot.Command, _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (_ftpClient == null)
                    {
                        MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    CommandOutputTextBox.AppendText(result + "\n");
                }
                catch (Exception ex)
                {
                    CommandOutputTextBox.AppendText($"[ERROR] {ex.Message}\n");
                    Debug.WriteLine($"[CustomCmd] Error: {ex}");
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                    CommandOutputTextBox.ScrollToEnd();
                }
            }
        }
        
        private void OnRemoveCustomCommand(CustomCommandSlot slot)
        {
            slot.Command = null;
            slot.ButtonText = "Configure Button";
            Debug.WriteLine($"[CustomCmd] Removed configuration from slot.");
            SettingsManager.SetCustomCommandSlots([.. CustomCommandSlots]);
        }

        private void CloseFtpClient(string? reason = null)
        {
            if (_ftpClient == null) return;
        
            try { _ftpClient.Disconnect(); }
            catch (Exception ex) { Debug.WriteLine($"[FTP] Disconnect failed: {ex.Message}"); }
        
            try { _ftpClient.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[FTP] Dispose failed: {ex.Message}"); }
        
            _ftpClient = null;
        
            if (!string.IsNullOrWhiteSpace(reason))
                Debug.WriteLine($"[FTP] Client closed. Reason: {reason}");
        }



    }
}
