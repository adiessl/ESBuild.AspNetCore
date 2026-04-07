namespace AspNetCore.Bundling.ESBuild.Tasks;

internal sealed class EsbuildBundle
{
    public string? EntryPoint { get; set; }

    public string? Output { get; set; }

    public string? Outdir { get; set; }

    public bool? Optional { get; set; }

    public bool? Splitting { get; set; }

    public bool? Minify { get; set; }

    public bool? Sourcemap { get; set; }

    public string? Target { get; set; }

    public string? Format { get; set; }

    public string? Platform { get; set; }

    public string[]? External { get; set; }

    public Dictionary<string, string>? Define { get; set; }

    public Dictionary<string, string>? Alias { get; set; }

    public Dictionary<string, string>? Loader { get; set; }

    public string? PublicPath { get; set; }
}
