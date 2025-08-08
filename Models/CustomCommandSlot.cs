using System.ComponentModel;

namespace glFTPd_Commander.Models
{
    public class CustomCommandSlot : INotifyPropertyChanged
    {
        private string _buttonText = "Configure Button";
        private string? _command;

        public string ButtonText
        {
            get => _buttonText;
            set { _buttonText = value; OnPropertyChanged(nameof(ButtonText)); }
        }

        public string? Command
        {
            get => _command;
            set { _command = value; OnPropertyChanged(nameof(Command)); OnPropertyChanged(nameof(IsConfigured)); }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Command);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
