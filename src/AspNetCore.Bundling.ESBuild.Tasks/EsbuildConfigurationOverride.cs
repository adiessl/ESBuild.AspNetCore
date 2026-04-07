namespace AspNetCore.Bundling.ESBuild.Tasks;

internal sealed class EsbuildConfigurationOverride
{
    public EsbuildDefaults? Defaults { get; set; }

    public List<EsbuildBundle>? Bundles { get; set; }
}
