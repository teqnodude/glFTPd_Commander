using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Views
{
    public static class EmbeddedViewManager
    {
        public static void ShowEscCloseableView<T>(ContentControl container, T view, Action? onClose = null)
            where T : UIElement
        {
            container.Content = view;

            if (view is IUnselectable escAwareView)
            {
                view.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape && escAwareView.UnselectOnEsc)
                    {
                        container.Content = null;
                        onClose?.Invoke();
                    }
                };
            }

            if (view is FrameworkElement frameworkElement)
            {
                frameworkElement.Loaded += (_, _) =>
                {
                    var focusTarget = frameworkElement.FindName("usernameText") as IInputElement ?? frameworkElement;
                    focusTarget.Focus();
                    Keyboard.Focus(focusTarget);
                    //System.Diagnostics.Debug.WriteLine("[EmbeddedViewManager] Focus forced to view or first control.");
                };
            }
        }
    }

    public interface IUnselectable
    {
        bool UnselectOnEsc { get; }
    }
}
