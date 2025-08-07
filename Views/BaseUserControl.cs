using System.Windows;
using System.Windows.Controls;

namespace glFTPd_Commander.Views
{
    public class BaseUserControl : UserControl
    {
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
                        tb.Select(tb.Text.Length, 0); // No selection, caret at end
                        tb.Focus();
                        System.Diagnostics.Debug.WriteLine($"[Debug] Caret moved to end for {tb.Name}");
                    }),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle
                );
            }
        }
    }
}
