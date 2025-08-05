using FluentFTP;
using glFTPd_Commander.Services;
using glFTPd_Commander.Windows;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Debug = System.Diagnostics.Debug;

namespace glFTPd_Commander.Views
{
    public partial class UserInfoView : BaseUserControl, IUnselectable
    {
        private FTP? _ftp;
        private FtpClient? _ftpClient;
        private string _username;
        private readonly string _currentUser;
        private string? _oldFlags, _oldRatios, _oldExpires, _oldIdleTime, _oldMaxLogins, _oldFromSameIp, _oldTagline, _oldUserComment, _oldMaxSimUploads, _oldMaxSimDownloads, _oldTimeLimit, _oldTimeframe;

        public event Action? GroupChanged;
        public event Action? UserDeleted;
        public event Action<string>? UserChanged;
        public Action? RequestClose { get; set; }
        public bool UnselectUserOnClose { get; private set; } = false;
        public bool UnselectOnEsc => UnselectUserOnClose;

        public UserInfoView(FTP ftp, FtpClient ftpClient, string username, string currentUser)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _currentUser = currentUser;
            Loaded += UserInfoView_Loaded;
            Loaded += (s, e) => usernameText.Focus();
        }

        private async void UserInfoView_Loaded(object sender, RoutedEventArgs e) => await LoadUserDetails();

        private async Task LoadUserDetails()
        {
            try
            {
                ResetAllFields();
                if (_ftp == null || _ftpClient == null)
                {
                    MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // Synchronous connection check:
                if (!FTP.EnsureConnected(ref _ftpClient, _ftp))
                {
                    MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var userDetails = await Task.Run(() => _ftp.GetUserDetails(_username, _ftpClient));
                if (userDetails == null) return;

                usernameText.Text = userDetails.Username;
                flagsText.Text = userDetails.Flags ?? string.Empty;

                if ((userDetails.Flags ?? "").Contains("6"))
                {
                    userPurge.Visibility = Visibility.Visible;
                    userReAdd.Visibility = Visibility.Visible;
                    userRemove.Visibility = Visibility.Collapsed;
                }
                else
                {
                    userPurge.Visibility = Visibility.Collapsed;
                    userReAdd.Visibility = Visibility.Collapsed;
                    userRemove.Visibility = Visibility.Visible;
                }

                var restrictions = userDetails.IpRestrictions
                    .Where(ip => !string.IsNullOrWhiteSpace(ip.Value))
                    .OrderBy(ip => ip.Key)
                    .ToList();
                addIpButton.IsEnabled = restrictions.Count < 10;
                ipRestrictionsList.ItemsSource = restrictions;
                noRestrictionsText.Visibility = restrictions.Any() ? Visibility.Collapsed : Visibility.Visible;

                var allGroups = await Task.Run(() => _ftp.GetGroups(_ftpClient));
                var userGroups = userDetails.Groups;

                var userGroupsNormalized = new HashSet<string>(
                userDetails.Groups.Select(g => g.TrimStart('*', '+')), StringComparer.OrdinalIgnoreCase);

                availableGroupsList.ItemsSource = allGroups
                    .Where(g => !userGroupsNormalized.Contains(g.Group.TrimStart('*', '+')))
                    .Select(g => g.Group)
                    .OrderBy(g => g)
                    .ToList();

                userGroupsList.ItemsSource = userDetails.Groups.OrderBy(g => g.TrimStart('*', '+')).ToList();

                var reply = await Task.Run(() => _ftp.ExecuteCommand($"SITE USER {_username}", _ftpClient));
                if (!string.IsNullOrEmpty(reply)) ParseDetailedUserInfo(reply);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading user details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetAllFields()
        {
            foreach (var ctrl in new TextBox[]
            {
                usernameText, userCommentText, addedByText, timeOnTodayText, flagsText, ratiosText,
                creditsText, totalLoginsText, maxLoginsText, maxSimUploadsText, maxUploadSpeedText,
                timesNukedText, weeklyAllotmentText, createdText, expiresText, lastSeenText,
                idleTimeText, currentLoginsText, fromSameIpText, maxSimDownloadsText,
                maxDownloadSpeedText, bytesNukedText, timeLimitText, timeframeText, taglineText
            }) ctrl.Text = string.Empty;

            _oldFlags = _oldRatios = _oldExpires = _oldIdleTime = _oldMaxLogins =
            _oldFromSameIp = _oldTagline = _oldUserComment = _oldMaxSimUploads =
            _oldMaxSimDownloads = _oldTimeLimit = _oldTimeframe =
            string.Empty;


            availableGroupsList.ItemsSource = null;
            userGroupsList.ItemsSource = null;
            ipRestrictionsList.ItemsSource = null;
            noRestrictionsText.Visibility = Visibility.Visible;
        }

        private void ParseDetailedUserInfo(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return;

            var fieldMap = new Dictionary<string, (TextBox, Action<string>)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Username"] = (usernameText, _ => { }),
                ["User Comment"] = (userCommentText, v => _oldUserComment = v),
                ["Added by"] = (addedByText, _ => { }),
                ["Created"] = (createdText, val =>
                {
                    if (val == "0")
                    {
                        createdText.Text = "0";
                    }
                    else if (DateTime.TryParseExact(val, "MM-dd-yy", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        createdText.Text = dt.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        createdText.Text = val; // fallback
                    }
                }),
                ["Expires"] = (expiresText, v => _oldExpires = v),
                ["Time On Today"] = (timeOnTodayText, val =>
                {
                    string result = val;
                
                    // Try HH:mm:ss first
                    if (TimeSpan.TryParseExact(val, @"hh\:mm\:ss", null, out var time))
                    {
                        result = $"{time.Hours}h {time.Minutes:D2}m {time.Seconds:D2}s";
                    }
                    // Fallback to mm:ss
                    else if (TimeSpan.TryParseExact(val, @"mm\:ss", null, out time))
                    {
                        result = $"{time.Minutes}m {time.Seconds:D2}s";
                    }
                
                    timeOnTodayText.Text = result;
                }),
                ["Last seen"] = (lastSeenText, val =>
                {
                    if (val.Equals("Never", StringComparison.OrdinalIgnoreCase))
                    {
                        lastSeenText.Text = "Never";
                    }
                    else if (DateTime.TryParseExact(val, "ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        lastSeenText.Text = dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        lastSeenText.Text = val; // fallback
                    }
                }),
                ["Flags"] = (flagsText, v => _oldFlags = v.Trim().ToUpper()),
                ["Idle time"] = (idleTimeText, v => _oldIdleTime = v),
                ["Ratios"] = (ratiosText, v => _oldRatios = ParseRatioValue(v)),
                ["Credits"] = (creditsText, _ => { }),
                ["Total Logins"] = (totalLoginsText, _ => { }),
                ["Current Logins"] = (currentLoginsText, _ => { }),
                ["Max Logins"] = (maxLoginsText, v => _oldMaxLogins = v),
                ["From same IP"] = (fromSameIpText, v => _oldFromSameIp = v),
                ["Max Sim Uploads"] = (maxSimUploadsText, v => _oldMaxSimUploads = v),
                ["Max Sim Downloads"] = (maxSimDownloadsText, v => _oldMaxSimDownloads = v),
                ["Max Upload Speed"] = (maxUploadSpeedText, v => { }),
                ["Max Download Speed"] = (maxDownloadSpeedText, v => { }),
                ["Times Nuked"] = (timesNukedText, _ => { }),
                ["Bytes Nuked"] = (bytesNukedText, v => { }),
                ["Weekly Allotment"] = (weeklyAllotmentText, v => { }),
                ["Time Limit"] = (timeLimitText, v => _oldTimeLimit = v),
                ["Timeframe"] = (timeframeText, v => _oldTimeframe = v),
                ["Tagline"] = (taglineText, v => _oldTagline = v)
            };

            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("200-")).Select(l => l[4..].Trim());

            foreach (var line in lines)
            {
                if (line.StartsWith("| User Comment:"))
                {
                    var comment = line[16..].Trim();
                    userCommentText.Text = comment;
                    _oldUserComment = comment;
                    continue;
                }
            
                string normalizedLine = Regex.Replace(line.Trim('|').Trim(), @" {2,}", "~");
            
                // Explicit handling of multi-field lines
                if (normalizedLine.Contains("Max Upload Speed") ||
                    normalizedLine.Contains("Max Download Speed") ||
                    normalizedLine.Contains("Weekly Allotment") ||
                    normalizedLine.Contains("Time Limit") ||
                    normalizedLine.Contains("Bytes Nuked"))
                {
                    foreach (string part in normalizedLine.Split('~'))
                    {
                        var m = Regex.Match(part, @"^(?<key>.+?):\s*(?<val>.+)$");
                        if (m.Success)
                        {
                            string key = m.Groups["key"].Value.Trim();
                            string val = m.Groups["val"].Value.Trim();
            
                            if (fieldMap.TryGetValue(key, out var entry))
                            {
                                string cleanedVal = Regex.Replace(val, @"\s*\([^)]*\)", "").Trim();
                                if (key.Equals("Time Limit", StringComparison.OrdinalIgnoreCase))
                                    cleanedVal = cleanedVal.Replace("minutes", "", StringComparison.OrdinalIgnoreCase).Replace(".", "").Trim();
            
                                entry.Item1.Text = cleanedVal;
                                entry.Item2.Invoke(cleanedVal);
                            }
                        }
                    }
                }
                else
                {
                    // Default regex-based parsing for normal lines
                    foreach (Match m in Regex.Matches(normalizedLine, @"(?<key>[^:~]+):\s*(?<val>[^~]*)"))
                    {
                        string key = m.Groups["key"].Value.Trim();
                        string val = m.Groups["val"].Value.Trim();
            
                        if (fieldMap.TryGetValue(key, out var entry))
                        {
                            string cleanedVal = Regex.Replace(val, @"\s*\([^)]*\)", "").Trim();
                            if (key.Equals("Time Limit", StringComparison.OrdinalIgnoreCase))
                                cleanedVal = cleanedVal.Replace("minutes", "", StringComparison.OrdinalIgnoreCase).Replace(".", "").Trim();
                            else if (key.Equals("Tagline", StringComparison.OrdinalIgnoreCase))
                                cleanedVal = cleanedVal.Replace("\"", "").Trim();
            
                            string display = key == "Ratios" ? ParseRatioValue(cleanedVal) : cleanedVal;
                            entry.Item1.Text = display;
                            entry.Item2.Invoke(display);
                        }
                    }
                }
            }

            // Extract specific wide-line fields that may not parse via default logic
            var wideFieldLookups = new (string Field, string Pattern, TextBox Target, Action<string>? Store)[]
            {
                ("Max Upload Speed", @"Max Upload Speed:\s+([^\s~]+)", maxUploadSpeedText, null),
                ("Max Download Speed", @"Max Download Speed:\s+([^\s~]+)", maxDownloadSpeedText, null),
                ("Weekly Allotment", @"Weekly Allotment:\s+([^\s~]+)", weeklyAllotmentText, null),
                ("Bytes Nuked", @"Bytes Nuked:\s+([^\s~]+)", bytesNukedText, null),
                ("Time Limit", @"Time Limit:\s+([^\s~]+)", timeLimitText, v => _oldTimeLimit = v)
            };
            
            foreach (var (field, pattern, target, store) in wideFieldLookups)
            {
                var match = Regex.Match(response, pattern);
                if (match.Success)
                {
                    var value = match.Groups[1].Value.Trim()
                        .Replace("minutes", "", StringComparison.OrdinalIgnoreCase)
                        .Trim();
            
                    target.Text = value;
                    store?.Invoke(value);
                }
            }


        }

        private string ParseRatioValue(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return "";
            if (val.Equals("Unlimited", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            var match = Regex.Match(val, "1:(\\d+)");
            return match.Success ? match.Groups[1].Value : int.TryParse(val, out _) ? val : "";
        }

        private async void FlagsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newFlags = flagsText.Text.Trim().ToUpper();
            if (_oldFlags == newFlags) return;

            bool siteOpRemoved = (_oldFlags?.Contains('1') == true) && !newFlags.Contains('1');
            if (siteOpRemoved)
            {
                MessageBox.Show(
                    $"You are trying to remove the SiteOP flag (1). This must be done manually via shell by removing flag 1 from ftp-data/users/{_username}.",
                    "SiteOP Flag Removal Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                flagsText.Text = _oldFlags;
                return;
            }
            var added = newFlags.Except(_oldFlags ?? "");
            var removed = (_oldFlags ?? "").Except(newFlags);

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                foreach (var flag in added)
                {
                    var result = await Task.Run(() => _ftp.UpdateUserFlags(_username, flag.ToString(), true));
                    if (result.Contains("Error")) throw new Exception(result);
                }

                foreach (var flag in removed)
                {
                    var result = await Task.Run(() => _ftp.UpdateUserFlags(_username, flag.ToString(), false));
                    if (result.Contains("Error")) throw new Exception(result);
                }

                _oldFlags = newFlags;
                GroupChanged?.Invoke();
                UserChanged?.Invoke(_username);
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating flags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                flagsText.Text = _oldFlags;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void RatiosTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = ParseRatioValue(ratiosText.Text.Trim());
            if (_oldRatios == newVal || string.IsNullOrEmpty(newVal)) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} ratio {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldRatios = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating ratio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ratiosText.Text = _oldRatios;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private void ExpiresInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DateOrZeroInputFilter(sender, e);
        }
        
        private async void ExpiresTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = expiresText.Text.Trim();
            if (_oldExpires == newVal) return;
            if (newVal.Equals("Never", StringComparison.OrdinalIgnoreCase)) newVal = "0";

            if (!glFTPd_Commander.Utils.InputUtils.IsValidExpiresInput(newVal))
            {
                MessageBox.Show("Expires must be 0 or in the format YYYY-MM-DD.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                expiresText.Text = _oldExpires;
                expiresText.Focus();
                return;
            }

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} expires {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldExpires = newVal == "0" ? "Never" : newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating expires: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                expiresText.Text = _oldExpires;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void IdleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = idleTimeText.Text.Trim();
            if (_oldIdleTime == newVal || !int.TryParse(newVal, out int minutes)) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} idle_time {minutes}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldIdleTime = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating idle time: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                idleTimeText.Text = _oldIdleTime;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newName = usernameText.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName.Equals(_username, StringComparison.OrdinalIgnoreCase))
                return;

            
            if (_username.Equals(_currentUser, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You cannot rename your own account while logged in.", "Security Restriction",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                usernameText.Text = _username;
                return;
            }

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE RENUSER {_username} {newName}", _ftpClient));
                if (result.Contains("Error"))
                    throw new Exception(result);

                _username = newName; // Update the local username after rename
                GroupChanged?.Invoke();

                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error renaming user: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                usernameText.Text = _username;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        
        private async void MaxLoginsText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = maxLoginsText.Text.Trim();
            if (_oldMaxLogins == newVal || !int.TryParse(newVal, out _)) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} max_logins {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldMaxLogins = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating max logins: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                maxLoginsText.Text = _oldMaxLogins;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void FromSameIpText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = fromSameIpText.Text.Trim();
            if (_oldFromSameIp == newVal || !int.TryParse(newVal, out _)) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} same_ip {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldFromSameIp = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating same IP: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                fromSameIpText.Text = _oldFromSameIp;
            }
            finally 
            { 
                _ftp.ConnectionLock.Release(); 
            }
        }
        
        private async void TaglineText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = taglineText.Text.Trim();
            if (_oldTagline == newVal) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} tagline {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldTagline = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating tagline: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                taglineText.Text = _oldTagline;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void UserCommentText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = userCommentText.Text.Trim();
            if (_oldUserComment == newVal) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} comment {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldUserComment = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating user comment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                userCommentText.Text = _oldUserComment;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void MaxSimUploadsText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = maxSimUploadsText.Text.Trim();
            if (_oldMaxSimUploads == newVal || !int.TryParse(newVal, out _)) return;

            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} max_sim_up {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldMaxSimUploads = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating max simultaneous uploads: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                maxSimUploadsText.Text = _oldMaxSimUploads;
            }
            finally 
            { 
                _ftp.ConnectionLock.Release(); 
            }
        }
        
        private async void MaxSimDownloadsText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = maxSimDownloadsText.Text.Trim();
            if (_oldMaxSimDownloads == newVal || !int.TryParse(newVal, out _)) return;
            
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHANGE {_username} max_sim_dn {newVal}", _ftpClient));
                if (result.Contains("Error")) throw new Exception(result);
                _oldMaxSimDownloads = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating max simultaneous downloads: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                maxSimDownloadsText.Text = _oldMaxSimDownloads;
            }
            finally 
            { 
                _ftp.ConnectionLock.Release(); 
            }
        }
        
        private async void AddGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (availableGroupsList.SelectedItems.Count > 0)
            {
                if (_ftp == null || _ftpClient == null)
                {
                    MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                await _ftp.ConnectionLock.WaitAsync();
                try
                {
                    foreach (string group in availableGroupsList.SelectedItems)
                    {
                        // Robust connection check inside the loop
                        if (!FTP.EnsureConnected(ref _ftpClient, _ftp))
                        {
                            MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
        
                        try
                        {
                            string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGRP {_username} {group}", _ftpClient));
                            if (result.Contains("Error"))
                            {
                                MessageBox.Show($"Error adding group {group}: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[UserInfoView] Exception while adding group {group}: {ex}");
                            MessageBox.Show($"Exception while adding group {group}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
        
                    // Reload only once after all groups are processed
                    await LoadUserDetails();
                    GroupChanged?.Invoke();
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
            }
        }

        
        private async void RemoveGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (userGroupsList.SelectedItems.Count > 0)
            {
                if (_ftp == null || _ftpClient == null)
                {
                    MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                await _ftp.ConnectionLock.WaitAsync();
                try
                {
                    foreach (string group in userGroupsList.SelectedItems)
                    {
                        // Robust connection check inside the loop
                        if (!FTP.EnsureConnected(ref _ftpClient, _ftp))
                        {
                            MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        try
                        {
                            string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE CHGRP {_username} {group}", _ftpClient));
                            if (result.Contains("Error"))
                            {
                                MessageBox.Show($"Error removing group {group}: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[UserInfoView] Exception while removing group {group}: {ex}");
                            MessageBox.Show($"Exception while removing group {group}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    
                    // Reload only once after all groups are processed
                    await LoadUserDetails();
                    GroupChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing groups: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
            }
        }
        
        private async void RemoveIpButton_Click(object sender, RoutedEventArgs e)
        {
            if (ipRestrictionsList.SelectedItem is KeyValuePair<string, string> selectedIp)
            {
                string ipNumber = selectedIp.Key.Replace("IP", "");
                if (_ftp == null || _ftpClient == null)
                {
                    MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                await _ftp.ConnectionLock.WaitAsync();
                try
                {
                    string result = await Task.Run(() => _ftp.ExecuteDelIpCommand(_username, ipNumber, _ftpClient));
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show(result, "Error Removing IP", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        //MessageBox.Show("IP restriction removed successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadUserDetails();
                    }
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
            }
            else
            {
                MessageBox.Show("Please select an IP restriction to remove.", "No IP Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private async void AddIpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var addIpWindow = new AddIpWindow
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (addIpWindow.ShowDialog() == true)
            {
                string ipAddress = addIpWindow.IPAddress;
                await _ftp.ConnectionLock.WaitAsync();
                try
                {
                    string result = await Task.Run(() => _ftp.AddIpRestriction(_username, ipAddress, _ftpClient));
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show(result, "Error Adding IP", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        //MessageBox.Show("IP restriction added successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadUserDetails();
                    }
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
            }
        }
        
        private async void AddCreditsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            var window = new CreditAdjustWindow(_ftp, _ftpClient, _username, "GIVE")
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (window.ShowDialog() == true)
                await LoadUserDetails();
        }

        
        private async void TakeCreditsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            var window = new CreditAdjustWindow(_ftp, _ftpClient, _username, "TAKE")
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (window.ShowDialog() == true)
                await LoadUserDetails();
        }


        private async void userReAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                string result = await Task.Run(() => _ftp.ExecuteCommand($"SITE READD {_username}", _ftpClient));
                if (!result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    UserDeleted?.Invoke();
                    UserChanged?.Invoke(_username);
                    await LoadUserDetails();
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void userRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if ((flagsText.Text ?? "").Contains("1"))
            {
                MessageBox.Show(
                    $"You are trying to remove a SiteOP = (flag 1). Please remove the flag 1 manually from the shell from ftp-data/users/{_username}.",
                    "Removal Of SiteOP Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Debug.WriteLine($"[UserInfoView] Delete blocked: '{_username}' has flag 1 (SiteOP).");
                return;
            }

            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                string result = await Task.Run(() => _ftp.DelUser(_username, _ftpClient));
                if (!result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    UserDeleted?.Invoke();
                    UserChanged?.Invoke(_username);
                    await LoadUserDetails();
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void userPurge_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                string result = await Task.Run(() => _ftp.PurgeUser(_username, _ftpClient));
                if (!result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    UserDeleted?.Invoke();
                    UnselectUserOnClose = true;
                    RequestClose?.Invoke();
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void TimeLimitText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = timeLimitText.Text.Trim();
            if (_oldTimeLimit == newVal) return;
        
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() =>
                    _ftp.ExecuteCommand($"SITE CHANGE {_username} time_limit {newVal}", _ftpClient));
                if (result.Contains("Error"))
                    throw new Exception(result);
        
                _oldTimeLimit = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating time limit: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                timeLimitText.Text = _oldTimeLimit;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }
        
        private async void TimeframeText_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = timeframeText.Text.Trim();
            if (_oldTimeframe == newVal) return;
        
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            await _ftp.ConnectionLock.WaitAsync();
            try
            {
                var result = await Task.Run(() =>
                    _ftp.ExecuteCommand($"SITE CHANGE {_username} timeframe {newVal}", _ftpClient));
                if (result.Contains("Error"))
                    throw new Exception(result);
        
                _oldTimeframe = newVal;
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating timeframe: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                timeframeText.Text = _oldTimeframe;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void SetAllotmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ftp == null || _ftpClient == null)
            {
                MessageBox.Show("FTP client not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        
            var setAllotmentWindow = new SetAllotmentWindow(_ftp, _ftpClient, _username)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (setAllotmentWindow.ShowDialog() == true)
            {
                await LoadUserDetails();
            }
        }

        private async void SetMaxUploadSpeed_Click(object sender, RoutedEventArgs e)
        {
            var win = new SetSpeedWindow(_ftp!, _ftpClient!, _username, "max_ulspeed")
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (win.ShowDialog() == true)
            {
                await LoadUserDetails();
            }
        }
        
        private async void SetMaxDownloadSpeed_Click(object sender, RoutedEventArgs e)
        {
            var win = new SetSpeedWindow(_ftp!, _ftpClient!, _username, "max_dlspeed")
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        
            if (win.ShowDialog() == true)
            {
                await LoadUserDetails();
            }
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                UnselectUserOnClose = true;
                RequestClose?.Invoke();
                e.Handled = true;
            }
        }

        private void ValueInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsOrNegative(sender, e);
        }

        private void FlagsInput(object sender, TextCompositionEventArgs e)
        {
            glFTPd_Commander.Utils.InputUtils.DigitsAndLettersOnly(sender, e);
        }
    }
}
