
using System.Windows;

namespace glFTPd_Commander.Windows
{
    public partial class CustomCommandConfigWindow : BaseWindow
    {
        public string SiteCommand => CommandBox.Text.Trim();
        public string CustomLabel => LabelBox.Text.Trim();

        public CustomCommandConfigWindow()
        {
            InitializeComponent();
            CommandBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SiteCommand))
            {
                MessageBox.Show("Please enter a SITE command.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                CommandBox.Focus();
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
