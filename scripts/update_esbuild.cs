using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

return await UpdateEsbuildApp.MainAsync(args);

internal static class UpdateEsbuildApp
{
    private static readonly Dictionary<string, (string PackageName, string EntryName, string OutputName)> Runtimes = new(StringComparer.Ordinal)
    {
        ["win-x64"] = ("@esbuild/win32-x64", "package/esbuild.exe", "esbuild.exe"),
        ["win-arm64"] = ("@esbuild/win32-arm64", "package/esbuild.exe", "esbuild.exe"),
        ["linux-x64"] = ("@esbuild/linux-x64", "package/bin/esbuild", "esbuild"),
        ["linux-arm64"] = ("@esbuild/linux-arm64", "package/bin/esbuild", "esbuild"),
        ["osx-x64"] = ("@esbuild/darwin-x64", "package/bin/esbuild", "esbuild"),
        ["osx-arm64"] = ("@esbuild/darwin-arm64", "package/bin/esbuild", "esbuild"),
    };

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public static async Task<int> MainAsync(string[] args)
    {
        var actions = new[]
        {
            args.Contains("--print-current-version"),
            args.Contains("--print-latest-version"),
            args.Contains("--version"),
        }.Count(static value => value);

        if (actions != 1)
        {
            Console.Error.WriteLine("Specify exactly one action: --print-current-version, --print-latest-version, or --version <value>.");
            return 1;
        }

        if (args.Contains("--print-current-version"))
        {
            Console.WriteLine(GetCurrentUpstreamVersion());
            return 0;
        }

        if (args.Contains("--print-latest-version"))
        {
            Console.WriteLine(await GetLatestVersionAsync());
            return 0;
        }

        var versionIndex = Array.IndexOf(args, "--version");
        if (versionIndex < 0 || versionIndex == args.Length - 1 || string.IsNullOrWhiteSpace(args[versionIndex + 1]))
        {
            Console.Error.WriteLine("The --version option requires a version value.");
            return 1;
        }

        var targetVersion = args[versionIndex + 1].Trim();
        Console.WriteLine($"Updating vendored esbuild binaries to {targetVersion}");
        await UpdateRuntimesAsync(targetVersion);
        WriteVersions(targetVersion);
        Console.WriteLine("Done");
        return 0;
    }

    private static string GetCurrentUpstreamVersion()
    {
        var match = Regex.Match(
            File.ReadAllText(GetPackageProjectPath()),
            "<ESBuildUpstreamVersion>([^<]+)</ESBuildUpstreamVersion>",
            RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            throw new InvalidOperationException("Could not find ESBuildUpstreamVersion in ESBuild.AspNetCore.csproj.");
        }

        return match.Groups[1].Value.Trim();
    }

    private static async Task<string> GetLatestVersionAsync()
    {
        using var response = await Http.GetAsync("https://registry.npmjs.org/esbuild");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("dist-tags").GetProperty("latest").GetString()
            ?? throw new InvalidOperationException("The npm registry response did not contain a latest version.");
    }

    private static async Task UpdateRuntimesAsync(string version)
    {
        Directory.CreateDirectory(GetRuntimesRootPath());

        foreach (var (runtime, value) in Runtimes)
        {
            Console.WriteLine($"Downloading {value.PackageName}@{version} -> {runtime}");
            await DownloadRuntimeAsync(version, runtime, value.PackageName, value.EntryName, value.OutputName);
        }
    }

    private static async Task DownloadRuntimeAsync(string version, string runtime, string packageName, string entryName, string outputName)
    {
        var packageMetadata = await FetchPackageMetadataAsync(packageName, version);
        var tarballUrl = packageMetadata
            .GetProperty("dist")
            .GetProperty("tarball")
            .GetString()
            ?? throw new InvalidOperationException($"The npm package metadata for {packageName}@{version} did not include a tarball URL.");

        var runtimeDirectory = Path.Combine(GetRuntimesRootPath(), runtime);
        if (Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }

        Directory.CreateDirectory(runtimeDirectory);

        await using var tarballStream = await Http.GetStreamAsync(tarballUrl);
        await using var gzipStream = new GZipStream(tarballStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = tarReader.GetNextEntry()) is not null)
        {
            if (!string.Equals(entry.Name, entryName, StringComparison.Ordinal))
            {
                continue;
            }

            var outputPath = Path.Combine(runtimeDirectory, outputName);
            await using var outputStream = File.Create(outputPath);

            if (entry.DataStream is null)
            {
                throw new InvalidOperationException($"The archive entry {entryName} in {packageName}@{version} had no data stream.");
            }

            await entry.DataStream.CopyToAsync(outputStream);
            await outputStream.FlushAsync();

            if (string.Equals(outputName, "esbuild", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(outputPath);
                mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(outputPath, mode);
            }

            return;
        }

        throw new InvalidOperationException($"Could not find '{entryName}' inside {packageName}@{version}.");
    }

    private static async Task<JsonElement> FetchPackageMetadataAsync(string packageName, string version)
    {
        var packageUrl = $"https://registry.npmjs.org/{Uri.EscapeDataString(packageName)}/{version}";
        using var response = await Http.GetAsync(packageUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static void WriteVersions(string upstreamVersion)
    {
        var projectPath = GetPackageProjectPath();
        var projectText = File.ReadAllText(projectPath);

        projectText = Regex.Replace(
            projectText,
            "(<ESBuildUpstreamVersion>)[^<]+(</ESBuildUpstreamVersion>)",
            $"$1{upstreamVersion}$2",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        projectText = Regex.Replace(
            projectText,
            "(<ESBuildWrapperRevision>)[^<]*(</ESBuildWrapperRevision>)",
            "$1$2",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        File.WriteAllText(projectPath, projectText);
    }

    private static string GetRepositoryRoot([CallerFilePath] string filePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, ".."));

    private static string GetPackageProjectPath()
        => Path.Combine(GetRepositoryRoot(), "src", "ESBuild.AspNetCore", "ESBuild.AspNetCore.csproj");

    private static string GetRuntimesRootPath()
        => Path.Combine(GetRepositoryRoot(), "src", "ESBuild.AspNetCore", "runtimes");
}
