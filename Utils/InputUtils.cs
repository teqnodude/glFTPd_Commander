using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Utils
{
    public static partial class InputUtils
    {
        public static void DigitsOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        public static void DigitsAndLettersOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(c => char.IsDigit(c) || char.IsLetter(c));
        }

        public static void DigitsOrNegative(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox tb)
            {
                string text = tb.Text;
                int selectionStart = tb.SelectionStart;
                int selectionLength = tb.SelectionLength;
                string newText = text.Remove(selectionStart, selectionLength)
                                     .Insert(selectionStart, e.Text);
        
                if (IntegerRegex().IsMatch(newText))
                {
                    e.Handled = false;
                    return;
                }
            }
            e.Handled = true;
        }

        public static void IpAddressInputFilter(object sender, TextCompositionEventArgs e)
        {
             e.Handled = !e.Text.All(c => char.IsDigit(c) || c == '.');
        }


        // Live filtering: only allow digits, at most two dashes, max length "YYYY-MM-DD", or a single "0"
        public static void DateOrZeroInputFilter(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox tb)
            {
                string text = tb.Text;
                int selectionStart = tb.SelectionStart;
                int selectionLength = tb.SelectionLength;

                string newText = text.Remove(selectionStart, selectionLength)
                                     .Insert(selectionStart, e.Text);

                // Allow just "0"
                if (newText == "0")
                {
                    e.Handled = false;
                    return;
                }

                // Allow digits and up to two dashes, like "YYYY-MM-DD"
                if (PartialDateRegex().IsMatch(newText))
                {
                    e.Handled = false;
                    return;
                }
            }
            e.Handled = true;
        }

        // Final validation: must be "0" or exactly "YYYY-MM-DD" (real date)
        public static bool IsValidExpiresInput(string input)
        {
            if (input == "0")
                return true;

            // Must match "YYYY-MM-DD" and be a valid date
            if (FullDateRegex().IsMatch(input))
            {
                return DateTime.TryParseExact(input, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);
            }

            return false;
        }

        public static bool IsValidGlftpdIp(string input)
        {
            if (input == "*")
                return true;
            if (input == "*@*")
                return true;
        
            string ipv4Octet = @"(\d{1,3}|\*)";
            string ipv4PartialPattern = $@"^(\*|[a-zA-Z0-9_\-]+)@({ipv4Octet}(\.{ipv4Octet}){{0,3}})$";
            string ipv6Pattern = @"^(\*|[a-zA-Z0-9_\-]+)@([0-9a-fA-F:\*]+)$";
            string wildcardPattern = @"^(\*|[a-zA-Z0-9_\-]+)@\*$";
            string identOnlyPattern = @"^(\*|[a-zA-Z0-9_\-]+)$";
            string ipOnlyPattern = $@"^({ipv4Octet}(\.{ipv4Octet}){{0,3}}|([0-9a-fA-F]{{0,4}}:)+[0-9a-fA-F]{{0,4}}|\*)$";
        
            // Validate partial IPv4, but check against all-* wildcards
            var ipv4Match = Regex.Match(input, ipv4PartialPattern);
            if (ipv4Match.Success)
            {
                var atIndex = input.IndexOf('@');
                var ipPart = input[(atIndex + 1)..];
                // Split and check if ALL octets are "*" (max 4 octets)
                var octets = ipPart.Split('.');
                if (octets.All(o => o == "*"))
                    return false; // Disallow *@*.*.*.* and similar
                return true;
            }
        
            return Regex.IsMatch(input, ipv6Pattern)
                || Regex.IsMatch(input, wildcardPattern)
                || Regex.IsMatch(input, identOnlyPattern)
                || Regex.IsMatch(input, ipOnlyPattern);
        }

        public static bool IsGlftpdIpAddError(string result) =>
            !string.IsNullOrEmpty(result) &&
            (result.Contains("not added", StringComparison.OrdinalIgnoreCase)
             || result.Contains("it is not specific enough", StringComparison.OrdinalIgnoreCase));
        

        public static bool ValidateAndWarn(bool condition, string message, System.Windows.Controls.Control? controlToFocus = null)
        {
            if (condition)
            {
                System.Windows.MessageBox.Show(message, "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                controlToFocus?.Focus();
                if (controlToFocus is System.Windows.Controls.TextBox tb)
                    tb.SelectAll();
                return true;
            }
            return false;
        }

        public static bool IsValidTimeframe(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false; // Must not be empty (since glFTPd always requires a value)
        
            // Allow "HH:mm-HH:mm" style (e.g., 00:00-00:00, 08:00-17:00)
            if (TimeframeRangeRegex().IsMatch(input))
            {
                var parts = input.Split('-', 2);
                return TimeSpan.TryParse(parts[0], out _) && TimeSpan.TryParse(parts[1], out _);
            }
        
            // Allow "H H" or "HH HH" (e.g., 8 17)
            var partsSpace = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partsSpace.Length == 2 &&
                int.TryParse(partsSpace[0], out int startInt) &&
                int.TryParse(partsSpace[1], out int endInt) &&
                startInt >= 0 && startInt <= 23 && endInt >= 0 && endInt <= 23)
            {
                return true;
            }
        
            return false;
        }



        [GeneratedRegex(@"^-?\d+$")]
        private static partial Regex IntegerRegex();
        
        [GeneratedRegex(@"^\d{0,4}-?\d{0,2}-?\d{0,2}$")]
        private static partial Regex PartialDateRegex();
        
        [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
        private static partial Regex FullDateRegex();
        
        [GeneratedRegex(@"^\d{2}:\d{2}-\d{2}:\d{2}$")]
        private static partial Regex TimeframeRangeRegex();

    }
}
