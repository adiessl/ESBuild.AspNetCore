using System.Text.Json;

namespace ESBuild.AspNetCore.Tasks;

internal static class EsbuildGeneratedFileSet
{
    public static IReadOnlyList<string> GetExpectedOutputs(
        EffectiveEsbuildBundle bundle,
        string entryPoint,
        string? outputPath,
        string? outdirPath)
    {
        string primaryOutput;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            primaryOutput = outputPath!;
        }
        else
        {
            primaryOutput = Path.Combine(outdirPath!, Path.GetFileNameWithoutExtension(entryPoint) + ".js");
        }

        var files = new List<string> { primaryOutput };

        if (bundle.Sourcemap)
        {
            files.Add(primaryOutput + ".map");
        }

        return files;
    }

    public static string GetManifestPath(string workingDirectory, string bundleKey)
    {
        var manifestDirectory = Path.Combine(workingDirectory, "obj", "ESBuild.AspNetCore");
        Directory.CreateDirectory(manifestDirectory);
        return Path.Combine(manifestDirectory, bundleKey + ".outputs.json");
    }

    public static IReadOnlyList<string> GetKnownOutputs(string manifestPath, IReadOnlyList<string> fallbackOutputs)
    {
        var manifestOutputs = TryReadManifest(manifestPath);
        return manifestOutputs.Count == 0 ? fallbackOutputs : manifestOutputs;
    }

    public static void WriteManifest(string manifestPath, IEnumerable<string> outputs)
    {
        var normalizedOutputs = outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static output => output, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(normalizedOutputs, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> ReadOutputsFromMetafile(string metafilePath, string workingDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(metafilePath));
        if (!document.RootElement.TryGetProperty("outputs", out var outputsElement) || outputsElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return outputsElement.EnumerateObject()
            .Select(output => output.Name)
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Select(output => Path.IsPathRooted(output)
                ? Path.GetFullPath(output)
                : Path.GetFullPath(Path.Combine(workingDirectory, output)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static output => output, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> TryReadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<string>();
        }

        var outputs = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifestPath));
        if (outputs is null || outputs.Length == 0)
        {
            return Array.Empty<string>();
        }

        return outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static output => output, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetRelativePath(string rootFolder, string path)
    {
        var normalizedRoot = TrimEndingDirectorySeparators(Path.GetFullPath(rootFolder));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(normalizedRoot, normalizedPath, comparison))
        {
            return string.Empty;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(rootWithSeparator, comparison)
            ? normalizedPath.Substring(rootWithSeparator.Length)
            : path;
    }

    private static string TrimEndingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        while (path.Length > root.Length && IsDirectorySeparator(path[path.Length - 1]))
        {
            path = path.Substring(0, path.Length - 1);
        }

        return path;
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }

    public static string GetFormattedFileSize(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return string.Empty;
            }

            double bytes = fileInfo.Length;
            if (bytes >= 1024 * 1024)
            {
                return $"{(bytes / 1024 / 1024).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}mb";
            }
            if (bytes >= 1024)
            {
                return $"{(bytes / 1024).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}kb";
            }
            return $"{bytes}b";
        }
        catch
        {
            return string.Empty;
        }
    }
}
