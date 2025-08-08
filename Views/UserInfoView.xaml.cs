using FluentFTP;
using glFTPd_Commander.FTP;
using glFTPd_Commander.Models;
using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using glFTPd_Commander.Windows;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Debug = System.Diagnostics.Debug;

namespace glFTPd_Commander.Views
{
    public partial class UserInfoView : BaseUserControl
    {
        private readonly GlFtpdClient? _ftp;
        private FtpClient? _ftpClient;
        private string _username;
        private readonly string _currentUser;
        private string? _oldFlags, _oldRatios, _oldExpires, _oldIdleTime, _oldMaxLogins, _oldFromSameIp, _oldTagline, _oldUserComment, _oldMaxSimUploads, _oldMaxSimDownloads, _oldTimeLimit, _oldTimeframe, _oldIp;

        public event Action? GroupChanged;
        public event Action? UserDeleted;
        public event Action<string>? UserChanged;
        public bool UnselectOnClose { get; protected set; }
        protected override string? FocusTargetName => "UsernameTextBox";


        private static readonly char[] LineCharDelimiters = ['\n', '\r'];

        public UserInfoView(GlFtpdClient ftp, FtpClient ftpClient, string username, string currentUser)
        {
            InitializeComponent();
            _ftp = ftp;
            _ftpClient = ftpClient;
            _username = username;
            _currentUser = currentUser;
            Loaded += UserInfoView_Loaded;
            Loaded += (s, e) => UsernameTextBox.Focus();
        }

        private async void UserInfoView_Loaded(object sender, RoutedEventArgs e) => await LoadUserDetails();

        private async Task LoadUserDetails()
        {
            try
            {
                ResetAllFields();
                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                    if (_ftpClient == null) return;

                var (userDetails, updatedClient) = await FtpBase.ExecuteWithConnectionAsync(
                    _ftpClient, _ftp!, c => Task.Run(() => _ftp!.GetUserDetails(_username, c))
                );
                _ftpClient = updatedClient;

                if (userDetails == null)
                {
                    MessageBox.Show("Lost connection to the FTP server. Please reconnect.", "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UsernameTextBox.Text = userDetails.Username;
                FlagsTextBox.Text = userDetails.Flags ?? string.Empty;

                if ((userDetails.Flags ?? "").Contains('6'))
                {
                    UserPurgeButton.Visibility = Visibility.Visible;
                    UserReAddButton.Visibility = Visibility.Visible;
                    UserRemoveButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    UserPurgeButton.Visibility = Visibility.Collapsed;
                    UserReAddButton.Visibility = Visibility.Collapsed;
                    UserRemoveButton.Visibility = Visibility.Visible;
                }

                var restrictions = userDetails.IpRestrictions
                    .Where(ip => !string.IsNullOrWhiteSpace(ip.Value))
                    .OrderBy(ip => ip.Value, StringComparer.OrdinalIgnoreCase)
                    .Select((ip, idx) => new IpRestriction((idx + 1).ToString(), ip.Value))
                    .ToList();

                AddIpButton.IsEnabled = restrictions.Count < 10;
                IpRestrictionsDataGrid.ItemsSource = restrictions;
                NoRestrictionsTextBlock.Visibility = restrictions.Count > 0 ? Visibility.Collapsed : Visibility.Visible;


                // Get all groups (with connection safety)
                var (allGroups, groupsUpdatedClient) = await FtpBase.ExecuteWithConnectionAsync(
                    _ftpClient, _ftp!, c => _ftp!.GetGroups(c)
                );
                _ftpClient = groupsUpdatedClient;

                var userGroupsNormalized = new HashSet<string>(
                userDetails.Groups.Select(g => g.TrimStart('*', '+')), StringComparer.OrdinalIgnoreCase);

                AvailableGroupsListBox.ItemsSource = allGroups?
                    .Where(g => !userGroupsNormalized.Contains(g.Group.TrimStart('*', '+')))
                    .Select(g => g.Group)
                    .OrderBy(g => g)
                    .ToList();

                UserGroupsListBox.ItemsSource = userDetails.Groups
                    .Select(g => g.TrimStart('*'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g)
                    .ToList();

                var adminGroupsUserIsIn = userDetails.Groups
                    .Where(g => g.StartsWith('*'))
                    .Select(g => g.TrimStart('*'))
                    .OrderBy(g => g)
                    .ToList();
                GroupAdminListBox.ItemsSource = adminGroupsUserIsIn;

                var (reply, updatedClient2) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE USER {_username}", _ftpClient, _ftp!);
                _ftpClient = updatedClient2;
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
                UsernameTextBox, UserCommentTextBox, AddedByTextBox, TimeOnTodayTextBox, FlagsTextBox, RatiosTextBox,
                CreditsTextBox, TotalLoginsTextBox, MaxLoginsTextBox, MaxSimUploadsTextBox, MaxUploadSpeedTextBox,
                TimesNukedTextBox, WeeklyAllotmentTextBox, CreatedTextBox, ExpiresTextBox, LastSeenTextBox,
                IdleTimeTextBox, CurrentLoginsTextBox, FromSameIpTextBox, MaxSimDownloadsTextBox,
                MaxDownloadSpeedTextBox, BytesNukedTextBox, TimeLimitTextBox, TimeframeTextBox, TaglineTextBox
            }) ctrl.Text = string.Empty;

            _oldFlags = _oldRatios = _oldExpires = _oldIdleTime = _oldMaxLogins =
            _oldFromSameIp = _oldTagline = _oldUserComment = _oldMaxSimUploads =
            _oldMaxSimDownloads = _oldTimeLimit = _oldTimeframe =
            string.Empty;


            AvailableGroupsListBox.ItemsSource = null;
            UserGroupsListBox.ItemsSource = null;
            IpRestrictionsDataGrid.ItemsSource = null;
            NoRestrictionsTextBlock.Visibility = Visibility.Visible;
        }

        private void ParseDetailedUserInfo(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return;

            var fieldMap = new Dictionary<string, (TextBox, Action<string>)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Username"] = (UsernameTextBox, _ => { }),
                ["User Comment"] = (UserCommentTextBox, v => _oldUserComment = v),
                ["Added by"] = (AddedByTextBox, _ => { }),
                ["Created"] = (CreatedTextBox, val =>
                {
                    if (val == "0")
                    {
                        CreatedTextBox.Text = "0";
                    }
                    else if (DateTime.TryParseExact(val, "MM-dd-yy", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        CreatedTextBox.Text = dt.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        CreatedTextBox.Text = val; // fallback
                    }
                }),
                ["Expires"] = (ExpiresTextBox, v => _oldExpires = v),
                ["Time On Today"] = (TimeOnTodayTextBox, val =>
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
                
                    TimeOnTodayTextBox.Text = result;
                }),
                ["Last seen"] = (LastSeenTextBox, val =>
                {
                    if (val.Equals("Never", StringComparison.OrdinalIgnoreCase))
                    {
                        LastSeenTextBox.Text = "Never";
                    }
                    else if (DateTime.TryParseExact(val, "ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        LastSeenTextBox.Text = dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        LastSeenTextBox.Text = val; // fallback
                    }
                }),
                ["Flags"] = (FlagsTextBox, v => _oldFlags = v.Trim().ToUpper()),
                ["Idle time"] = (IdleTimeTextBox, v => _oldIdleTime = v),
                ["Ratios"] = (RatiosTextBox, v => _oldRatios = ParseRatioValue(v)),
                ["Credits"] = (CreditsTextBox, _ => { }),
                ["Total Logins"] = (TotalLoginsTextBox, _ => { }),
                ["Current Logins"] = (CurrentLoginsTextBox, _ => { }),
                ["Max Logins"] = (MaxLoginsTextBox, v => _oldMaxLogins = v),
                ["From same IP"] = (FromSameIpTextBox, v => _oldFromSameIp = v),
                ["Max Sim Uploads"] = (MaxSimUploadsTextBox, v => _oldMaxSimUploads = v),
                ["Max Sim Downloads"] = (MaxSimDownloadsTextBox, v => _oldMaxSimDownloads = v),
                ["Max Upload Speed"] = (MaxUploadSpeedTextBox, v => { }),
                ["Max Download Speed"] = (MaxDownloadSpeedTextBox, v => { }),
                ["Times Nuked"] = (TimesNukedTextBox, _ => { }),
                ["Bytes Nuked"] = (BytesNukedTextBox, v => { }),
                ["Weekly Allotment"] = (WeeklyAllotmentTextBox, v => { }),
                ["Time Limit"] = (TimeLimitTextBox, v => _oldTimeLimit = v),
                ["Timeframe"] = (TimeframeTextBox, v => _oldTimeframe = v),
                ["Tagline"] = (TaglineTextBox, v => _oldTagline = v)
            };

            var lines = response.Split(LineCharDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("200-")).Select(l => l[4..].Trim());

            foreach (var line in lines)
            {
                if (line.StartsWith("| User Comment:"))
                {
                    var comment = line[16..].Trim();
                    UserCommentTextBox.Text = comment;
                    _oldUserComment = comment;
                    continue;
                }
            
                string normalizedLine = MultiSpaceRegex().Replace(line.Trim('|').Trim(), "~");
            
                // Explicit handling of multi-field lines
                if (normalizedLine.Contains("Max Upload Speed") ||
                    normalizedLine.Contains("Max Download Speed") ||
                    normalizedLine.Contains("Weekly Allotment") ||
                    normalizedLine.Contains("Time Limit") ||
                    normalizedLine.Contains("Bytes Nuked"))
                {
                    foreach (string part in normalizedLine.Split('~'))
                    {
                        var m = KeyValueRegex().Match(part);
                        if (m.Success)
                        {
                            string key = m.Groups["key"].Value.Trim();
                            string val = m.Groups["val"].Value.Trim();
            
                            if (fieldMap.TryGetValue(key, out var entry))
                            {
                                string cleanedVal = ParenCleanupRegex().Replace(val, "").Trim();
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
                    foreach (Match m in MultiKeyValueRegex().Matches(normalizedLine))
                    {
                        string key = m.Groups["key"].Value.Trim();
                        string val = m.Groups["val"].Value.Trim();
            
                        if (fieldMap.TryGetValue(key, out var entry))
                        {
                            string cleanedVal = ParenCleanupRegex().Replace(val, "").Trim();
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
                ("Max Upload Speed", @"Max Upload Speed:\s+([^\s~]+)", MaxUploadSpeedTextBox, null),
                ("Max Download Speed", @"Max Download Speed:\s+([^\s~]+)", MaxDownloadSpeedTextBox, null),
                ("Weekly Allotment", @"Weekly Allotment:\s+([^\s~]+)", WeeklyAllotmentTextBox, null),
                ("Bytes Nuked", @"Bytes Nuked:\s+([^\s~]+)", BytesNukedTextBox, null),
                ("Time Limit", @"Time Limit:\s+([^\s~]+)", TimeLimitTextBox, v => _oldTimeLimit = v)
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

        private static string ParseRatioValue(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return "";
            if (val.Equals("Unlimited", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            var match = RatioRegex().Match(val);
            return match.Success ? match.Groups[1].Value : int.TryParse(val, out _) ? val : "";
        }

        private async Task ApplyUserChange(string command, string oldValue, Action<string> updateOld, TextBox control)
        {
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
        
                updateOld(control.Text.Trim());
                await LoadUserDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception applying change: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                control.Text = oldValue;
            }
            finally { _ftp.ConnectionLock.Release(); }
        }

        private async void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = UsernameTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a username.", UsernameTextBox))
            {
                UsernameTextBox.Text = _username;
                return;
            }
            if (newVal.Equals(_username, StringComparison.OrdinalIgnoreCase)) return;
            if (_username.Equals(_currentUser, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You cannot rename your own account while logged in.", "Security Restriction", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Text = _username;
                return;
            }

            // Prevent renaming to an existing username
            if (await ExistenceChecks.UsernameExistsAsync(_ftp!, _ftpClient, newVal))
            {
                MessageBox.Show("A user with this name already exists.", "Duplicate Username",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Text = _username;
                UsernameTextBox.Focus();
                UsernameTextBox.SelectAll();
                Debug.WriteLine($"[UserInfoView] Prevented renaming '{_username}' to existing username '{newVal}'");
                return;
            }

            await ApplyUserChange($"SITE RENUSER {_username} {newVal}", _username, v => _username = v, UsernameTextBox);

        }

        private async void SetPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string? newPassword = await SetPasswordWindow.SetPassword(Window.GetWindow(this), _ftp!, _ftpClient, _username);
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                await LoadUserDetails();
            }
        }

        private async void FlagsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newFlags = FlagsTextBox.Text.Trim().ToUpper();
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newFlags), "Please enter flags.", FlagsTextBox))
            {
                FlagsTextBox.Text = _oldFlags;
                return;
            }
            if (_oldFlags == newFlags) return;

            bool siteOpRemoved = (_oldFlags?.Contains('1') == true) && !newFlags.Contains('1');
            if (siteOpRemoved)
            {
                MessageBox.Show(
                    $"You are trying to remove the SiteOP flag (1). This must be done manually via shell by removing flag 1 from ftp-data/users/{_username}.",
                    "SiteOP Flag Removal Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                FlagsTextBox.Text = _oldFlags;
                return;
            }
            var added = newFlags.Except(_oldFlags ?? "");
            var removed = (_oldFlags ?? "").Except(newFlags);

            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                foreach (var flag in added)
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHANGE {_username} flags +{flag}", _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error")) throw new Exception(result);
                }
                
                foreach (var flag in removed)
                {
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHANGE {_username} flags -{flag}", _ftpClient, _ftp);
                    _ftpClient = updatedClient;
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
                FlagsTextBox.Text = _oldFlags;
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void RatiosTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = ParseRatioValue(RatiosTextBox.Text.Trim());
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  int.TryParse(newVal, out _)),
                "Ratio must be an integer not less than 0, 0 = Unlimited.", RatiosTextBox))
            {
                RatiosTextBox.Text = _oldRatios;
                return;
            }
            if (_oldRatios == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} ratio {newVal}", _oldRatios!, v => _oldRatios = v, RatiosTextBox);
        }
        
        private async void ExpiresTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = ExpiresTextBox.Text.Trim();
            string checkedVal = newVal.Equals("Never", StringComparison.OrdinalIgnoreCase) ? "0" : newVal;
            if (InputUtils.ValidateAndWarn(!InputUtils.IsValidExpiresInput(checkedVal), "Expires must be in the format YYYY-MM-DD, 0 = Never.", ExpiresTextBox))
            {
                ExpiresTextBox.Text = _oldExpires;
                return;
            }
            if (_oldExpires == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} expires {checkedVal}", _oldExpires!, v => _oldExpires = v == "0" ? "Never" : v, ExpiresTextBox);
        }
        
        private async void IdleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = IdleTimeTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int seconds) && seconds >= -1)),
                "Idle time must be an integer in seconds, -1 = disabled.", IdleTimeTextBox))
            {
                IdleTimeTextBox.Text = _oldIdleTime;
                return;
            }
            if (_oldIdleTime == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} idle_time {newVal}", _oldIdleTime!, v => _oldIdleTime = v, IdleTimeTextBox);
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
            if (_oldMaxLogins == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} max_logins {newVal}", _oldMaxLogins!, v => _oldMaxLogins = v, MaxLoginsTextBox);
        }

        private async void FromSameIpTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = FromSameIpTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= 0)),
                "From same IP must be an integer not less than 0\n\n 0 = Unlimited.", FromSameIpTextBox))
            {
                FromSameIpTextBox.Text = _oldFromSameIp;
                return;
            }
            if (_oldFromSameIp == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} num_logins {newVal}", _oldFromSameIp!, v => _oldFromSameIp = v, FromSameIpTextBox);
        }
        
        private async void TaglineTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = TaglineTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a tagline.", TaglineTextBox))
            {
                TaglineTextBox.Text = _oldTagline;
                return;
            }
            if (_oldTagline == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} tagline {newVal}", _oldTagline!, v => _oldTagline = v, TaglineTextBox);
        }
        
        private async void UserCommentTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = UserCommentTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(newVal), "Please enter a user comment.", UserCommentTextBox))
            {
                UserCommentTextBox.Text = _oldUserComment;
                return;
            }
            if (_oldUserComment == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} comment {newVal}", _oldUserComment!, v => _oldUserComment = v, UserCommentTextBox);
        }
        
        private async void MaxSimUploadsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = MaxSimUploadsTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= -1 && val <= 30000)),
                "Max simultaneous uploads must be between -1 and 30000\n\n 0 = None, -1 = Unlimited.", MaxSimUploadsTextBox))
            {
                MaxSimUploadsTextBox.Text = _oldMaxSimUploads;
                return;
            }
            if (_oldMaxSimUploads == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} max_sim_up {newVal}", _oldMaxSimUploads!, v => _oldMaxSimUploads = v, MaxSimUploadsTextBox);
        }
        
        private async void MaxSimDownloadsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = MaxSimDownloadsTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                string.IsNullOrWhiteSpace(newVal) ||
                !(newVal.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                  (int.TryParse(newVal, out int val) && val >= -1 && val <= 30000)),
                "Max simultaneous downloads must be between -1 and 30000\n\n 0 = None, -1 = Unlimited.", MaxSimDownloadsTextBox))
            {
                MaxSimDownloadsTextBox.Text = _oldMaxSimDownloads;
                return;
            }
            if (_oldMaxSimDownloads == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} max_sim_dn {newVal}", _oldMaxSimDownloads!, v => _oldMaxSimDownloads = v, MaxSimDownloadsTextBox);
        }

        private async void TimeLimitTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = TimeLimitTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                    !int.TryParse(newVal, out int val) || val < 0,
                    "Time limit must be an integer not less than 0 in minutes\n\n 0 = Unlimited.", TimeLimitTextBox))
            {
                TimeLimitTextBox.Text = _oldTimeLimit;
                return;
            }
            if (_oldTimeLimit == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} time_limit {newVal}", _oldTimeLimit!, v => _oldTimeLimit = v, TimeLimitTextBox);
        }
        
        private async void TimeframeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newVal = TimeframeTextBox.Text.Trim();
            if (InputUtils.ValidateAndWarn(
                    !InputUtils.IsValidTimeframe(newVal),
                    "Timeframe must be two hours (e.g. '8 17'), 0 0 = all day ).",
                    TimeframeTextBox))
            {
                TimeframeTextBox.Text = _oldTimeframe;
                TimeframeTextBox.SelectAll();
                return;
            }
            if (_oldTimeframe == newVal) return;
            await ApplyUserChange($"SITE CHANGE {_username} timeframe {newVal}", _oldTimeframe!, v => _oldTimeframe = v, TimeframeTextBox);
        }
        
        private async void AddGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableGroupsListBox.SelectedItems.Count > 0)
            {
                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;
        
                await _ftp!.ConnectionLock.WaitAsync();
                try
                {
                    foreach (string group in AvailableGroupsListBox.SelectedItems)
                    {
                        try
                        {
                            var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGRP {_username} {group}", _ftpClient, _ftp);
                            _ftpClient = updatedClient;
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
        
                    await LoadUserDetails();
                    GroupChanged?.Invoke();
                }
                finally
                {
                    _ftp!.ConnectionLock.Release();
                }
            }
        }

        private async void RemoveGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserGroupsListBox.SelectedItems.Count == 0)
                return;
        
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
            if (_ftpClient == null) return;
        
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                foreach (string group in UserGroupsListBox.SelectedItems)
                {
                    string groupName = group.TrimStart('+');
                    string? error = null;
        
                    // If group is a group admin, remove admin status first
                    if (group.StartsWith('+'))
                    {
                        var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGADMIN {_username} {groupName}", _ftpClient, _ftp);
                        _ftpClient = updatedClient;
                        if (result.Contains("Error"))
                        {
                            error = $"Error removing group admin status from group {groupName}: {result}";
                        }
                    }
        
                    if (error == null)
                    {
                        var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE CHGRP {_username} {groupName}", _ftpClient, _ftp);
                        _ftpClient = updatedClient;
                        if (result.Contains("Error"))
                        {
                            error = $"Error removing group {groupName}: {result}";
                        }
                    }
        
                    if (error != null)
                    {
                        MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
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
                _ftp!.ConnectionLock.Release();
            }
        }

        private async void AddGroupAdminButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserGroupsListBox.SelectedItem is string group)
            {
                // Optionally: check if already admin, using groupAdminList.ItemsSource
                if (GroupAdminListBox.ItemsSource is IEnumerable<string> admins && admins.Contains(group, StringComparer.OrdinalIgnoreCase))
                    return;

                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;
        
                await _ftp!.ConnectionLock.WaitAsync();
                try
                {
                    var command = $"SITE CHGADMIN {_username} {group}";
                    Debug.WriteLine($"[UserInfoView] {command}");
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error adding group admin: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
                await LoadUserDetails();
                GroupChanged?.Invoke();
            }
        }

        private async void RemoveGroupAdminButton_Click(object sender, RoutedEventArgs e)
        {
            if (GroupAdminListBox.SelectedItem is string group)
            {
                _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;
        
                await _ftp!.ConnectionLock.WaitAsync();
                try
                {
                    var command = $"SITE CHGADMIN {_username} {group}";
                    Debug.WriteLine($"[UserInfoView] {command}");
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(command, _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error removing group admin: {result}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                finally
                {
                    _ftp.ConnectionLock.Release();
                }
                await LoadUserDetails();
                GroupChanged?.Invoke();
            }
        }

        private async void AddIpButton_Click(object sender, RoutedEventArgs e)
        {
            string? addedIp = await AddIpWindow.ShowAndAddIp(Window.GetWindow(this), _ftp!, _ftpClient, _username);
            if (!string.IsNullOrWhiteSpace(addedIp))
            {
                await LoadUserDetails();
                await HighlightIpRestriction(addedIp);
            }
        }
        
        private async void RemoveIpButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedIps = IpRestrictionsDataGrid.SelectedItems
                .OfType<IpRestriction>()
                .ToList();
        
            if (selectedIps.Count == 0)
            {
                MessageBox.Show("Please select one or more IP restrictions to remove.", "No IP Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        
            // Confirm removal for multiple items
            if (selectedIps.Count > 1)
            {
                var confirm = MessageBox.Show(
                    $"Are you sure you want to remove {selectedIps.Count} selected IP restrictions?",
                    "Confirm Remove Multiple",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                if (confirm != MessageBoxResult.Yes)
                    return;
            }
        
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
            if (_ftpClient == null) return;
        
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                foreach (var ip in selectedIps)
                {
                    string ipAddress = ip.IpValue.Trim();
                    if (string.IsNullOrWhiteSpace(ipAddress))
                        continue;
        
                    var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE DELIP {_username} {ipAddress}", _ftpClient, _ftp);
                    _ftpClient = updatedClient;
                    if (result.Contains("Error"))
                    {
                        MessageBox.Show($"Error removing {ipAddress}:\n{result}", "Error Removing IP", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                await LoadUserDetails();
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private void IpRestrictionsDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is IpRestriction restriction)
            {
                _oldIp = restriction.IpValue;
                Debug.WriteLine($"[IpEdit] Original IP before edit: {_oldIp}");
            }
        }

        private async void IpRestrictionsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not IpRestriction row) return;
        
            var ipRestriction = row;
            string newIp = ipRestriction.IpValue?.Trim() ?? "";
        
            if (!InputUtils.IsValidGlftpdIp(newIp)) {
                MessageBox.Show(
                    "Invalid IP restriction format. Examples:\n*@127.0.0.1\n*@127.0.0.*\n*@2001:db8::*\nident@127.0.0.1\nident@2001:db8::1\n*@*",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadUserDetails();
                _oldIp = null;
                return;
            }
        
            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(_oldIp))
                {
                    MessageBox.Show("Unable to determine the original IP for removal.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await LoadUserDetails();
                    return;
                }
        
                // Remove the old IP by value
                var (delResult, delClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(
                    $"SITE DELIP {_username} {_oldIp}", _ftpClient, _ftp);
                _ftpClient = delClient;
        
                if (delResult.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to remove old IP: {delResult}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await LoadUserDetails();
                    return;
                }
        
                // Add the new IP
                var (addResult, addClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync(
                    $"SITE ADDIP {_username} {newIp}", _ftpClient, _ftp);
                _ftpClient = addClient;
        
                if (InputUtils.IsGlftpdIpAddError(addResult))
                {
                    MessageBox.Show(addResult, "IP Add Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    await LoadUserDetails();
                    return;
                }
                else if (addResult.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"Failed to add new IP: {addResult}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await LoadUserDetails();
                    return;
                }
        
                Debug.WriteLine($"[UserInfoView] Changed IP from {_oldIp} to {newIp} for user {_username}");
                await LoadUserDetails();
                await Task.Delay(50); // Small delay to allow UI refresh (tweak if needed)

                if (IpRestrictionsDataGrid.ItemsSource is IEnumerable<IpRestriction> ipList)
                {
                    var match = ipList.FirstOrDefault(r => r.IpValue?.Trim() == newIp);
                    if (match != null)
                    {
                        IpRestrictionsDataGrid.SelectedItem = match;
                        IpRestrictionsDataGrid.ScrollIntoView(match);
                        Debug.WriteLine($"[UserInfoView] Highlighted edited IP: {newIp}");
                    }
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
                _oldIp = null;
            }
        }
        
        private async void AddCreditsButton_Click(object sender, RoutedEventArgs e)
        {
            if (await CreditAdjustWindow.ShowAndAdjustCredits(Window.GetWindow(this), _ftp!, _ftpClient, _username, "GIVE"))
                await LoadUserDetails();
        }
        
        private async void TakeCreditsButton_Click(object sender, RoutedEventArgs e)
        {
            if (await CreditAdjustWindow.ShowAndAdjustCredits(Window.GetWindow(this), _ftp!, _ftpClient, _username, "TAKE"))
                await LoadUserDetails();
        }

        private async void UserReAdd_Click(object sender, RoutedEventArgs e)
        {
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE READD {_username}", _ftpClient, _ftp);
                _ftpClient = updatedClient;
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
        
        private async void UserRemove_Click(object sender, RoutedEventArgs e)
        {
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            if ((FlagsTextBox.Text ?? "").Contains('1'))
            {
                MessageBox.Show(
                    $"You are trying to remove a SiteOP = (flag 1). Please remove the flag 1 manually from the shell from ftp-data/users/{_username}.",
                    "Removal Of SiteOP Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Debug.WriteLine($"[UserInfoView] Delete blocked: '{_username}' has flag 1 (SiteOP).");
                return;
            }

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE DELUSER {_username}", _ftpClient, _ftp);
                _ftpClient = updatedClient;

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
        
        private async void UserPurge_Click(object sender, RoutedEventArgs e)
        {
            _ftpClient = await GlFtpdClient.EnsureConnectedWithUiAsync(_ftp, _ftpClient);
                if (_ftpClient == null) return;

            await _ftp!.ConnectionLock.WaitAsync();
            try
            {
                var (result, updatedClient) = await FtpBase.ExecuteFtpCommandWithReconnectAsync($"SITE PURGE {_username}", _ftpClient, _ftp);
                _ftpClient = updatedClient;

                if (!result.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    UserDeleted?.Invoke();
                    UnselectOnClose = true;
                    RequestClose?.Invoke();
                }
            }
            finally
            {
                _ftp.ConnectionLock.Release();
            }
        }

        private async void SetAllotmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SetAllotmentWindow.ShowAndSetAllotment(Window.GetWindow(this), _ftp!, _ftpClient, _username))
                await LoadUserDetails();
        }

        private async void SetMaxUploadSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (await SetSpeedWindow.ShowAndSetSpeed(Window.GetWindow(this), _ftp!, _ftpClient, _username, "max_ulspeed"))
                await LoadUserDetails();
        }

        private async void SetMaxDownloadSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (await SetSpeedWindow.ShowAndSetSpeed(Window.GetWindow(this), _ftp!, _ftpClient, _username, "max_dlspeed"))
                await LoadUserDetails();
        }

        private async Task HighlightIpRestriction(string ip)
        {
            // Wait for grid UI to update
            await Task.Delay(50);
        
            await Dispatcher.InvokeAsync(() =>
            {
                if (IpRestrictionsDataGrid.ItemsSource is IEnumerable<IpRestriction> ipList)
                {
                    var match = ipList.FirstOrDefault(r => r.IpValue?.Trim() == ip.Trim());
                    if (match != null)
                    {
                        IpRestrictionsDataGrid.SelectedItem = match;
                        IpRestrictionsDataGrid.ScrollIntoView(match);
                        Debug.WriteLine($"[UserInfoView] Highlighted added IP: {ip}");
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        protected override void OnEscape()
        {
            UnselectOnClose = true;
        }

        [GeneratedRegex(@" {2,}")]
        private static partial Regex MultiSpaceRegex();
        
        [GeneratedRegex(@"^(?<key>.+?):\s*(?<val>.+)$")]
        private static partial Regex KeyValueRegex();
        
        [GeneratedRegex(@"\s*\([^)]*\)")]
        private static partial Regex ParenCleanupRegex();
        
        [GeneratedRegex(@"(?<key>[^:~]+):\s*(?<val>[^~]*)")]
        private static partial Regex MultiKeyValueRegex();
        
        [GeneratedRegex(@"1:(\d+)")]
        private static partial Regex RatioRegex();
    }
}
