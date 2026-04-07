using Microsoft.Build.Utilities;

namespace AspNetCore.Bundling.ESBuild.Tasks;

public sealed class CleanESBuildOutputs : Microsoft.Build.Utilities.Task
{
    public string? ProjectDirectory { get; set; }

    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            Log.LogError("ProjectDirectory was not provided.");
            return false;
        }

        var projectDirectory = Path.GetFullPath(ProjectDirectory);
        var manifestDirectory = Path.Combine(projectDirectory, "obj", "AspNetCore.Bundling.ESBuild");
        if (!Directory.Exists(manifestDirectory))
        {
            return true;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "*.outputs.json", SearchOption.TopDirectoryOnly))
        {
            foreach (var outputPath in EsbuildGeneratedFileSet.GetKnownOutputs(manifestPath, Array.Empty<string>()))
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }

            var metafilePath = Path.ChangeExtension(manifestPath, ".metafile.json");
            if (File.Exists(metafilePath))
            {
                File.Delete(metafilePath);
            }

            File.Delete(manifestPath);
        }

        if (!Directory.EnumerateFileSystemEntries(manifestDirectory).Any())
        {
            Directory.Delete(manifestDirectory);
        }

        return true;
    }
}
