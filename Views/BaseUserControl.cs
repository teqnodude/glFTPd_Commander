using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Views
{
    public class BaseUserControl : UserControl
    {
        protected virtual bool HandleEscape => true;
        protected virtual void OnEscape() { }
        public Action? RequestClose { get; set; }
        protected virtual string? FocusTargetName => null;

        public BaseUserControl()
        {
            Loaded += (s, e) =>
            {
                if (FocusTargetName != null)
                {
                    if (FindName(FocusTargetName) is IInputElement input)
                    {
                        input.Focus();
                        Keyboard.Focus(input);
                    }
                }
                else
                {
                    this.Focus();
                }
            };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape && HandleEscape)
            {
                OnEscape();
                RequestClose?.Invoke();
                e.Handled = true;
            }
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
