namespace ESBuild.AspNetCore.Tasks;

internal sealed class EsbuildConfigException : Exception
{
    public EsbuildConfigException(string message)
        : base(message)
    {
    }
}
