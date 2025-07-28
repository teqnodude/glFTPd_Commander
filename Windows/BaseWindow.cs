using System.Windows;
using System.Windows.Input;

namespace glFTPd_Commander.Windows
{
    public class BaseWindow : Window
    {
        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key == Key.Escape)
            {
                // Set DialogResult only for modal windows
                if (IsModal())
                    DialogResult = false;

                Close();
            }
        }

        private bool IsModal()
        {
            return Owner != null && !ShowInTaskbar;
        }
    }
}
