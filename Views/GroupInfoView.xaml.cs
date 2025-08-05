using FluentFTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Debug = System.Diagnostics.Debug;

namespace glFTPd_Commander.Views
{
    public partial class GroupInfoView : BaseUserControl, IUnselectable
    {
        private FTP _ftp;
        private FtpClient _ftpClient;
        private string _group;
        private readonly string _currentUser;
        private GroupInfo _originalInfo;
        private string _originalGroupAdmin = string.Empty;
        private string _originalDescription = string.Empty;
        private string? _oldGroupName, _oldDescription, _oldSlots, _oldLeech, _oldAllot, _oldMaxLogins, _oldComment;

        public event Action? GroupChanged;
        public Action? RequestClose;
        public bool UnselectGroupOnClose { get; private set; } = false;
        public bool UnselectOnEsc => UnselectGroupOnClose;

        public GroupInfoView(FTP ftp, FtpClient ftpClient, string group, string currentUser)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _group = group;
            _currentUser = currentUser;
            _originalInfo = new GroupInfo();

            Loaded += GroupInfoView_Loaded;
            Loaded += (s, e) => GroupNameTextBox.Focus();
        }

        private async void GroupInfoView_Loaded(object sender, RoutedEventArgs e)
        {
            await ReloadGroupDetails();
        }

        private async Task ReloadGroupDetails()
        {
            try
            {
                // Synchronous connection check:
                if (!FTP.EnsureConnected(ref _ftpClient, _ftp))
                {
                    MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
        
                GroupNameTextBox.Text = _group;
        
                var siteGrpTask = Task.Run(() => _ftp.ExecuteCommand($"SITE GRP {_group}", _ftpClient));
                var siteGroupsTask = Task.Run(() => _ftp.ExecuteCommand("SITE GROUPS", _ftpClient));
                var siteGinfoTask = Task.Run(() => _ftp.ExecuteCommand($"SITE GINFO {_group}", _ftpClient));
        
                await Task.WhenAll(siteGrpTask, siteGroupsTask, siteGinfoTask);
        
                _originalDescription = ParseGroupDescription(siteGroupsTask.Result, _group);
                DescriptionTextBox.Text = _originalDescription;
        
                _originalInfo = ParseGroupInfo(siteGrpTask.Result);
                UpdateUiFromGroupInfo(_originalInfo);
        
                LoadUsersAndAdmins(siteGinfoTask.Result);
        
                var admins = GroupAdminComboBox.ItemsSource as IList<string>;
                if (admins != null && admins.Count > 0)
                {
                    _originalGroupAdmin = admins[0];
                }
                else
                {
                    _originalGroupAdmin = string.Empty;
                }
        
                _oldGroupName = GroupNameTextBox.Text;
                _oldDescription = DescriptionTextBox.Text;
                _oldSlots = SlotsTextBox.Text;
                _oldLeech = LeechTextBox.Text;
                _oldAllot = AllotTextBox.Text;
                _oldMaxLogins = MaxLoginsTextBox.Text;
                _oldComment = CommentTextBox.Text;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading group information: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApplyGroupChange(string command, string oldValue, Action<string> updateOld, TextBox control)
        {
            string newValue = control.Text.Trim();
            if (newValue == oldValue?.Trim()) return;  // ← prevent redundant command execution

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
                string result = await Task.Run(() => _ftp.ExecuteCommand(command, _ftpClient));
                if (result.Contains("Error"))
                {
                    MessageBox.Show($"Error applying change: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    control.Text = oldValue;
                    return;
                }

                updateOld(newValue);
                await ReloadGroupDetails();
                GroupChanged?.Invoke();

                if (!_ftpClient.IsConnected)
                {
                    MessageBox.Show("The connection to the FTP server was lost.", "Disconnected", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Optional: trigger UI disconnect flow or reconnect logic here.
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception applying change: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                control.Text = oldValue;
            }
            finally { _ftp.ConnectionLock.Release(); }
        }

        private async void GroupNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = GroupNameTextBox.Text.Trim();
            if (_oldGroupName == newVal || string.IsNullOrWhiteSpace(newVal)) return;
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE GRPREN {_group} {newVal}", _ftpClient));
                if (result.Contains("Error"))
                    throw new Exception(result);

                _oldGroupName = newVal;
                _group = newVal;
                UnselectGroupOnClose = true;
                GroupChanged?.Invoke();
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error renaming group: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                GroupNameTextBox.Text = _oldGroupName;
            }
            finally { _ftp.ConnectionLock.Release(); }
        }

        private async void DescriptionTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPNFO {_group} {DescriptionTextBox.Text.Trim()}", _oldDescription!, v => _oldDescription = v, DescriptionTextBox);

        private async void SlotsTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPCHANGE {_group} slots {SlotsTextBox.Text.Trim()}", _oldSlots!, v => _oldSlots = v, SlotsTextBox);

        private async void LeechTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPCHANGE {_group} leech_slots {LeechTextBox.Text.Trim()}", _oldLeech!, v => _oldLeech = v, LeechTextBox);

        private async void AllotTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPCHANGE {_group} allot_slots {AllotTextBox.Text.Trim()}", _oldAllot!, v => _oldAllot = v, AllotTextBox);

        private async void MaxLoginsTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPCHANGE {_group} max_logins {MaxLoginsTextBox.Text.Trim()}", _oldMaxLogins!, v => _oldMaxLogins = v, MaxLoginsTextBox);

        private async void CommentTextBox_LostFocus(object sender, RoutedEventArgs e)
            => await ApplyGroupChange($"SITE GRPCHANGE {_group} comment {CommentTextBox.Text.Trim()}", _oldComment!, v => _oldComment = v, CommentTextBox);

        private async void AddButton_Click(object sender, RoutedEventArgs e)
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
                foreach (var user in AvailableUsersComboBox.SelectedItems.Cast<string>().ToList())
                {
                    string cleanUser = user.TrimStart('*', '+');
                    string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGADMIN {cleanUser} {_group}", _ftpClient));
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error adding user to group: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }
                }
        
                // After all changes, just reload from server (no Remove/Add needed):
                await ReloadGroupDetails();
                GroupChanged?.Invoke();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void RemoveButton_Click(object sender, RoutedEventArgs e)
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
                foreach (var user in GroupAdminComboBox.SelectedItems.Cast<string>().ToList())
                {
                    string cleanUser = user.TrimStart('*', '+');
                    string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGADMIN {cleanUser} {_group}", _ftpClient));
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error removing user from group: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }
                }
        
                // Just reload lists after all operations, like UserInfoView does:
                await ReloadGroupDetails();
                GroupChanged?.Invoke();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void LoadUsersAndAdmins(string ginfoResponse)
        {
            try
            {
                var (allUsers, admins) = ParseAllUserDataFromGinfo(ginfoResponse);
        
                // Assign sorted lists directly to ItemsSource, no in-memory fields
                AvailableUsersComboBox.ItemsSource = allUsers
                    .Except(admins)
                    .OrderBy(u => u.TrimStart('*', '+'), StringComparer.OrdinalIgnoreCase)
                    .ToList();
        
                GroupAdminComboBox.ItemsSource = admins
                    .OrderBy(u => u.TrimStart('*', '+'), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading users: {ex.Message}", "Error");
            }
        }


        private GroupInfo ParseGroupInfo(string response)
        {
            var info = new GroupInfo();

            var groupCommentMatch = Regex.Match(response, @"Group Comment:\s*(.*?)\r?\n");
            if (groupCommentMatch.Success)
                info.GroupComment = groupCommentMatch.Groups[1].Value.Trim();

            var slotsMatch = Regex.Match(response, @"Number of slots left:\s*(.*?)\s*\(");
            if (slotsMatch.Success)
                info.SlotsLeft = slotsMatch.Groups[1].Value.Trim();

            var leechMatch = Regex.Match(response, @"Number of leech slots left:\s*(.*?)\s*\(");
            if (leechMatch.Success)
                info.LeechSlotsLeft = leechMatch.Groups[1].Value.Trim();

            var allotMatch = Regex.Match(response, @"Number of allotment slots left:\s*(.*?)\s*\(");
            if (allotMatch.Success)
                info.AllotmentSlotsLeft = allotMatch.Groups[1].Value.Trim();

            var maxAllotMatch = Regex.Match(response, @"Max\. allotment size:\s*(.*?)\s*\(");
            if (maxAllotMatch.Success)
                info.MaxAllotmentSize = maxAllotMatch.Groups[1].Value.Trim();

            var maxLoginsMatch = Regex.Match(response, @"Max simultaneous logins:\s*(.*?)\s*\(");
            if (maxLoginsMatch.Success)
                info.MaxSimultaneousLogins = maxLoginsMatch.Groups[1].Value.Trim();

            return info;
        }

        private string ParseGroupDescription(string response, string targetGroup)
        {
            var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(")  "));

            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"\(.*?\)\s+([^\s]+)\s+(.*)$");
                if (match.Success && match.Groups[1].Value.Equals(targetGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Groups[2].Value.Trim();
                }
            }

            return string.Empty;
        }

        private void UpdateUiFromGroupInfo(GroupInfo info)
        {
            SlotsTextBox.Text = info.SlotsLeft;
            LeechTextBox.Text = info.LeechSlotsLeft;
            CommentTextBox.Text = info.GroupComment;
            AllotTextBox.Text = info.AllotmentSlotsLeft;
            MaxAllotTextBox.Text = FormatSize(info.MaxAllotmentSize);
            MaxLoginsTextBox.Text = info.MaxSimultaneousLogins;
        }

        private string FormatSize(string raw)
        {
            if (string.Equals(raw, "Unlimited", StringComparison.OrdinalIgnoreCase))
            return raw;

            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double gib))
            {
                return raw;
            }
        
            double mib = gib * 1024;
        
            if (mib < 1024)
                return $"{Math.Round(mib)} MiB";
            else if (gib < 1024)
                return $"{Math.Round(gib, 2)} GiB";
            else
                return $"{Math.Round(gib / 1024, 2)} TiB";
        }


        private (List<string> allUsers, List<string> admins) ParseAllUserDataFromGinfo(string response)
        {
            var allUsers = new List<string>();
            var admins = new List<string>();
        
            var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
            foreach (var line in lines)
            {
                if (!line.StartsWith("200- |") || line.Contains("Username") || line.Contains("--------") ||
                    line.Contains("* denotes") || line.Contains("Tot ") || line.Contains("Total Free"))
                    continue;
        
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
        
                var username = parts[1].Trim();
        
                if (string.IsNullOrWhiteSpace(username))
                    continue;
        
                if (username.StartsWith("+"))
                {
                    var cleanName = username.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(cleanName))
                    {
                        admins.Add(cleanName);
                        allUsers.Add(cleanName);
                    }
                }
                else
                {
                    allUsers.Add(username);
                }
            }
        
            return (allUsers, admins);
        }

       private async void groupRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to remove group '{_group}' and unassign all its users first?",
                "Confirm Group Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
        
            if (confirm != MessageBoxResult.Yes)
                return;

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
                // Step 1: Initial GINFO and parse
                string ginfoInitial = await Task.Run(() => _ftp.ExecuteCommand($"SITE GINFO {_group}", _ftpClient));
                var (allUsers, admins) = ParseAllUserDataFromGinfo(ginfoInitial);
        
                // Step 2: Demote group admins
                foreach (var admin in admins)
                {
                    string chgadmin = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGADMIN {admin} {_group}", _ftpClient));
                    if (chgadmin.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Error removing admin {admin}: {chgadmin}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
        
                // Step 3: Re-fetch GINFO after admin removal
                string ginfoClean = await Task.Run(() => _ftp.ExecuteCommand($"SITE GINFO {_group}", _ftpClient));
                var (cleanUsers, _) = ParseAllUserDataFromGinfo(ginfoClean);
        
                // Step 4: Remove users from group
                foreach (var username in cleanUsers)
                {
                    string chgrp = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGRP {username} {_group}", _ftpClient));
                    if (chgrp.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Error removing {username} from group: {chgrp}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
        
                // Step 5: Remove the group
                string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE GRPDEL {_group}", _ftpClient));
                if (!result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    UnselectGroupOnClose = true;
                    GroupChanged?.Invoke();
                    RequestClose?.Invoke();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void SetMaxAllotButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new SetMaxAllotWindow(_ftp, _ftpClient, _group)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (win.ShowDialog() == true)
            {
                GroupInfoView_Loaded(null!, null!);
                GroupChanged?.Invoke();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                UnselectGroupOnClose = true;
                RequestClose?.Invoke();
                e.Handled = true;
            }
        }

        private void ValueInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOrNegative(sender, e);
        }

    }

    public class GroupInfo
    {
        public string GroupComment { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SlotsLeft { get; set; } = string.Empty;
        public string LeechSlotsLeft { get; set; } = string.Empty;
        public string AllotmentSlotsLeft { get; set; } = string.Empty;
        public string MaxAllotmentSize { get; set; } = string.Empty;
        public string MaxSimultaneousLogins { get; set; } = string.Empty;
    }
}
