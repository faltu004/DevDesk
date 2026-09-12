using System.Text;

namespace DevDesk.Infrastructure.Commands;

/// <summary>
/// Parser implementing canonical Windows command-line argument tokenization
/// matching Windows CommandLineToArgvW behavior in reverse.
/// Supports quotes, escaped quotes, backslashes, empty arguments, spaces, and Unicode.
/// </summary>
public static class WindowsCommandLineParser
{
    /// <summary>
    /// Parses a raw command-line string into structured argument tokens.
    /// </summary>
    public static IReadOnlyList<string> ParseArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasQuotedToken = false;
        int i = 0;

        while (i < commandLine.Length)
        {
            char c = commandLine[i];

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0 || hasQuotedToken)
                {
                    results.Add(current.ToString());
                    current.Clear();
                    hasQuotedToken = false;
                }
                i++;
                continue;
            }

            if (c == '\\')
            {
                int backslashCount = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i < commandLine.Length && commandLine[i] == '\"')
                {
                    // 2N backslashes + quote => N backslashes + toggle quotation mode
                    // 2N + 1 backslashes + quote => N backslashes + literal quote
                    current.Append('\\', backslashCount / 2);
                    if (backslashCount % 2 == 0)
                    {
                        inQuotes = !inQuotes;
                        hasQuotedToken = true;
                    }
                    else
                    {
                        current.Append('\"');
                    }
                    i++;
                }
                else
                {
                    // Backslashes not preceding a double quote are emitted literally
                    current.Append('\\', backslashCount);
                }
                continue;
            }

            if (c == '\"')
            {
                inQuotes = !inQuotes;
                hasQuotedToken = true;
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        if (current.Length > 0 || hasQuotedToken)
        {
            results.Add(current.ToString());
        }

        return results;
    }
}
