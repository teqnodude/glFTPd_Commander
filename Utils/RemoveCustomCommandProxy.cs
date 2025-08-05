using System.Windows;
using System.Windows.Input;

namespace glFTPd_Commander.Utils
{
    public class RemoveCustomCommandProxy : Freezable
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(RemoveCustomCommandProxy));

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new RemoveCustomCommandProxy();
        }
    }
}
