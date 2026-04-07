namespace AspNetCore.Bundling.ESBuild.Tasks;

internal sealed class EsbuildConfigException : Exception
{
    public EsbuildConfigException(string message)
        : base(message)
    {
    }
}
