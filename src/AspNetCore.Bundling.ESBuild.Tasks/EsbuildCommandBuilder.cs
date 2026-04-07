using System.Diagnostics;

namespace AspNetCore.Bundling.ESBuild.Tasks;

internal static class EsbuildCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(
        EffectiveEsbuildBundle bundle,
        string entryPoint,
        string? output,
        string? outdir)
    {
        var arguments = new List<string>
        {
            entryPoint,
            "--bundle",
            $"--target={bundle.Target}",
            $"--format={bundle.Format}",
            $"--platform={bundle.Platform}",
        };

        if (!string.IsNullOrWhiteSpace(output))
        {
            arguments.Add($"--outfile={output}");
        }

        if (!string.IsNullOrWhiteSpace(outdir))
        {
            arguments.Add($"--outdir={outdir}");
        }

        if (bundle.Minify)
        {
            arguments.Add("--minify");
        }

        if (bundle.Sourcemap)
        {
            arguments.Add("--sourcemap");
        }

        if (bundle.Splitting)
        {
            arguments.Add("--splitting");
        }

        foreach (var external in bundle.External)
        {
            arguments.Add($"--external:{external}");
        }

        foreach (var define in bundle.Define.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            arguments.Add($"--define:{define.Key}={define.Value}");
        }

        foreach (var alias in bundle.Alias.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            arguments.Add($"--alias:{alias.Key}={alias.Value}");
        }

        foreach (var loader in bundle.Loader.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            arguments.Add($"--loader:{loader.Key}={loader.Value}");
        }

        if (!string.IsNullOrWhiteSpace(bundle.PublicPath))
        {
            arguments.Add($"--public-path={bundle.PublicPath}");
        }

        return arguments;
    }

    public static void Apply(ProcessStartInfo startInfo, IEnumerable<string> arguments)
    {
        startInfo.Arguments = BuildCommandLine(arguments);
    }

    public static string FormatForLogging(string command, IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            new[] { QuoteIfNeeded(command) }.Concat(arguments.Select(QuoteIfNeeded)));
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string BuildCommandLine(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteIfNeeded));
    }
}
