using ESBuild.AspNetCore.Tasks;

namespace ESBuild.AspNetCore.Tasks.Tests;

public sealed class CompileESBuildTests
{
    [Theory]
    [MemberData(nameof(GetRelativePathData))]
    public void GetRelativePath_CorrectlyNormalizesPaths(string rootFolder, string path, string expected)
    {
        var normalizedRoot = Path.GetFullPath(rootFolder);
        var normalizedPath = Path.GetFullPath(path);
        var normalizedExpected = Path.IsPathRooted(expected) ? Path.GetFullPath(expected) : expected;

        Assert.Equal(normalizedExpected, EsbuildGeneratedFileSet.GetRelativePath(normalizedRoot, normalizedPath));
    }

    [Theory]
    [InlineData(345, "345b")]
    [InlineData(14540, "14.2kb")]
    [InlineData(1572864, "1.5mb")]
    public void GetFormattedFileSize_FormatsSizesCorrectly(long fileSizeBytes, string expected)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        var tempFile = Path.Combine(tempFolder, "temp.js");

        try
        {
            File.WriteAllBytes(tempFile, new byte[fileSizeBytes]);
            var sizeStr = EsbuildGeneratedFileSet.GetFormattedFileSize(tempFile);
            Assert.Equal(expected, sizeStr);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    public static IEnumerable<object[]> GetRelativePathData()
    {
        var baseFolder = Path.Combine(Path.GetTempPath(), "ESBuild.AspNetCore.Tests");
        var rootFolder = Path.Combine(baseFolder, "Project");

        yield return new object[]
        {
            rootFolder,
            Path.Combine(rootFolder, "wwwroot", "js", "site.js"),
            Path.Combine("wwwroot", "js", "site.js"),
        };

        yield return new object[]
        {
            rootFolder,
            Path.Combine(rootFolder, "site.js"),
            "site.js",
        };

        var siblingFolderPath = Path.Combine(baseFolder, "ProjectOther", "file.js");
        yield return new object[]
        {
            rootFolder,
            siblingFolderPath,
            siblingFolderPath,
        };

        var unrelatedPath = Path.Combine(baseFolder, "OtherFolder", "file.js");
        yield return new object[]
        {
            rootFolder,
            unrelatedPath,
            unrelatedPath,
        };
    }
}
