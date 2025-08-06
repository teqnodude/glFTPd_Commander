using FluentFTP;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using glFTPd_Commander.FTP;

namespace glFTPd_Commander.Services
{
    public static class FtpBase
    {
        /// <summary>
        /// Ensures an FTP client is connected (creates/reconnects as needed).
        /// Returns a usable client or null on failure.
        /// </summary>
        public static async Task<FtpClient?> EnsureConnectedAsync(
            FtpClient? client,
            GlFtpdClient ftp,
            CancellationToken cancellationToken = default)
        {
            if (client == null || !client.IsConnected)
            {
                try
                {
                    client?.Dispose();
                    client = ftp.CreateClient();
                    await Task.Run(() => client.Connect(), cancellationToken);
                    Debug.WriteLine("[FtpBase] Disposed old client and connected new FtpClient.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FtpBase] EnsureConnectedAsync failed: {ex}");
                    client?.Dispose();
                    client = null;
                }
            }
            return (client?.IsConnected == true) ? client : null;
        }

        /// <summary>
        /// Executes an FTP command, always checking and repairing connection first.
        /// Returns (result, updated client). Handles all connection loss scenarios.
        /// </summary>
        public static async Task<(string Result, FtpClient? Client)> ExecuteFtpCommandWithReconnectAsync(
            string command,
            FtpClient? client,
            GlFtpdClient ftp,
            CancellationToken cancellationToken = default)
        {
            client = await EnsureConnectedAsync(client, ftp, cancellationToken);
            if (client == null)
            {
                Debug.WriteLine($"[FtpBase] Command '{command}' failed: lost connection.");
                return ("[ERROR] Lost connection to FTP server.", null);
            }

            try
            {
                var reply = await Task.Run(() => client.Execute(command), cancellationToken);
                var result = !string.IsNullOrWhiteSpace(reply.InfoMessages)
                    ? reply.InfoMessages
                    : reply.Message ?? string.Empty;

                Debug.WriteLine($"[FtpBase] {command} → {result}");
                return (result, client);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FtpBase] ExecuteFtpCommandWithReconnectAsync failed: {ex}");
                return ($"[ERROR] {ex.Message}", client);
            }
        }

        /// <summary>
        /// Executes an FTP command that returns a boolean success/failure.
        /// </summary>
        public static async Task<(bool Success, FtpClient? Client)> ExecuteFtpCommandOkWithReconnectAsync(
            string command,
            FtpClient? client,
            GlFtpdClient ftp,
            CancellationToken cancellationToken = default)
        {
            client = await EnsureConnectedAsync(client, ftp, cancellationToken);
            if (client == null)
            {
                Debug.WriteLine($"[FtpBase] Command '{command}' failed: lost connection.");
                return (false, null);
            }

            try
            {
                var reply = await Task.Run(() => client.Execute(command), cancellationToken);
                return (reply.Success, client);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FtpBase] ExecuteFtpCommandOkWithReconnectAsync failed: {ex}");
                return (false, client);
            }
        }

        /// <summary>
        /// Runs any custom FTP operation (with result), ensuring a working connection. Returns (result, updated client).
        /// </summary>
        public static async Task<(T Result, FtpClient? Client)> ExecuteWithConnectionAsync<T>(
            FtpClient? client,
            GlFtpdClient ftp,
            Func<FtpClient, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            client = await EnsureConnectedAsync(client, ftp, cancellationToken);
            if (client == null) return (default!, null);
        
            try
            {
                var result = await operation(client);
                return (result, client);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FtpBase] ExecuteWithConnectionAsync failed: {ex}");
                return (default!, client);
            }
        }
    }
}
