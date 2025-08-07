using Debug = System.Diagnostics.Debug;
using FluentFTP;
using glFTPd_Commander.FTP;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace glFTPd_Commander.Utils
{
    public static class ExistenceChecks
    {
        // Returns true if username exists (case-insensitive)
        public static async Task<bool> UsernameExistsAsync(GlFtpdClient ftp, FtpClient? client, string username)
        {
            if (ftp == null || string.IsNullOrWhiteSpace(username)) return false;
            var users = await ftp.GetUsers(client);
            Debug.WriteLine($"[ExistenceChecks] Checked {users.Count} users for '{username}'");
            return users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        // Returns true if group exists (case-insensitive)
        public static async Task<bool> GroupExistsAsync(GlFtpdClient ftp, FtpClient? client, string groupName)
        {
            if (ftp == null || string.IsNullOrWhiteSpace(groupName)) return false;
            var groups = await ftp.GetGroups(client);
            Debug.WriteLine($"[ExistenceChecks] Checked {groups.Count} groups for '{groupName}'");
            return groups.Any(g => g.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
