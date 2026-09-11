using System.Text;

namespace DevDesk.Infrastructure.Runner.Native;

/// <summary>
/// Canonical Windows command-line argument encoder and serializer.
/// Strictly implements the standard Windows CommandLineToArgvW escaping algorithm:
/// - Arguments with spaces or tabs are quoted.
/// - Backslashes preceding a double quote are doubled: 2N + 1 backslashes followed by quote.
/// - Backslashes not preceding a double quote are emitted literally: N backslashes.
/// - Trailing backslashes at the end of a quoted argument are doubled: 2N backslashes before closing quote.
/// - Empty arguments are represented as "".
/// </summary>
internal static class WindowsCommandLineSerializer
{
    public static string FormatCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        sb.Append(EncodeArgument(executablePath));

        foreach (var arg in arguments)
        {
            sb.Append(' ');
            sb.Append(EncodeArgument(arg));
        }

        return sb.ToString();
    }

    public static string FormatCmdShimCommandLine(string comSpec, string shimPath, IReadOnlyList<string> arguments)
    {
        // On Windows, executing a .cmd batch file via cmd.exe with UseShellExecute=false requires:
        // cmd.exe /d /s /c "<shimPath> <args...>"
        // where cmd.exe /s strips the outer quotes and interprets the inner tokens.
        var innerTokens = new StringBuilder();
        innerTokens.Append(EncodeArgument(shimPath));

        foreach (var arg in arguments)
        {
            innerTokens.Append(' ');
            innerTokens.Append(EncodeArgument(arg));
        }

        return $"{EncodeArgument(comSpec)} /d /s /c \"{innerTokens}\"";
    }

    public static string EncodeArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        // If argument contains no spaces, tabs, newlines, or quotes, it does not need enclosing quotes
        if (argument.IndexOfAny([' ', '\t', '\n', '\v', '\"']) == -1)
        {
            return argument;
        }

        var sb = new StringBuilder();
        sb.Append('\"');

        int consecutiveBackslashes = 0;

        for (int i = 0; i < argument.Length; i++)
        {
            char c = argument[i];

            if (c == '\\')
            {
                consecutiveBackslashes++;
            }
            else if (c == '\"')
            {
                // Emit 2N + 1 backslashes followed by quote:
                // 2N backslashes escape the preceding backslashes, and 1 backslash escapes the quote.
                sb.Append('\\', consecutiveBackslashes * 2 + 1);
                sb.Append('\"');
                consecutiveBackslashes = 0;
            }
            else
            {
                // Emit N backslashes literally followed by the non-quote character
                if (consecutiveBackslashes > 0)
                {
                    sb.Append('\\', consecutiveBackslashes);
                    consecutiveBackslashes = 0;
                }

                sb.Append(c);
            }
        }

        // Trailing backslashes before the closing quote must be doubled (2N) so they do not escape the closing quote
        if (consecutiveBackslashes > 0)
        {
            sb.Append('\\', consecutiveBackslashes * 2);
        }

        sb.Append('\"');
        return sb.ToString();
    }
}
