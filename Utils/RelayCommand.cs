using System;
using System.Windows.Input;

namespace glFTPd_Commander.Utils
{
    public class RelayCommand<T>(Action<T> execute, Predicate<T>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute?.Invoke((T)parameter!) ?? true;
        public void Execute(object? parameter) => execute((T)parameter!);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value!; }
            remove { CommandManager.RequerySuggested -= value!; }
        }
    }
}
