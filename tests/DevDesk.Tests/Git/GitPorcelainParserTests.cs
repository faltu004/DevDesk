using DevDesk.Infrastructure.Git;
using Xunit;

namespace DevDesk.Tests.Git;

public class GitPorcelainParserTests
{
    [Fact]
    public void Parse_EmptyOrNull_ReturnsCleanUnknown()
    {
        var result = GitPorcelainParser.Parse(null);
        Assert.Equal("unknown", result.Branch.BranchName);
        Assert.True(result.WorkingTree.IsClean);

        var result2 = GitPorcelainParser.Parse(string.Empty);
        Assert.Equal("unknown", result2.Branch.BranchName);
        Assert.True(result2.WorkingTree.IsClean);
    }

    [Fact]
    public void Parse_CleanRepository_ReturnsZeroCounts()
    {
        var input = "# branch.oid 56c0e0a123456789\0# branch.head main\0# branch.upstream origin/main\0# branch.ab +0 -0\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal("main", result.Branch.BranchName);
        Assert.Equal("origin/main", result.Branch.UpstreamBranch);
        Assert.Equal(0, result.Branch.AheadCount);
        Assert.Equal(0, result.Branch.BehindCount);
        Assert.False(result.Branch.IsDetached);
        Assert.False(result.Branch.IsUnborn);
        Assert.True(result.WorkingTree.IsClean);
        Assert.Equal(0, result.WorkingTree.StagedCount);
        Assert.Equal(0, result.WorkingTree.UnstagedCount);
        Assert.Equal(0, result.WorkingTree.UntrackedCount);
        Assert.Equal(0, result.WorkingTree.ConflictedCount);
    }

    [Fact]
    public void Parse_StagedFile_IncrementsStagedCount()
    {
        var input = "# branch.head main\01 M. N... 100644 100644 100644 hash1 hash2 file.txt\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.StagedCount);
        Assert.Equal(0, result.WorkingTree.UnstagedCount);
        Assert.True(result.WorkingTree.HasModifications);
    }

    [Fact]
    public void Parse_UnstagedFile_IncrementsUnstagedCount()
    {
        var input = "# branch.head main\01 .M N... 100644 100644 100644 hash1 hash2 file.txt\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(0, result.WorkingTree.StagedCount);
        Assert.Equal(1, result.WorkingTree.UnstagedCount);
        Assert.True(result.WorkingTree.HasModifications);
    }

    [Fact]
    public void Parse_StagedAndUnstagedSameFile_CountsBothAccurately()
    {
        var input = "# branch.head main\01 MM N... 100644 100644 100644 hash1 hash2 both.txt\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.StagedCount);
        Assert.Equal(1, result.WorkingTree.UnstagedCount);
    }

    [Fact]
    public void Parse_UntrackedFile_IncrementsUntrackedCount()
    {
        var input = "# branch.head main\0? newfile.cs\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.UntrackedCount);
        Assert.Equal(0, result.WorkingTree.StagedCount);
        Assert.Equal(0, result.WorkingTree.UnstagedCount);
    }

    [Fact]
    public void Parse_ConflictedFile_IncrementsConflictedCount()
    {
        var input = "# branch.head main\0u UU N... 100644 100644 100644 100644 hash1 hash2 hash3 conflicted.cs\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.ConflictedCount);
        Assert.Equal(0, result.WorkingTree.StagedCount);
        Assert.Equal(0, result.WorkingTree.UnstagedCount);
    }

    [Fact]
    public void Parse_RenamedFile_ConsumesOrigPathInZMode()
    {
        var input = "# branch.head main\02 R. N... 100644 100644 100644 hash1 hash2 R100 newname.txt\0oldname.txt\0? extra.txt\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.StagedCount);
        Assert.Equal(0, result.WorkingTree.UnstagedCount);
        Assert.Equal(1, result.WorkingTree.UntrackedCount);
    }

    [Fact]
    public void Parse_DetachedHead_DetectsDetached()
    {
        var input = "# branch.oid a1b2c3d4e5f6\0# branch.head (detached)\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.True(result.Branch.IsDetached);
        Assert.Equal("(detached)", result.Branch.BranchName);
    }

    [Fact]
    public void Parse_UnbornBranch_DetectsUnborn()
    {
        var input = "# branch.oid (initial)\0# branch.head feat-branch\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.True(result.Branch.IsUnborn);
        Assert.Equal("feat-branch", result.Branch.BranchName);
    }

    [Fact]
    public void Parse_NoUpstream_LeavesUpstreamNull()
    {
        var input = "# branch.oid 123456\0# branch.head main\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.False(result.Branch.HasUpstream);
        Assert.Null(result.Branch.UpstreamBranch);
        Assert.Null(result.Branch.AheadCount);
        Assert.Null(result.Branch.BehindCount);
    }

    [Fact]
    public void Parse_AheadOnly_SetsAheadCount()
    {
        var input = "# branch.head main\0# branch.upstream origin/main\0# branch.ab +3 -0\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(3, result.Branch.AheadCount);
        Assert.Equal(0, result.Branch.BehindCount);
    }

    [Fact]
    public void Parse_BehindOnly_SetsBehindCount()
    {
        var input = "# branch.head main\0# branch.upstream origin/main\0# branch.ab +0 -5\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(0, result.Branch.AheadCount);
        Assert.Equal(5, result.Branch.BehindCount);
    }

    [Fact]
    public void Parse_AheadAndBehind_SetsBothCounts()
    {
        var input = "# branch.head main\0# branch.upstream origin/main\0# branch.ab +2 -4\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(2, result.Branch.AheadCount);
        Assert.Equal(4, result.Branch.BehindCount);
    }

    [Fact]
    public void Parse_UnicodeAndSpacesInFilenames_ParsedCorrectly()
    {
        var input = "# branch.head main\01 .M N... 100644 100644 100644 h1 h2 src/My Documents/Special File with Spaces.cs\0? 測試/文件名.txt\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.Equal(1, result.WorkingTree.UnstagedCount);
        Assert.Equal(1, result.WorkingTree.UntrackedCount);
    }

    [Fact]
    public void Parse_MalformedPorcelainOutput_DoesNotThrow()
    {
        var input = "unexpected string without standard prefix\01 incomplete\0### branch.unknown\0\0\0";
        var result = GitPorcelainParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("unknown", result.Branch.BranchName);
        Assert.Equal(0, result.WorkingTree.StagedCount);
    }
}
