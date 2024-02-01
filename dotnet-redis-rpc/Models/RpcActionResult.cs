namespace WebIOS.Packages.RedisRPC.Models
{
    public class RpcActionResult
    {
        public object? Result { get; set; } = null;
        public string? ErrorMessage { get; set; } = null;
        public int ErrorCode { get; set; } = 0;

        public bool IsError { get { return ErrorMessage != null; } }

        public static RpcActionResult Ok(object? result = null)
        {
            return new RpcActionResult {
                Result = result
            };
        }

        public static RpcActionResult Error(int code, string message)
        {
            return new RpcActionResult {
                ErrorCode = code,
                ErrorMessage = message
            };
        }
    }
}
