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
            }
        }
