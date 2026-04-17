using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace ESBuild.AspNetCore.Tasks;

public sealed class ResolveESBuildOutputs : Microsoft.Build.Utilities.Task
{
    public string? ESBuildFile { get; set; }

    public string? Configuration { get; set; }

    public string? ProjectDirectory { get; set; }

    public string? TargetFramework { get; set; }

    public string? TargetFrameworks { get; set; }

    [Output]
    public ITaskItem[] OutputFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        var rootFolder = ResolveProjectDirectory();
        if (rootFolder is null || string.IsNullOrWhiteSpace(ESBuildFile))
        {
            OutputFiles = Array.Empty<ITaskItem>();
            return true;
        }

        var esbuildFilePath = ResolvePath(rootFolder, ESBuildFile!);
        if (!File.Exists(esbuildFilePath))
        {
            OutputFiles = Array.Empty<ITaskItem>();
            return true;
        }

        IReadOnlyList<EffectiveEsbuildBundle> bundles;
        try
        {
            bundles = EsbuildConfigLoader.Load(esbuildFilePath, Configuration);
        }
        catch (EsbuildConfigException ex)
        {
            Log.LogError(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogError($"Unable to read esbuild.json: {ex}");
            return false;
        }

        var outputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in bundles)
        {
            if (bundle.Optional)
            {
                continue;
            }

            var entryPoint = ResolvePath(rootFolder, bundle.EntryPoint);
            var output = string.IsNullOrWhiteSpace(bundle.Output)
                ? null
                : ResolvePath(rootFolder, bundle.Output!);
            var outdir = string.IsNullOrWhiteSpace(bundle.Outdir)
                ? null
                : ResolvePath(rootFolder, bundle.Outdir!);

            foreach (var expectedOutput in EsbuildGeneratedFileSet.GetExpectedOutputs(bundle, entryPoint, output, outdir))
            {
                outputFiles.Add(MakeRelativePath(rootFolder, expectedOutput));
            }
        }

        OutputFiles = outputFiles
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => new TaskItem(path))
            .ToArray();
        return !Log.HasLoggedErrors;
    }

    private string? ResolveProjectDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            return Path.GetFullPath(ProjectDirectory);
        }

        if (!string.IsNullOrWhiteSpace(ESBuildFile))
        {
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(ESBuildFile));
            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                return configDirectory;
            }
        }

        return null;
    }

    private static string ResolvePath(string rootFolder, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootFolder, path));
    }

    private static string MakeRelativePath(string rootFolder, string absolutePath)
    {
        var root = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootFolder));
        var path = Path.GetFullPath(absolutePath);

        var relativeUri = new Uri(root, UriKind.Absolute).MakeRelativeUri(new Uri(path, UriKind.Absolute));
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
