using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace glFTPd_Commander.Windows
{
    public class BaseWindow : Window
    {
        protected override void OnPreviewKeyDown(KeyEventArgs e)
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

        protected void SelectAllText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        protected void MoveCaretToEnd(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        tb.Select(tb.Text.Length, 0);
                        tb.Focus();
                        System.Diagnostics.Debug.WriteLine($"[Debug] Caret moved to end for {tb.Name}");
                    }),
                    DispatcherPriority.ApplicationIdle
                );
            }
        }

        protected static void AttachPasswordReveal(PasswordBox passwordBox, TextBox visibleTextBox)
        {
            visibleTextBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;

            passwordBox.GotFocus += (s, e) =>
            {
                RevealPassword(passwordBox, visibleTextBox, show: true);
            };

            visibleTextBox.LostFocus += (s, e) =>
            {
                RevealPassword(passwordBox, visibleTextBox, show: false);
            };
        }

        protected static void RevealPassword(PasswordBox passwordBox, TextBox visibleTextBox, bool show)
        {
            if (show)
            {
                visibleTextBox.Text = passwordBox.Password;
                visibleTextBox.Visibility = Visibility.Visible;
                passwordBox.Visibility = Visibility.Collapsed;
                visibleTextBox.Focus();
                visibleTextBox.SelectAll();
                Debug.WriteLine("[BaseWindow] RevealPassword(show=true) -> showing visibleTextBox, copying from passwordBox.");
            }
            else
            {
                passwordBox.Password = visibleTextBox.Text;
                passwordBox.Visibility = Visibility.Visible;
                visibleTextBox.Visibility = Visibility.Collapsed;
                Debug.WriteLine("[BaseWindow] RevealPassword(show=false) -> hiding visibleTextBox, copying back to passwordBox.");
            }
        }

        protected static string GetPassword(PasswordBox passwordBox, TextBox visibleTextBox)
        {
            var isVisible = visibleTextBox.Visibility == Visibility.Visible;
            var value = isVisible ? visibleTextBox.Text : passwordBox.Password;
            Debug.WriteLine($"[BaseWindow] GetPassword -> using {(isVisible ? "visibleTextBox" : "passwordBox")} value (len={value?.Length ?? 0}).");
            return value ?? string.Empty;
        }

    }
}

