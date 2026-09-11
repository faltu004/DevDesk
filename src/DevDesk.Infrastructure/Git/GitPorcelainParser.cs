using System;
using System.Collections.Generic;
using System.Globalization;
using DevDesk.Core.Git;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// High-performance, memory-safe parser for Git status --porcelain=v2 -z output.
/// Accurately parses branch topology, tracking upstream, ahead/behind counters,
/// and working tree classifications (staged, modified, untracked, conflicts)
/// supporting spaces, Unicode, renames, and unmerged records.
/// </summary>
public static class GitPorcelainParser
{
    public sealed record ParseResult(
        GitBranchInfo Branch,
        GitWorkingTreeSummary WorkingTree);

    public static ParseResult Parse(string? porcelainOutput)
    {
        if (string.IsNullOrWhiteSpace(porcelainOutput))
        {
            return new ParseResult(
                new GitBranchInfo(BranchName: "unknown"),
                new GitWorkingTreeSummary());
        }

        string branchName = "unknown";
        string? upstreamBranch = null;
        int? aheadCount = null;
        int? behindCount = null;
        bool isDetached = false;
        bool isUnborn = false;

        int stagedCount = 0;
        int unstagedCount = 0;
        int untrackedCount = 0;
        int conflictedCount = 0;

        // In porcelain v2 -z, records are delimited by '\0'
        var tokens = porcelainOutput.Split('\0');

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            // Headers may be individual NUL tokens or contain newline-separated lines
            if (token.StartsWith('#'))
            {
                var lines = token.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    ParseHeaderLine(
                        line,
                        ref branchName,
                        ref upstreamBranch,
                        ref aheadCount,
                        ref behindCount,
                        ref isDetached,
                        ref isUnborn);
                }
                continue;
            }

            // Type 1: Ordinary changed entry
            // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
            if (token.StartsWith("1 ", StringComparison.Ordinal))
            {
                var parts = token.Split(' ', 9);
                if (parts.Length >= 9)
                {
                    var xy = parts[1];
                    if (xy.Length >= 2)
                    {
                        if (xy[0] != '.') stagedCount++;
                        if (xy[1] != '.') unstagedCount++;
                    }
                }
                continue;
            }

            // Type 2: Renamed / copied entry
            // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <Xscore> <path>\0<origPath>
            if (token.StartsWith("2 ", StringComparison.Ordinal))
            {
                var parts = token.Split(' ', 10);
                if (parts.Length >= 10)
                {
                    var xy = parts[1];
                    if (xy.Length >= 2)
                    {
                        if (xy[0] != '.') stagedCount++;
                        if (xy[1] != '.') unstagedCount++;
                    }
                }

                // In -z mode, origPath is in the immediately following NUL-delimited token
                if (i + 1 < tokens.Length && !tokens[i + 1].StartsWith('#') && !tokens[i + 1].StartsWith('1') && !tokens[i + 1].StartsWith('2') && !tokens[i + 1].StartsWith('u') && !tokens[i + 1].StartsWith('?') && !tokens[i + 1].StartsWith('!'))
                {
                    i++; // Consume origPath
                }
                continue;
            }

            // Type u: Unmerged / Conflicted entry
            // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
            if (token.StartsWith("u ", StringComparison.Ordinal))
            {
                conflictedCount++;
                continue;
            }

            // Type ?: Untracked entry
            // ? <path>
            if (token.StartsWith("? ", StringComparison.Ordinal))
            {
                untrackedCount++;
                continue;
            }

            // Type !: Ignored entry
            if (token.StartsWith("! ", StringComparison.Ordinal))
            {
                continue;
            }
        }

        var branchInfo = new GitBranchInfo(
            BranchName: branchName,
            UpstreamBranch: upstreamBranch,
            AheadCount: aheadCount,
            BehindCount: behindCount,
            IsDetached: isDetached,
            IsUnborn: isUnborn);

        var workingTree = new GitWorkingTreeSummary(
            StagedCount: stagedCount,
            UnstagedCount: unstagedCount,
            UntrackedCount: untrackedCount,
            ConflictedCount: conflictedCount);

        return new ParseResult(branchInfo, workingTree);
    }

    private static void ParseHeaderLine(
        string line,
        ref string branchName,
        ref string? upstreamBranch,
        ref int? aheadCount,
        ref int? behindCount,
        ref bool isDetached,
        ref bool isUnborn)
    {
        if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
        {
            var oid = line["# branch.oid ".Length..].Trim();
            if (oid.Equals("(initial)", StringComparison.OrdinalIgnoreCase))
            {
                isUnborn = true;
            }
        }
        else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
        {
            var head = line["# branch.head ".Length..].Trim();
            if (head.Equals("(detached)", StringComparison.OrdinalIgnoreCase))
            {
                isDetached = true;
                branchName = "(detached)";
            }
            else
            {
                branchName = head;
            }
        }
        else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
        {
            var upstream = line["# branch.upstream ".Length..].Trim();
            if (!string.IsNullOrEmpty(upstream))
            {
                upstreamBranch = upstream;
            }
        }
        else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
        {
            // Format: +<ahead> -<behind>
            var ab = line["# branch.ab ".Length..].Trim();
            var parts = ab.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var aheadPart = parts[0].TrimStart('+');
                var behindPart = parts[1].TrimStart('-');

                if (int.TryParse(aheadPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ahead))
                {
                    aheadCount = ahead;
                }
                if (int.TryParse(behindPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var behind))
                {
                    behindCount = behind;
                }
            }
        }
    }
}
