using Pastel;

namespace TwitterSky.Utilities
{
    /// <summary>
    /// Utility class for printing colored messages to the command line.
    /// </summary>
    public class CmdUtil
    {
        private bool _isVerboseEnabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="CmdUtil"/> class.
        /// </summary>
        /// <param name="isVerboseEnabled">If set to <c>true</c>, verbose messages will be printed.</param>
        public CmdUtil(bool isVerboseEnabled)
        {
            _isVerboseEnabled = isVerboseEnabled;
        }

        /// <summary>
        /// Prints a warning message in yellow color.
        /// </summary>
        /// <param name="message">The warning message to print.</param>
        /// <param name="isVerboseOnly">If set to <c>true</c>, the message will only be printed if verbose mode is enabled.</param>
        public void PrintWarning(string message, bool isVerboseOnly = false)
        {
            if (!isVerboseOnly || isVerboseOnly && _isVerboseEnabled)
            {
                Console.WriteLine($"{DateTime.Now:hh:mm} {"warning:\t".Pastel(ConsoleColor.Yellow)}{message}");
            }
        }

        /// <summary>
        /// Prints an error message in red color.
        /// </summary>
        /// <param name="message">The error message to print.</param>
        /// <param name="isVerboseOnly">If set to <c>true</c>, the message will only be printed if verbose mode is enabled.</param>
        public void PrintError(string message)
        {
            Console.WriteLine($"{DateTime.Now:hh:mm} {"error:\t\t".Pastel(ConsoleColor.Red)}{message}");
        }

        /// <summary>
        /// Prints a success message in green color.
        /// </summary>
        /// <param name="message">The success message to print.</param>
        /// <param name="isVerboseOnly">If set to <c>true</c>, the message will only be printed if verbose mode is enabled.</param>
        public void PrintSuccess(string message, bool isVerboseOnly = false)
        {
            if (!isVerboseOnly || isVerboseOnly && _isVerboseEnabled)
            {
                Console.WriteLine($"{DateTime.Now:hh:mm} {"success:\t".Pastel(ConsoleColor.Green)}{message}");
            }
        }

        /// <summary>
        /// Prints an informational message in cyan color.
        /// </summary>
        /// <param name="message">The informational message to print.</param>
        /// <param name="isVerboseOnly">If set to <c>true</c>, the message will only be printed if verbose mode is enabled.</param>
        public void PrintInfo(string message, bool isVerboseOnly = false)
        {
            if (!isVerboseOnly || isVerboseOnly && _isVerboseEnabled)
            {
                Console.WriteLine($"{DateTime.Now:hh:mm} {"info:\t\t".Pastel(ConsoleColor.Cyan)}{message}");
            }
        }
    }
}
