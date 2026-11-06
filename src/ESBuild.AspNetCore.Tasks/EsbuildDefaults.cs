namespace ESBuild.AspNetCore.Tasks;

internal sealed class EsbuildDefaults
{
    public bool? Bundle { get; set; }

    public bool? Minify { get; set; }

    public bool? Sourcemap { get; set; }

    public string? Target { get; set; }

    public string? Format { get; set; }

    public string? Platform { get; set; }
}
