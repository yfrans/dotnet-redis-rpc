namespace WebIOS.Packages.RedisRPC.Models
{
    public class RpcError
    {
        public int Code { get; set; } = 0;
        public string? Message { get; set; } = null;

        public static RpcError ActionNotFound(string? actionName = null)
        {
            return new RpcError { Code = 404, Message = $"Action {actionName ?? "\b"} not found" };
        }

        public static RpcError InvokeActionError(string action, string message)
        {
            return new RpcError { Code = 999, Message = $"Error while invoking action {action}: {message}" };
        }
    }
}
