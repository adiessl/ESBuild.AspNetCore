using AspNetCore.Bundling.ESBuild.Tasks;

namespace AspNetCore.Bundling.ESBuild.Tasks.Tests;

public sealed class GeneratedFileSetTests
{
    [Fact]
    public void GetExpectedOutputs_IncludesSourceMapWhenEnabled()
    {
        var bundle = CreateBundle(output: "wwwroot/js/site.js", outdir: null, splitting: false, sourcemap: true);
        var outputs = EsbuildGeneratedFileSet.GetExpectedOutputs(bundle, "Scripts/site.ts", "wwwroot/js/site.js", null);

        Assert.Equal(
            ["wwwroot/js/site.js", "wwwroot/js/site.js.map"],
            outputs);
    }

    [Fact]
    public void GetExpectedOutputs_OnlyIncludesBundleWhenSourceMapDisabled()
    {
        var bundle = CreateBundle(output: "wwwroot/js/site.js", outdir: null, splitting: false, sourcemap: false);
        var outputs = EsbuildGeneratedFileSet.GetExpectedOutputs(bundle, "Scripts/site.ts", "wwwroot/js/site.js", null);

        Assert.Equal(["wwwroot/js/site.js"], outputs);
    }

    [Fact]
    public void GetExpectedOutputs_UsesOutdirForPrimaryBundle()
    {
        var bundle = CreateBundle(output: null, outdir: "wwwroot/js", splitting: true, sourcemap: true);
        var outputs = EsbuildGeneratedFileSet.GetExpectedOutputs(bundle, "Scripts/site.ts", null, "wwwroot/js");

        Assert.Equal(
            ["wwwroot/js/site.js", "wwwroot/js/site.js.map"],
            outputs);
    }

    [Fact]
    public void ReadOutputsFromMetafile_ReturnsAbsoluteOutputPaths()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var metafilePath = Path.Combine(workingDirectory, "test.metafile.json");

        try
        {
            File.WriteAllText(
                metafilePath,
                """
                {
                  "outputs": {
                    "wwwroot/js/site.js": {},
                    "wwwroot/js/chunk-ABC123.js": {}
                  }
                }
                """);

            var outputs = EsbuildGeneratedFileSet.ReadOutputsFromMetafile(metafilePath, workingDirectory);

            Assert.Equal(
                [
                    Path.Combine(workingDirectory, "wwwroot/js/chunk-ABC123.js"),
                    Path.Combine(workingDirectory, "wwwroot/js/site.js"),
                ],
                outputs,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static EffectiveEsbuildBundle CreateBundle(string? output, string? outdir, bool splitting, bool sourcemap)
    {
        return new EffectiveEsbuildBundle
        {
            EntryPoint = "Scripts/site.ts",
            Output = output,
            Outdir = outdir,
            Optional = false,
            Splitting = splitting,
            Minify = false,
            Sourcemap = sourcemap,
            Target = "es2020",
            Format = "esm",
            Platform = "browser",
            External = Array.Empty<string>(),
            Define = new Dictionary<string, string>(),
            Alias = new Dictionary<string, string>(),
            Loader = new Dictionary<string, string>(),
        };
    }
}
