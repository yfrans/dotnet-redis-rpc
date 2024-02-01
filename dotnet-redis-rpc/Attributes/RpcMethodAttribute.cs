namespace WebIOS.Packages.RedisRPC.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RpcMethodAttribute : Attribute
    {
        public string MethodName { get; private set; }

        public RpcMethodAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
