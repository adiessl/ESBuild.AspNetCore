using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AspNetCore.Bundling.ESBuild.Tasks;

public sealed class CompileESBuild : Microsoft.Build.Utilities.Task
{
    public string? ESBuildFile { get; set; }

    public string? Command { get; set; }

    public string? Configuration { get; set; }

    public ITaskItem[] InputFiles { get; set; } = Array.Empty<ITaskItem>();

    public string? ProjectDirectory { get; set; }

    public string? TargetFramework { get; set; }

    public string? TargetFrameworks { get; set; }

    [Output]
    public ITaskItem[] GeneratedFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        var rootFolder = ResolveProjectDirectory();
        if (rootFolder is null)
        {
            Log.LogError("ProjectDirectory was not provided.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ESBuildFile))
        {
            Log.LogError("ESBuildFile was not provided.");
            return false;
        }

        var esbuildFilePath = ResolvePath(rootFolder, ESBuildFile!);
        if (!File.Exists(esbuildFilePath))
        {
            Log.LogMessage(MessageImportance.Low, $"Skipping esbuild because '{esbuildFilePath}' was not found.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            Log.LogError("No esbuild executable is configured for this platform.");
            return false;
        }

        if (!File.Exists(Command))
        {
            Log.LogError($"Configured esbuild executable was not found: '{Command}'.");
            return false;
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

        var generatedFiles = new List<ITaskItem>();
        foreach (var bundle in bundles)
        {
            var entryPoint = ResolvePath(rootFolder, bundle.EntryPoint);
            var output = string.IsNullOrWhiteSpace(bundle.Output)
                ? null
                : ResolvePath(rootFolder, bundle.Output!);
            var outdir = string.IsNullOrWhiteSpace(bundle.Outdir)
                ? null
                : ResolvePath(rootFolder, bundle.Outdir!);

            if (!File.Exists(entryPoint))
            {
                if (bundle.Optional)
                {
                    Log.LogMessage(MessageImportance.Low, $"Skipping optional esbuild entry point '{bundle.EntryPoint}'.");
                    continue;
                }

                Log.LogError($"Could not bundle '{bundle.EntryPoint}' because the entry point does not exist.");
                return false;
            }

            var outputDirectory = !string.IsNullOrWhiteSpace(output)
                ? Path.GetDirectoryName(output)
                : outdir;
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var bundleKey = CreateBundleKey(entryPoint, output, outdir);
            var arguments = EsbuildCommandBuilder.BuildArguments(bundle, entryPoint, output, outdir);
            var expectedOutputs = EsbuildGeneratedFileSet.GetExpectedOutputs(bundle, entryPoint, output, outdir);
            var manifestPath = EsbuildGeneratedFileSet.GetManifestPath(rootFolder, bundleKey);
            var knownOutputs = EsbuildGeneratedFileSet.GetKnownOutputs(manifestPath, expectedOutputs);
            var existingInputs = GetExistingInputs(rootFolder, entryPoint, esbuildFilePath);

            var generatedBundleOutputs = ExecuteBundle(rootFolder, bundleKey, arguments, knownOutputs, existingInputs, expectedOutputs, manifestPath);
            if (generatedBundleOutputs is null)
            {
                return false;
            }

            generatedFiles.AddRange(generatedBundleOutputs.Select(static path => new TaskItem(path)));
        }

        GeneratedFiles = generatedFiles.ToArray();
        return !Log.HasLoggedErrors;
    }

    private IReadOnlyList<string>? ExecuteBundle(
        string rootFolder,
        string bundleKey,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> knownOutputs,
        IReadOnlyList<string> existingInputs,
        IReadOnlyList<string> expectedOutputs,
        string manifestPath)
    {
        using var mutex = CreateMutex(bundleKey);

        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            Log.LogWarning("The ESBuild mutex was abandoned. Continuing with bundle execution.");
        }

        try
        {
            var currentOutputs = EsbuildGeneratedFileSet.GetKnownOutputs(manifestPath, knownOutputs);
            if (IsUpToDate(existingInputs, currentOutputs))
            {
                Log.LogMessage(
                    MessageImportance.Low,
                    $"Skipping esbuild because outputs are up to date: '{currentOutputs[0]}'.");
                return currentOutputs;
            }

            var metafilePath = Path.ChangeExtension(manifestPath, ".metafile.json");
            var processArguments = arguments.Concat(new[] { $"--metafile={metafilePath}" }).ToArray();
            var startInfo = new ProcessStartInfo
            {
                FileName = Command!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = rootFolder,
            };

            EsbuildCommandBuilder.Apply(startInfo, processArguments);
            Log.LogMessage(
                MessageImportance.Normal,
                $"Bundling TypeScript: {EsbuildCommandBuilder.FormatForLogging(startInfo.FileName, processArguments)}");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    Log.LogMessage(MessageImportance.Normal, stdout.Trim());
                }

                Log.LogError(string.IsNullOrWhiteSpace(stderr)
                    ? $"esbuild exited with code {process.ExitCode}."
                    : stderr.Trim());
                return null;
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Log.LogMessage(MessageImportance.Low, stdout.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Log.LogWarning(stderr.Trim());
            }

            IReadOnlyList<string> generatedOutputs;
            try
            {
                generatedOutputs = EsbuildGeneratedFileSet.ReadOutputsFromMetafile(metafilePath, rootFolder);
            }
            catch (Exception ex)
            {
                Log.LogError($"Unable to read esbuild metafile '{metafilePath}': {ex.Message}");
                return null;
            }

            if (generatedOutputs.Count == 0)
            {
                generatedOutputs = expectedOutputs;
            }

            foreach (var expectedOutput in expectedOutputs)
            {
                if (!generatedOutputs.Contains(expectedOutput, StringComparer.OrdinalIgnoreCase))
                {
                    Log.LogError($"esbuild completed successfully but expected output '{expectedOutput}' was not reported by the metafile.");
                    return null;
                }
            }

            foreach (var generatedOutput in generatedOutputs)
            {
                if (!File.Exists(generatedOutput))
                {
                    Log.LogError($"esbuild completed successfully but expected output '{generatedOutput}' was not found.");
                    return null;
                }
            }

            EsbuildGeneratedFileSet.WriteManifest(manifestPath, generatedOutputs);
            return generatedOutputs;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
    }

    private IReadOnlyList<string> GetExistingInputs(string rootFolder, string entryPoint, string esbuildFilePath)
    {
        var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entryPoint,
        };

        inputs.Add(esbuildFilePath);

        foreach (var inputFile in InputFiles)
        {
            if (string.IsNullOrWhiteSpace(inputFile.ItemSpec))
            {
                continue;
            }

            var fullPath = ResolvePath(rootFolder, inputFile.ItemSpec);

            if (File.Exists(fullPath))
            {
                inputs.Add(fullPath);
            }
        }

        return inputs.ToArray();
    }

    private static bool IsUpToDate(IReadOnlyList<string> existingInputs, IReadOnlyList<string> expectedOutputs)
    {
        if (existingInputs.Count == 0 || expectedOutputs.Count == 0)
        {
            return false;
        }

        if (expectedOutputs.Any(static output => !File.Exists(output)))
        {
            return false;
        }

        var latestInputWriteTimeUtc = existingInputs.Max(File.GetLastWriteTimeUtc);
        var earliestOutputWriteTimeUtc = expectedOutputs.Min(File.GetLastWriteTimeUtc);

        return earliestOutputWriteTimeUtc >= latestInputWriteTimeUtc;
    }

    private static string CreateBundleKey(string entryPoint, string? output, string? outdir)
    {
        using var sha256 = SHA256.Create();
        var key = string.Join(
            "|",
            new[]
            {
                entryPoint,
                output ?? string.Empty,
                outdir ?? string.Empty,
            }.Select(static x => x.ToLowerInvariant()));
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
    }

    private static Mutex CreateMutex(string bundleKey)
    {
        return new Mutex(false, $"AspNetCore.Bundling.ESBuild_{bundleKey}");
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
}
