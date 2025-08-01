using glFTPd_Commander.Windows;
using System.Windows;


namespace glFTPd_Commander.Windows
{
    public partial class AddIpWindow : BaseWindow
    {
        public string IPAddress => ipTextBox.Text;

        public AddIpWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) => ipTextBox.Focus();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ipTextBox.Text))
            {
                MessageBox.Show("Please enter an IP address", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
