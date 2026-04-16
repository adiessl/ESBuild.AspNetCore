namespace ESBuild.AspNetCore.Tasks;

internal sealed class EffectiveEsbuildBundle
{
    public string EntryPoint { get; set; } = string.Empty;

    public string? Output { get; set; }

    public string? Outdir { get; set; }

    public bool Optional { get; set; }

    public bool Splitting { get; set; }

    public bool Minify { get; set; }

    public bool Sourcemap { get; set; }

    public string Target { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public IReadOnlyList<string> External { get; set; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Define { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Alias { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Loader { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? PublicPath { get; set; }
}
