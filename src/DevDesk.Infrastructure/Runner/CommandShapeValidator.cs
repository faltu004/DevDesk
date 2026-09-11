using System.Text;
using System.Text.RegularExpressions;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Result of command parsing and allowlist validation.
/// </summary>
internal sealed record ValidatedCommand
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string Tool { get; init; } = string.Empty;
    public bool IsCmdShim { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public static ValidatedCommand Success(string tool, bool isCmdShim, IReadOnlyList<string> arguments) =>
        new() { IsValid = true, Tool = tool, IsCmdShim = isCmdShim, Arguments = arguments };

    public static ValidatedCommand Failed(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Strictly validates and tokenizes DeveloperProject.RunCommand strings according to
/// Phase 7 allowed command shapes, preventing shell injection and arbitrary execution.
/// Differentiates direct native executables (allowing valid path characters) from cmd.exe shims.
/// </summary>
internal static class CommandShapeValidator
{
    private static readonly char[] ForbiddenShimShellChars = { '&', '|', '<', '>', '^', '%', '!', ';', '\n', '\r' };

    private static readonly HashSet<string> ShellOperatorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "&", "&&", "|", "||", ";", ";;", ">", ">>", "<", "<<", "^"
    };

    private static readonly HashSet<string> BlockedPackageCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "install", "i", "add", "remove", "rm", "uninstall", "update", "up",
        "publish", "exec", "dlx", "create", "init", "upgrade", "audit", "link", "unlink"
    };

    private static readonly Regex ValidScriptNameRegex = new(@"^[a-zA-Z0-9_\-:\@\/\.]+$", RegexOptions.Compiled);

    public static ValidatedCommand ValidateAndParse(string? rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return ValidatedCommand.Failed("Run command is empty or not configured.");
        }

        // 1. Forbid raw newline and null characters across all command types
        if (rawCommand.Contains('\n') || rawCommand.Contains('\r') || rawCommand.Contains('\0'))
        {
            return ValidatedCommand.Failed("Run command cannot contain newline or null characters.");
        }

        // 2. Tokenize respecting quotes
        var tokens = Tokenize(rawCommand.Trim());
        if (tokens.Count == 0)
        {
            return ValidatedCommand.Failed("Run command contains no executable tokens.");
        }

        var tool = tokens[0].ToLowerInvariant();

        // Strip .exe / .cmd suffix if user explicitly wrote it (e.g. dotnet.exe -> dotnet)
        if (tool.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || tool.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            tool = tool[..^4];
        }

        return tool switch
        {
            "dotnet" => ValidateDotnet(tokens),
            "bun" => ValidateBun(tokens),
            "npm" => ValidateShim(rawCommand, tokens, ValidateNpm),
            "pnpm" => ValidateShim(rawCommand, tokens, ValidatePnpm),
            "yarn" => ValidateShim(rawCommand, tokens, ValidateYarn),
            _ => ValidatedCommand.Failed(
                $"Command tool '{tokens[0]}' is not permitted in Phase 7. DevDesk currently supports dotnet, npm, pnpm, yarn, and bun run commands.")
        };
    }

    private static ValidatedCommand ValidateShim(
        string rawCommand,
        IReadOnlyList<string> tokens,
        Func<IReadOnlyList<string>, ValidatedCommand> validator)
    {
        // For Windows .cmd shims (npm, pnpm, yarn), cmd.exe is the interpreter.
        // Retain the strictest shell-safety policy forbidding all shell metacharacters.
        if (rawCommand.IndexOfAny(ForbiddenShimShellChars) >= 0)
        {
            return ValidatedCommand.Failed(
                "Command contains forbidden shell operators (&, |, <, >, ^, %, !, ;). Shell chaining and redirection are strictly prohibited.");
        }

        return validator(tokens);
    }

    private static ValidatedCommand ValidateDotnet(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return ValidatedCommand.Failed("Incomplete dotnet command. Phase 7 requires 'dotnet run'.");
        }

        var subcommand = tokens[1].ToLowerInvariant();
        if (subcommand != "run")
        {
            return ValidatedCommand.Failed(
                $"Unsupported dotnet subcommand '{tokens[1]}'. Phase 7 only supports 'dotnet run' project execution.");
        }

        // Check for shell chaining/redirection tokens passed to direct native execution
        foreach (var token in tokens)
        {
            if (ShellOperatorTokens.Contains(token))
            {
                return ValidatedCommand.Failed(
                    $"Command contains shell operator token '{token}'. Shell chaining and redirection are strictly prohibited.");
            }
        }

        // All tokens after "dotnet" are valid arguments
        var args = tokens.Skip(1).ToList();
        return ValidatedCommand.Success("dotnet", isCmdShim: false, args);
    }

    private static ValidatedCommand ValidateNpm(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return ValidatedCommand.Failed("Incomplete npm command. Expected 'npm run <script>' or 'npm start'.");
        }

        var sub = tokens[1].ToLowerInvariant();

        if (sub == "start" || sub == "test")
        {
            var args = tokens.Skip(1).ToList();
            return ValidatedCommand.Success("npm", isCmdShim: true, args);
        }

        if (sub == "run")
        {
            if (tokens.Count < 3)
            {
                return ValidatedCommand.Failed("Missing script name for 'npm run'.");
            }

            var script = tokens[2];
            if (!ValidScriptNameRegex.IsMatch(script))
            {
                return ValidatedCommand.Failed($"Script name '{script}' contains invalid characters.");
            }

            var args = tokens.Skip(1).ToList();
            return ValidatedCommand.Success("npm", isCmdShim: true, args);
        }

        if (BlockedPackageCommands.Contains(sub))
        {
            return ValidatedCommand.Failed($"Command 'npm {tokens[1]}' is blocked. Only project run scripts may be executed.");
        }

        return ValidatedCommand.Failed($"Unsupported npm command form '{string.Join(' ', tokens)}'. Expected 'npm run <script>' or 'npm start'.");
    }

    private static ValidatedCommand ValidatePnpm(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return ValidatedCommand.Failed("Incomplete pnpm command. Expected 'pnpm run <script>' or 'pnpm <script>'.");
        }

        var sub = tokens[1].ToLowerInvariant();

        if (sub == "start" || sub == "test")
        {
            return ValidatedCommand.Success("pnpm", isCmdShim: true, tokens.Skip(1).ToList());
        }

        if (sub == "run")
        {
            if (tokens.Count < 3)
            {
                return ValidatedCommand.Failed("Missing script name for 'pnpm run'.");
            }

            if (!ValidScriptNameRegex.IsMatch(tokens[2]))
            {
                return ValidatedCommand.Failed($"Script name '{tokens[2]}' contains invalid characters.");
            }

            return ValidatedCommand.Success("pnpm", isCmdShim: true, tokens.Skip(1).ToList());
        }

        if (BlockedPackageCommands.Contains(sub))
        {
            return ValidatedCommand.Failed($"Command 'pnpm {tokens[1]}' is blocked. Only project run scripts may be executed.");
        }

        if (ValidScriptNameRegex.IsMatch(tokens[1]))
        {
            return ValidatedCommand.Success("pnpm", isCmdShim: true, tokens.Skip(1).ToList());
        }

        return ValidatedCommand.Failed($"Unsupported pnpm command form '{string.Join(' ', tokens)}'.");
    }

    private static ValidatedCommand ValidateYarn(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return ValidatedCommand.Failed("Incomplete yarn command. Expected 'yarn run <script>' or 'yarn <script>'.");
        }

        var sub = tokens[1].ToLowerInvariant();

        if (sub == "start" || sub == "test")
        {
            return ValidatedCommand.Success("yarn", isCmdShim: true, tokens.Skip(1).ToList());
        }

        if (sub == "run")
        {
            if (tokens.Count < 3)
            {
                return ValidatedCommand.Failed("Missing script name for 'yarn run'.");
            }

            if (!ValidScriptNameRegex.IsMatch(tokens[2]))
            {
                return ValidatedCommand.Failed($"Script name '{tokens[2]}' contains invalid characters.");
            }

            return ValidatedCommand.Success("yarn", isCmdShim: true, tokens.Skip(1).ToList());
        }

        if (BlockedPackageCommands.Contains(sub))
        {
            return ValidatedCommand.Failed($"Command 'yarn {tokens[1]}' is blocked. Only project run scripts may be executed.");
        }

        if (ValidScriptNameRegex.IsMatch(tokens[1]))
        {
            return ValidatedCommand.Success("yarn", isCmdShim: true, tokens.Skip(1).ToList());
        }

        return ValidatedCommand.Failed($"Unsupported yarn command form '{string.Join(' ', tokens)}'.");
    }

    private static ValidatedCommand ValidateBun(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return ValidatedCommand.Failed("Incomplete bun command. Expected 'bun run <script>' or 'bun start'.");
        }

        foreach (var token in tokens)
        {
            if (ShellOperatorTokens.Contains(token))
            {
                return ValidatedCommand.Failed(
                    $"Command contains shell operator token '{token}'. Shell chaining and redirection are strictly prohibited.");
            }
        }

        var sub = tokens[1].ToLowerInvariant();

        if (sub == "start" || sub == "test")
        {
            return ValidatedCommand.Success("bun", isCmdShim: false, tokens.Skip(1).ToList());
        }

        if (sub == "run")
        {
            if (tokens.Count < 3)
            {
                return ValidatedCommand.Failed("Missing script name for 'bun run'.");
            }

            if (!ValidScriptNameRegex.IsMatch(tokens[2]))
            {
                return ValidatedCommand.Failed($"Script name '{tokens[2]}' contains invalid characters.");
            }

            return ValidatedCommand.Success("bun", isCmdShim: false, tokens.Skip(1).ToList());
        }

        if (BlockedPackageCommands.Contains(sub))
        {
            return ValidatedCommand.Failed($"Command 'bun {tokens[1]}' is blocked. Only project run scripts may be executed.");
        }

        return ValidatedCommand.Failed($"Unsupported bun command form '{string.Join(' ', tokens)}'.");
    }

    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];

            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c is '"' or '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
