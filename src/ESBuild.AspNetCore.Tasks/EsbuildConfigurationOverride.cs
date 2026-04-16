namespace ESBuild.AspNetCore.Tasks;

internal sealed class EsbuildConfigurationOverride
{
    public EsbuildDefaults? Defaults { get; set; }

    public List<EsbuildBundle>? Bundles { get; set; }
}
