namespace AspNetCore.Bundling.ESBuild.Tasks;

internal sealed class EsbuildConfigFile
{
    public EsbuildDefaults? Defaults { get; set; }

    public List<EsbuildBundle>? Bundles { get; set; }

    public Dictionary<string, EsbuildConfigurationOverride>? Configurations { get; set; }
}
