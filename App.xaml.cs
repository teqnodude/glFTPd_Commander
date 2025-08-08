using glFTPd_Commander.Services;
using glFTPd_Commander.Utils;
using System.Diagnostics;
using System.Windows;

namespace glFTPd_Commander
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            EncryptionKeyManager.Initialize();
            SettingsManager.Load();
            Application.Current.ThemeMode = ThemeMode.Dark;
            base.OnStartup(e);
        }
    }
}
