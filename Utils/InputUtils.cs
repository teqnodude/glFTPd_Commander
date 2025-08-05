using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace glFTPd_Commander.Utils
{
    public static partial class InputUtils
    {
        // Use as event handler for TextCompositionEventArgs
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

        [GeneratedRegex(@"^-?\d+$")]
        private static partial Regex IntegerRegex();
        
        [GeneratedRegex(@"^\d{0,4}-?\d{0,2}-?\d{0,2}$")]
        private static partial Regex PartialDateRegex();
        
        [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
        private static partial Regex FullDateRegex();
    }
}
