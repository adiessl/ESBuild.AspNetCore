using ESBuild.AspNetCore.Tasks;

namespace ESBuild.AspNetCore.Tasks.Tests;

public sealed class CommandBuilderTests
{
    [Fact]
    public void BuildArguments_IncludesExpectedFlags()
    {
        var bundle = new EffectiveEsbuildBundle
        {
            EntryPoint = "Scripts/site.ts",
            Output = "wwwroot/js/site.js",
            Outdir = null,
            Optional = false,
            Splitting = false,
            Minify = true,
            Sourcemap = true,
            Target = "es2020",
            Format = "esm",
            Platform = "browser",
            External = ["jquery", "react"],
            Define = new Dictionary<string, string>
            {
                ["process.env.NODE_ENV"] = "\"production\"",
                ["DEBUG"] = "false",
            },
            Alias = new Dictionary<string, string>
            {
                ["@shared"] = "./Scripts/shared",
            },
            Loader = new Dictionary<string, string>
            {
                [".svg"] = "text",
            },
            PublicPath = "/static",
        };

        var arguments = EsbuildCommandBuilder.BuildArguments(bundle, "/repo/Scripts/site.ts", "/repo/wwwroot/js/site.js", null);

        Assert.Contains("/repo/Scripts/site.ts", arguments);
        Assert.Contains("--bundle", arguments);
        Assert.Contains("--outfile=/repo/wwwroot/js/site.js", arguments);
        Assert.Contains("--minify", arguments);
        Assert.Contains("--sourcemap", arguments);
        Assert.Contains("--target=es2020", arguments);
        Assert.Contains("--format=esm", arguments);
        Assert.Contains("--platform=browser", arguments);
        Assert.Contains("--external:jquery", arguments);
        Assert.Contains("--external:react", arguments);
        Assert.Contains("--define:DEBUG=false", arguments);
        Assert.Contains("--define:process.env.NODE_ENV=\"production\"", arguments);
        Assert.Contains("--alias:@shared=./Scripts/shared", arguments);
        Assert.Contains("--loader:.svg=text", arguments);
        Assert.Contains("--public-path=/static", arguments);
    }

    [Fact]
    public void BuildArguments_OmitsDisabledOptionalFlags()
    {
        var bundle = new EffectiveEsbuildBundle
        {
            EntryPoint = "Scripts/site.ts",
            Output = "wwwroot/js/site.js",
            Outdir = null,
            Optional = false,
            Splitting = false,
            Minify = false,
            Sourcemap = false,
            Target = "es2019",
            Format = "iife",
            Platform = "node",
            External = Array.Empty<string>(),
            Define = new Dictionary<string, string>(),
            Alias = new Dictionary<string, string>(),
            Loader = new Dictionary<string, string>(),
        };

        var arguments = EsbuildCommandBuilder.BuildArguments(bundle, "input.ts", "output.js", null);

        Assert.DoesNotContain("--minify", arguments);
        Assert.DoesNotContain("--sourcemap", arguments);
        Assert.DoesNotContain(arguments, static x => x.StartsWith("--alias:", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, static x => x.StartsWith("--loader:", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, static x => x.StartsWith("--public-path=", StringComparison.Ordinal));
        Assert.Contains("--target=es2019", arguments);
        Assert.Contains("--format=iife", arguments);
        Assert.Contains("--platform=node", arguments);
    }

    [Fact]
    public void FormatForLogging_QuotesValuesWithSpaces()
    {
        var command = "/tmp/esbuild";
        var arguments = new[]
        {
            "/tmp/path with spaces/input.ts",
            "--outfile=/tmp/path with spaces/output.js",
        };

        var formatted = EsbuildCommandBuilder.FormatForLogging(command, arguments);

        Assert.Contains("\"/tmp/path with spaces/input.ts\"", formatted);
        Assert.Contains("\"--outfile=/tmp/path with spaces/output.js\"", formatted);
    }

    [Fact]
    public void BuildArguments_SortsDefineFlagsDeterministically()
    {
        var bundle = new EffectiveEsbuildBundle
        {
            EntryPoint = "Scripts/site.ts",
            Output = "wwwroot/js/site.js",
            Outdir = null,
            Optional = false,
            Splitting = false,
            Minify = false,
            Sourcemap = false,
            Target = "es2020",
            Format = "esm",
            Platform = "browser",
            External = Array.Empty<string>(),
            Define = new Dictionary<string, string>
            {
                ["z-last"] = "1",
                ["a-first"] = "2",
            },
            Alias = new Dictionary<string, string>(),
            Loader = new Dictionary<string, string>(),
        };

        var arguments = EsbuildCommandBuilder.BuildArguments(bundle, "input.ts", "output.js", null);
        var defineArguments = arguments.Where(static x => x.StartsWith("--define:", StringComparison.Ordinal)).ToArray();

        Assert.Equal(
            ["--define:a-first=2", "--define:z-last=1"],
            defineArguments);
    }

    [Fact]
    public void BuildArguments_SortsAliasAndLoaderFlagsDeterministically()
    {
        var bundle = new EffectiveEsbuildBundle
        {
            EntryPoint = "Scripts/site.ts",
            Output = null,
            Outdir = "wwwroot/js",
            Optional = false,
            Splitting = true,
            Minify = false,
            Sourcemap = false,
            Target = "es2020",
            Format = "esm",
            Platform = "browser",
            External = Array.Empty<string>(),
            Define = new Dictionary<string, string>(),
            Alias = new Dictionary<string, string>
            {
                ["z-last"] = "./z",
                ["a-first"] = "./a",
            },
            Loader = new Dictionary<string, string>
            {
                [".png"] = "file",
                [".css"] = "css",
            },
        };

        var arguments = EsbuildCommandBuilder.BuildArguments(bundle, "input.ts", null, "out");

        Assert.Equal(
            ["--alias:a-first=./a", "--alias:z-last=./z"],
            arguments.Where(static x => x.StartsWith("--alias:", StringComparison.Ordinal)).ToArray());
        Assert.Equal(
            ["--loader:.css=css", "--loader:.png=file"],
            arguments.Where(static x => x.StartsWith("--loader:", StringComparison.Ordinal)).ToArray());
        Assert.Contains("--outdir=out", arguments);
        Assert.Contains("--splitting", arguments);
    }
}
