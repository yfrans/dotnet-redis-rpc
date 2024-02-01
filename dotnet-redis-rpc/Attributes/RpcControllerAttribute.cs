namespace WebIOS.Packages.RedisRPC.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class RpcControllerAttribute : Attribute
    {
        public string? Prefix { get; private set; }

        public RpcControllerAttribute(string? prefix = null)
        {
            Prefix = prefix;
        }
    }
}
