using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;

namespace glFTPd_Commander
{
    public partial class CertificateWindow : Window
    {
        public bool IsApproved { get; private set; }
        public bool RememberDecision { get; private set; }
        public string CertificateThumbprint { get; }
        public string CertificateSubject { get; }

        public CertificateWindow(X509Certificate certificate)
        {
            InitializeComponent();

            try
            {
                using (var cert2 = new X509Certificate2(certificate))
                {
                    CertificateThumbprint = cert2.Thumbprint;
                    CertificateSubject = cert2.Subject;
                    DisplayCertificateDetails(cert2);
                    return;
                }
            }
            catch
            {
                CertificateThumbprint = certificate.GetCertHashString();
                CertificateSubject = certificate.Subject;
                DisplayBasicCertificateInfo(certificate);
            }
        }

        private void DisplayCertificateDetails(X509Certificate2 cert)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SSL Certificate Details ===");
            sb.AppendLine($"Subject: {cert.Subject}");
            sb.AppendLine($"Issuer: {cert.Issuer}");
            sb.AppendLine($"Valid From: {cert.NotBefore.ToShortDateString()}");
            sb.AppendLine($"Valid To: {cert.NotAfter.ToShortDateString()}");
            sb.AppendLine($"Thumbprint: {cert.Thumbprint}");
            sb.AppendLine($"Serial Number: {cert.SerialNumber}");

            txtCertDetails.Text = sb.ToString();
        }

        private void DisplayBasicCertificateInfo(X509Certificate cert)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SSL Certificate Details ===");
            sb.AppendLine($"Subject: {cert.Subject}");
            sb.AppendLine($"Issuer: {cert.Issuer}");
            sb.AppendLine($"Thumbprint: {CertificateThumbprint}");
            sb.AppendLine();
            sb.AppendLine("Note: Some details unavailable due to certificate format restrictions");

            txtCertDetails.Text = sb.ToString();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            IsApproved = true;
            RememberDecision = chkRemember.IsChecked ?? false;
            DialogResult = true;
            Close();
        }

        private void Reject_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}