using System.Text.Json;

namespace ESBuild.AspNetCore.Tasks;

internal static class EsbuildConfigLoader
{
    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "iife",
        "cjs",
        "esm",
    };

    private static readonly HashSet<string> AllowedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "browser",
        "node",
        "neutral",
    };

    public static IReadOnlyList<EffectiveEsbuildBundle> Load(string path, string? configuration)
    {
        var contents = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(contents))
        {
            throw new EsbuildConfigException("esbuild.json exists but is empty.");
        }

        var root = JsonSerializer.Deserialize<EsbuildConfigFile>(
            contents,
            new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true,
            });

        if (root is null)
        {
            throw new EsbuildConfigException("Unable to read esbuild.json.");
        }

        if (root.Bundles is null || root.Bundles.Count == 0)
        {
            throw new EsbuildConfigException("esbuild.json must contain at least one bundle in 'Bundles'.");
        }

        var configurations = root.Configurations is null
            ? new Dictionary<string, EsbuildConfigurationOverride>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, EsbuildConfigurationOverride>(root.Configurations, StringComparer.OrdinalIgnoreCase);
        configurations.TryGetValue(configuration ?? string.Empty, out var configurationOverride);

        var defaults = MergeDefaults(root.Defaults, configurationOverride?.Defaults);
        var bundleOverrides = BuildConfigurationBundleOverrideMap(configuration, root.Bundles, configurationOverride?.Bundles);

        var bundles = new List<EffectiveEsbuildBundle>(root.Bundles.Count);
        foreach (var bundle in root.Bundles)
        {
            var effectiveBundle = ApplyConfigurationBundleOverride(bundle, bundleOverrides);

            if (string.IsNullOrWhiteSpace(effectiveBundle.EntryPoint))
            {
                throw new EsbuildConfigException("Every bundle in esbuild.json must define 'EntryPoint'.");
            }

            var entryPoint = effectiveBundle.EntryPoint!;
            var output = NormalizeOptionalValue(effectiveBundle.Output);
            var outdir = NormalizeOptionalValue(effectiveBundle.Outdir);
            if (string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(outdir))
            {
                throw new EsbuildConfigException($"Bundle '{entryPoint}' must define either 'Output' or 'Outdir'.");
            }

            if (!string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(outdir))
            {
                throw new EsbuildConfigException($"Bundle '{entryPoint}' cannot define both 'Output' and 'Outdir'.");
            }

            var bundleOptions = MergeBundleOptions(defaults, effectiveBundle);
            ValidateBundleOptions(entryPoint, output, outdir, effectiveBundle.Splitting ?? false, bundleOptions);

            bundles.Add(new EffectiveEsbuildBundle
            {
                EntryPoint = NormalizePath(entryPoint),
                Output = output is null ? null : NormalizePath(output),
                Outdir = outdir is null ? null : NormalizePath(outdir),
                Optional = effectiveBundle.Optional ?? false,
                Splitting = effectiveBundle.Splitting ?? false,
                Minify = bundleOptions.Minify,
                Sourcemap = bundleOptions.Sourcemap,
                Target = bundleOptions.Target,
                Format = bundleOptions.Format,
                Platform = bundleOptions.Platform,
                External = (effectiveBundle.External ?? Array.Empty<string>())
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeValue)
                    .ToArray(),
                Define = NormalizeDictionary(entryPoint, "Define", effectiveBundle.Define),
                Alias = NormalizeDictionary(entryPoint, "Alias", effectiveBundle.Alias),
                Loader = NormalizeDictionary(entryPoint, "Loader", effectiveBundle.Loader),
                PublicPath = NormalizeOptionalValue(effectiveBundle.PublicPath),
            });
        }

        return bundles;
    }

    private static EffectiveDefaults MergeDefaults(EsbuildDefaults? rootDefaults, EsbuildDefaults? configurationDefaults)
    {
        var merged = new EffectiveDefaults
        {
            Minify = false,
            Sourcemap = true,
            Target = "es2020",
            Format = "iife",
            Platform = "browser",
        };

        ApplyDefaults(rootDefaults, merged);
        ApplyDefaults(configurationDefaults, merged);

        return merged;
    }

    private static void ApplyDefaults(EsbuildDefaults? source, EffectiveDefaults destination)
    {
        if (source is null)
        {
            return;
        }

        if (source.Minify.HasValue)
        {
            destination.Minify = source.Minify.Value;
        }

        if (source.Sourcemap.HasValue)
        {
            destination.Sourcemap = source.Sourcemap.Value;
        }

        if (!string.IsNullOrWhiteSpace(source.Target))
        {
            destination.Target = NormalizeValue(source.Target!);
        }

        if (!string.IsNullOrWhiteSpace(source.Format))
        {
            destination.Format = NormalizeValue(source.Format!).ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(source.Platform))
        {
            destination.Platform = NormalizeValue(source.Platform!).ToLowerInvariant();
        }
    }

    private static EffectiveBundleOptions MergeBundleOptions(EffectiveDefaults defaults, EsbuildBundle bundle)
    {
        return new EffectiveBundleOptions
        {
            Minify = bundle.Minify ?? defaults.Minify,
            Sourcemap = bundle.Sourcemap ?? defaults.Sourcemap,
            Target = string.IsNullOrWhiteSpace(bundle.Target) ? defaults.Target : NormalizeValue(bundle.Target!),
            Format = string.IsNullOrWhiteSpace(bundle.Format) ? defaults.Format : NormalizeValue(bundle.Format!).ToLowerInvariant(),
            Platform = string.IsNullOrWhiteSpace(bundle.Platform) ? defaults.Platform : NormalizeValue(bundle.Platform!).ToLowerInvariant(),
        };
    }

    private static Dictionary<string, EsbuildBundle> BuildConfigurationBundleOverrideMap(
        string? configuration,
        IReadOnlyList<EsbuildBundle> rootBundles,
        IReadOnlyList<EsbuildBundle>? configurationBundles)
    {
        var overrides = new Dictionary<string, EsbuildBundle>(StringComparer.OrdinalIgnoreCase);
        if (configurationBundles is null || configurationBundles.Count == 0)
        {
            return overrides;
        }

        var rootBundleCounts = rootBundles
            .Where(static bundle => !string.IsNullOrWhiteSpace(bundle.EntryPoint))
            .GroupBy(static bundle => NormalizePath(bundle.EntryPoint!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in configurationBundles)
        {
            if (string.IsNullOrWhiteSpace(bundle.EntryPoint))
            {
                throw new EsbuildConfigException($"Every bundle override in configuration '{configuration}' must define 'EntryPoint'.");
            }

            var normalizedEntryPoint = NormalizePath(bundle.EntryPoint!);
            if (!rootBundleCounts.TryGetValue(normalizedEntryPoint, out var matchCount))
            {
                throw new EsbuildConfigException($"Configuration '{configuration}' defines a bundle override for unknown entry point '{bundle.EntryPoint}'.");
            }

            if (matchCount > 1)
            {
                throw new EsbuildConfigException($"Configuration '{configuration}' defines an ambiguous bundle override for '{bundle.EntryPoint}'. Root bundles must have unique 'EntryPoint' values when using configuration-specific bundle overrides.");
            }

            if (overrides.ContainsKey(normalizedEntryPoint))
            {
                throw new EsbuildConfigException($"Configuration '{configuration}' defines multiple bundle overrides for '{bundle.EntryPoint}'.");
            }

            overrides.Add(normalizedEntryPoint, bundle);
        }

        return overrides;
    }

    private static EsbuildBundle ApplyConfigurationBundleOverride(
        EsbuildBundle bundle,
        IReadOnlyDictionary<string, EsbuildBundle> bundleOverrides)
    {
        if (string.IsNullOrWhiteSpace(bundle.EntryPoint))
        {
            return bundle;
        }

        if (!bundleOverrides.TryGetValue(NormalizePath(bundle.EntryPoint!), out var bundleOverride))
        {
            return bundle;
        }

        return new EsbuildBundle
        {
            EntryPoint = bundle.EntryPoint,
            Output = !string.IsNullOrWhiteSpace(bundleOverride.Output)
                ? bundleOverride.Output
                : !string.IsNullOrWhiteSpace(bundleOverride.Outdir)
                    ? null
                    : bundle.Output,
            Outdir = !string.IsNullOrWhiteSpace(bundleOverride.Outdir)
                ? bundleOverride.Outdir
                : !string.IsNullOrWhiteSpace(bundleOverride.Output)
                    ? null
                    : bundle.Outdir,
            Optional = bundleOverride.Optional ?? bundle.Optional,
            Splitting = bundleOverride.Splitting ?? bundle.Splitting,
            Minify = bundleOverride.Minify ?? bundle.Minify,
            Sourcemap = bundleOverride.Sourcemap ?? bundle.Sourcemap,
            Target = bundleOverride.Target ?? bundle.Target,
            Format = bundleOverride.Format ?? bundle.Format,
            Platform = bundleOverride.Platform ?? bundle.Platform,
            External = bundleOverride.External ?? bundle.External,
            Define = bundleOverride.Define ?? bundle.Define,
            Alias = bundleOverride.Alias ?? bundle.Alias,
            Loader = bundleOverride.Loader ?? bundle.Loader,
            PublicPath = bundleOverride.PublicPath ?? bundle.PublicPath,
        };
    }

    private static void ValidateBundleOptions(
        string entryPoint,
        string? output,
        string? outdir,
        bool splitting,
        EffectiveBundleOptions options)
    {
        if (!AllowedFormats.Contains(options.Format))
        {
            throw new EsbuildConfigException($"Bundle '{entryPoint}' uses unsupported esbuild format '{options.Format}'. Supported values: iife, cjs, esm.");
        }

        if (!AllowedPlatforms.Contains(options.Platform))
        {
            throw new EsbuildConfigException($"Bundle '{entryPoint}' uses unsupported esbuild platform '{options.Platform}'. Supported values: browser, node, neutral.");
        }

        if (splitting && !string.IsNullOrWhiteSpace(output))
        {
            throw new EsbuildConfigException($"Bundle '{entryPoint}' cannot enable 'Splitting' when using 'Output'. Use 'Outdir' instead.");
        }

        if (splitting && string.IsNullOrWhiteSpace(outdir))
        {
            throw new EsbuildConfigException($"Bundle '{entryPoint}' cannot enable 'Splitting' without 'Outdir'.");
        }

        if (splitting && !string.Equals(options.Format, "esm", StringComparison.Ordinal))
        {
            throw new EsbuildConfigException($"Bundle '{entryPoint}' cannot enable 'Splitting' unless 'Format' is 'esm'.");
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeDictionary(
        string? entryPoint,
        string propertyName,
        Dictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var key = NormalizeOptionalValue(pair.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new EsbuildConfigException($"Bundle '{entryPoint}' contains an empty '{propertyName}' key.");
            }

            var value = NormalizeOptionalValue(pair.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new EsbuildConfigException($"Bundle '{entryPoint}' contains an empty '{propertyName}' value for '{key}'.");
            }

            normalized[key!] = value!;
        }

        return normalized;
    }

    private static string NormalizePath(string value)
        => value.Replace('\\', '/');

    private static string NormalizeValue(string value)
        => value.Trim();

    private static string? NormalizeOptionalValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim();
    }

    private sealed class EffectiveDefaults
    {
        public bool Minify { get; set; }

        public bool Sourcemap { get; set; }

        public string Target { get; set; } = string.Empty;

        public string Format { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;
    }

    private sealed class EffectiveBundleOptions
    {
        public bool Minify { get; set; }

        public bool Sourcemap { get; set; }

        public string Target { get; set; } = string.Empty;

        public string Format { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;
    }
}
