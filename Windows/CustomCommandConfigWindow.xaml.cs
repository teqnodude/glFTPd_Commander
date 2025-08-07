using glFTPd_Commander.Utils;
using System.Windows;

namespace glFTPd_Commander.Windows
{
    public partial class CustomCommandConfigWindow : BaseWindow
    {
        public string SiteCommand => SiteCommandTextBox.Text.Trim();
        public string CustomLabel => ButtonLabelTextBox.Text.Trim();

        public CustomCommandConfigWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => SiteCommandTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputUtils.ValidateAndWarn(string.IsNullOrWhiteSpace(SiteCommand), "Please enter a SITE command.", SiteCommandTextBox)) return;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
