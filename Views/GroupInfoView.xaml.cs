using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
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
        private readonly GlFtpdClient? _ftp;
        private FtpClient? _ftpClient;
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
        private static readonly string[] LineSplitDelimiters = ["\r\n", "\n"];

        public GroupInfoView(GlFtpdClient ftp, FtpClient ftpClient, string group, string currentUser)
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
                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                    if (_ftpClient == null) return;
                
                GroupNameTextBox.Text = _group;
        
                (var siteGrpTask, _ftpClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GRP {_group}", _ftpClient, _ftp!);
                (var siteGroupsTask, _ftpClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync("SITE GROUPS", _ftpClient, _ftp!);
                (var siteGinfoTask, _ftpClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GINFO {_group}", _ftpClient, _ftp!);
        
                _originalDescription = ParseGroupDescription(siteGroupsTask, _group);
                DescriptionTextBox.Text = _originalDescription;

                _originalInfo = ParseGroupInfo(siteGrpTask);
                UpdateUiFromGroupInfo(_originalInfo);

                await LoadUsersAndAdmins(siteGinfoTask);
        
                if (GroupAdminComboBox.ItemsSource is IList<string> admins && admins.Count > 0)
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

            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                _ftpClient = updatedClient;
                if (result.Contains("Error"))
                {
                    MessageBox.Show($"Error applying change: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    control.Text = oldValue;
                    return;
                }

                updateOld(newValue);
                await ReloadGroupDetails();
                GroupChanged?.Invoke();

                if (!_ftpClient!.IsConnected)
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
            if (_oldGroupName == newVal) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a group name.", GroupNameTextBox))
            {
                GroupNameTextBox.Text = _oldGroupName;
                GroupNameTextBox.Focus();
                GroupNameTextBox.SelectAll();
                return;
            }
            
            // Prevent renaming to an existing group
            if (await ExistenceChecks.GroupExistsAsync(_ftp!, _ftpClient, newVal))
            {
                MessageBox.Show("A group with this name already exists.", "Duplicate Group", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                GroupNameTextBox.Text = _oldGroupName;
                GroupNameTextBox.Focus();
                GroupNameTextBox.SelectAll();
                Debug.WriteLine($"[GroupInfoView] Prevented renaming group '{_group}' to existing group '{newVal}'");
                return;
            }
            
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
            if (_ftpClient == null) return;
            
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GRPREN {_group} {newVal}", _ftpClient, _ftp);
                _ftpClient = updatedClient;
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
        {
            string newVal = DescriptionTextBox.Text.Trim();
            if (_oldDescription == newVal) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a description.", DescriptionTextBox))
            {
                DescriptionTextBox.Text = _oldDescription;
                DescriptionTextBox.Focus();
                DescriptionTextBox.SelectAll();
                return;
            }
        
            await ApplyGroupChange($"SITE GRPNFO {_group} {newVal}", _oldDescription!, v => _oldDescription = v, DescriptionTextBox);
        }

        private async void SlotsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = SlotsTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                    string.IsNullOrWhiteSpace(newVal) ||
                    (!(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) || (int.TryParse(newVal, out int val) && val >= -1))),
                    "Slots must be an integer not less than -1, -1 = Unlimited.", SlotsTextBox))
            {
                SlotsTextBox.Text = _oldSlots;
                return;
            }
            await ApplyGroupChange($"SITE GRPCHANGE {_group} slots {newVal}", _oldSlots!, v => _oldSlots = v, SlotsTextBox);
        }
        
        private async void LeechTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = LeechTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  newVal.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= -2)),
                "Leech must be an integer not less than -2\n\n -1 = Unlimited, -2 = Disabled.", LeechTextBox))
            {
                LeechTextBox.Text = _oldLeech;
                return;
            }
            await ApplyGroupChange($"SITE GRPCHANGE {_group} leech_slots {newVal}", _oldLeech!, v => _oldLeech = v, LeechTextBox);
        }
        
        private async void AllotTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = AllotTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  newVal.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= -2)),
                "Allot must be an integer not less than -2\n\n -1 = Unlimited, -2 = Disabled.", AllotTextBox))
            {
                AllotTextBox.Text = _oldAllot;
                return;
            }
            await ApplyGroupChange($"SITE GRPCHANGE {_group} allot_slots {newVal}", _oldAllot!, v => _oldAllot = v, AllotTextBox);
        }
        
        private async void MaxLoginsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = MaxLoginsTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= 0)),
                "Max Logins must be an integer not less than 0, 0 = Unlimited.", MaxLoginsTextBox))
            {
                MaxLoginsTextBox.Text = _oldMaxLogins;
                return;
            }
            await ApplyGroupChange($"SITE GRPCHANGE {_group} max_logins {newVal}", _oldMaxLogins!, v => _oldMaxLogins = v, MaxLoginsTextBox);
        }

        private async void CommentTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = CommentTextBox.Text.Trim();
            if (_oldComment == newVal) return;
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a comment.", CommentTextBox))
            {
                CommentTextBox.Text = _oldComment;
                CommentTextBox.Focus();
                CommentTextBox.SelectAll();
                return;
            }
        
            await ApplyGroupChange($"SITE GRPCHANGE {_group} comment {newVal}", _oldComment!, v => _oldComment = v, CommentTextBox);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;
        
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                foreach (var user in AvailableUsersComboBox.SelectedItems.Cast<string>().ToList())
                {
                    string cleanUser = user.TrimStart('*', '+');
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGADMIN {cleanUser} {_group}", _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error adding user to group: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }
                }
        
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
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;
        
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                foreach (var user in GroupAdminComboBox.SelectedItems.Cast<string>().ToList())
                {
                    string cleanUser = user.TrimStart('*', '+');
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGADMIN {cleanUser} {_group}", _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error removing user to group: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }
                }
        
                await ReloadGroupDetails();
                GroupChanged?.Invoke();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async Task LoadUsersAndAdmins(string ginfoResponse)
        {
            try
            {
                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                    if (_ftpClient == null) return;
                // Get all users (raw) in the group from GINFO
                var allUsers = ParseAllUsersFromGinfo(ginfoResponse);
        
                var groupAdmins = new List<string>();
                var availableUsers = new List<string>();
        
                foreach (var user in allUsers)
                {
                    var (userDetails, updatedClient) = await FtpBase.ExecuteWithConnectionAsync(_ftpClient, _ftp!, c => Task.Run(() => _ftp!.GetUserDetails(user, c)));
                    _ftpClient = updatedClient;
        
                    // userDetails.Groups: List<string> like [*Group1, +Group2, Group3]
                    bool isAdmin = userDetails?.Groups?.Any(g =>
                        g.Equals($"*{_group}", StringComparison.OrdinalIgnoreCase) ||
                        g.Equals($"+{_group}", StringComparison.OrdinalIgnoreCase)
                    ) == true;
        
                    if (isAdmin)
                        groupAdmins.Add(user);
                    else
                        availableUsers.Add(user);
                }
        
                AvailableUsersComboBox.ItemsSource = availableUsers.OrderBy(u => u).ToList();
                GroupAdminComboBox.ItemsSource = groupAdmins.OrderBy(u => u).ToList();
      
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading users: {ex.Message}", "Error");
            }
        }

        private static GroupInfo ParseGroupInfo(string response)
        {
            var info = new GroupInfo();

            var groupCommentMatch = GroupCommentRegex().Match(response);
            if (groupCommentMatch.Success)
                info.GroupComment = groupCommentMatch.Groups[1].Value.Trim();

            var slotsMatch = SlotsLeftRegex().Match(response);
            if (slotsMatch.Success)
                info.SlotsLeft = slotsMatch.Groups[1].Value.Trim();

            var leechMatch = LeechSlotsLeftRegex().Match(response);
            if (leechMatch.Success)
                info.LeechSlotsLeft = leechMatch.Groups[1].Value.Trim();

            var allotMatch = AllotmentSlotsLeftRegex().Match(response);
            if (allotMatch.Success)
                info.AllotmentSlotsLeft = allotMatch.Groups[1].Value.Trim();

            var maxAllotMatch = MaxAllotmentSizeRegex().Match(response);
            if (maxAllotMatch.Success)
                info.MaxAllotmentSize = maxAllotMatch.Groups[1].Value.Trim();

            var maxLoginsMatch = MaxSimultaneousLoginsRegex().Match(response);
            if (maxLoginsMatch.Success)
                info.MaxSimultaneousLogins = maxLoginsMatch.Groups[1].Value.Trim();

            return info;
        }

        private static string ParseGroupDescription(string response, string targetGroup)
        {
            var lines = response.Split(LineSplitDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(")  "));

            foreach (var line in lines)
            {
                var match = GroupDescriptionRegex().Match(line);
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

        private static string FormatSize(string raw)
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


        private static (List<string> allUsers, List<string> admins) ParseAllUserDataFromGinfo(string response)
        {
            var allUsers = new List<string>();
            var admins = new List<string>();
                    
            var lines = response.Split(LineSplitDelimiters, StringSplitOptions.RemoveEmptyEntries);

        
            foreach (var line in lines)
            {
                if (!line.StartsWith("200- |") || line.Contains("Username") || line.Contains("--------") ||
                    line.Contains("* denotes") || line.Contains("Tot ") || line.Contains("Total Free"))
                    continue;
        
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
        
                var usernameRaw = parts[1].Trim();
        
                if (string.IsNullOrWhiteSpace(usernameRaw))
                    continue;
        
                // Always get clean name (no prefix)
                var cleanName = usernameRaw.TrimStart('*', '+').Trim();
        
                allUsers.Add(cleanName);
        
                // Add to admins if it has * or +
                if (usernameRaw.StartsWith('+') || usernameRaw.StartsWith('*'))
                    admins.Add(cleanName);
            }
        
            return (allUsers.Distinct().ToList(), admins.Distinct().ToList());
        }

        private static List<string> ParseAllUsersFromGinfo(string response)
        {
            var users = new List<string>();
        
            var lines = response.Split(LineSplitDelimiters, StringSplitOptions.RemoveEmptyEntries);
        
            foreach (var line in lines)
            {
                if (!line.StartsWith("200- |") || line.Contains("Username") || line.Contains("--------") ||
                    line.Contains("* denotes") || line.Contains("Tot ") || line.Contains("Total Free"))
                    continue;
        
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
        
                var usernameRaw = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(usernameRaw)) continue;
        
                var cleanName = usernameRaw.TrimStart('*', '+').Trim();
                users.Add(cleanName);
            }
        
            return [.. users.Distinct()];
        }

        private async void GroupRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            var confirm = MessageBox.Show(
                $"Are you sure you want to remove group '{_group}' and unassign all its users first?",
                "Confirm Group Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
        
            if (confirm != MessageBoxResult.Yes)
                return;

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                // Step 1: Initial GINFO and parse
                var (ginfoInitial, clientAfterGinfo1) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GINFO {_group}", _ftpClient, _ftp!);
                _ftpClient = clientAfterGinfo1;
                var (allUsers, admins) = ParseAllUserDataFromGinfo(ginfoInitial);
        
                // Step 2: Demote group admins
                foreach (var admin in admins)
                {
                    var (chgadmin, clientAfterChgAdmin) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGADMIN {admin} {_group}", _ftpClient, _ftp!);
                    _ftpClient = clientAfterChgAdmin;
                    if (chgadmin.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Error removing admin {admin}: {chgadmin}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
        
                // Step 3: Re-fetch GINFO after admin removal
                var (ginfoClean, clientAfterGinfo2) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GINFO {_group}", _ftpClient, _ftp!);
                _ftpClient = clientAfterGinfo2;
                var (cleanUsers, _) = ParseAllUserDataFromGinfo(ginfoClean);
        
                // Step 4: Remove users from group
                foreach (var username in cleanUsers)
                {
                    var (chgrp, clientAfterChgrp) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGRP {username} {_group}", _ftpClient, _ftp!);
                    _ftpClient = clientAfterChgrp;
                    if (chgrp.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Error removing {username} from group: {chgrp}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
        
                // Step 5: Remove the group
                var (result, clientAfterDel) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE GRPDEL {_group}", _ftpClient, _ftp!);
                _ftpClient = clientAfterDel;
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

        private async void SetMaxAllotButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SetMaxAllotWindow.ShowAndSetMaxAllot(Window.GetWindow(this), _ftp!, _ftpClient, _group))
            {
                await ReloadGroupDetails();
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

        [GeneratedRegex(@"Group Comment:\s*(.*?)\r?\n")]
        private static partial Regex GroupCommentRegex();

        [GeneratedRegex(@"Number of slots left:\s*(.*?)\s*\(")]
        private static partial Regex SlotsLeftRegex();

        [GeneratedRegex(@"Number of leech slots left:\s*(.*?)\s*\(")]
        private static partial Regex LeechSlotsLeftRegex();

        [GeneratedRegex(@"Number of allotment slots left:\s*(.*?)\s*\(")]
        private static partial Regex AllotmentSlotsLeftRegex();

        [GeneratedRegex(@"Max\. allotment size:\s*(.*?)\s*\(")]
        private static partial Regex MaxAllotmentSizeRegex();

        [GeneratedRegex(@"Max simultaneous logins:\s*(.*?)\s*\(")]
        private static partial Regex MaxSimultaneousLoginsRegex();

        [GeneratedRegex(@"\(.*?\)\s+([^\s]+)\s+(.*)$")]
        private static partial Regex GroupDescriptionRegex();


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
