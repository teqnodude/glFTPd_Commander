using glFTPd_Commander.Services;
using System.Windows;

namespace glFTPd_Commander
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            EncryptionKeyManager.Initialize();
            System.Windows.Application.Current.ThemeMode = ThemeMode.Dark;
        }
    }
}
