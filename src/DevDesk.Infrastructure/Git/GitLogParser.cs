using System;
using System.Globalization;
using DevDesk.Core.Git;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Parser for machine-formatted git log -1 output using NUL (%x00) delimited fields.
/// </summary>
public static class GitLogParser
{
    public static GitCommitInfo? Parse(string? logOutput)
    {
        if (string.IsNullOrWhiteSpace(logOutput))
        {
            return null;
        }

        // Expected format: %H%x00%h%x00%an%x00%ae%x00%cI%x00%s
        var fields = logOutput.Split('\0');
        if (fields.Length < 6)
        {
            return null;
        }

        var hash = fields[0].Trim();
        var shortHash = fields[1].Trim();
        var authorName = fields[2].Trim();
        var authorEmail = fields[3].Trim();
        var timestampStr = fields[4].Trim();
        var subject = fields[5].Trim();

        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        DateTimeOffset timestamp;
        if (!DateTimeOffset.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
        {
            timestamp = DateTimeOffset.UtcNow;
        }

        return new GitCommitInfo(
            Hash: hash,
            ShortHash: string.IsNullOrEmpty(shortHash) ? hash.Substring(0, Math.Min(7, hash.Length)) : shortHash,
            AuthorName: authorName,
            AuthorEmail: authorEmail,
            Timestamp: timestamp,
            Subject: subject);
    }
}
