using glFTPd_Commander.Windows;
using System.Windows;
using System.Windows.Controls;


namespace glFTPd_Commander.Windows
{
    public partial class AddGroupWindow : BaseWindow
    {
        // Public properties to safely access the values
        public string GroupName => txtGroupName.Text;
        public string Description => txtDescription.Text;

        public AddGroupWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) => txtGroupName.Focus();
        }

        private void InputField_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            btnAdd.IsEnabled = !string.IsNullOrWhiteSpace(txtGroupName.Text) &&
                              !string.IsNullOrWhiteSpace(txtDescription.Text);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtGroupName.Text))
            {
                MessageBox.Show("Please enter a group name",
                              "Validation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                txtGroupName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(txtDescription.Text))
            {
                MessageBox.Show("Please enter a description",
                              "Validation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                txtDescription.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}