using System;
using DevDesk.Infrastructure.Git;
using Xunit;

namespace DevDesk.Tests.Git;

public class GitLogParserTests
{
    [Fact]
    public void Parse_ValidLogOutput_ExtractsAllFields()
    {
        var input = "56c0e0a123456789abcdef0123456789abcdef01\056c0e0a\0Jane Developer\0jane@dev.local\02026-09-11T14:30:00+05:30\0feat(processes): add read-only process manager\0";
        var result = GitLogParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("56c0e0a123456789abcdef0123456789abcdef01", result.Hash);
        Assert.Equal("56c0e0a", result.ShortHash);
        Assert.Equal("Jane Developer", result.AuthorName);
        Assert.Equal("jane@dev.local", result.AuthorEmail);
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T14:30:00+05:30"), result.Timestamp);
        Assert.Equal("feat(processes): add read-only process manager", result.Subject);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(GitLogParser.Parse(null));
        Assert.Null(GitLogParser.Parse(string.Empty));
        Assert.Null(GitLogParser.Parse("   "));
    }

    [Fact]
    public void Parse_IncompleteFields_ReturnsNull()
    {
        var input = "hash\0shorthash\0author\0email\0";
        Assert.Null(GitLogParser.Parse(input));
    }

    [Fact]
    public void Parse_InvalidTimestamp_FallsBackGracefully()
    {
        var input = "hash\0shorthash\0author\0email\0not-a-date\0subject\0";
        var result = GitLogParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("hash", result.Hash);
        Assert.True(result.Timestamp <= DateTimeOffset.UtcNow);
    }
}
