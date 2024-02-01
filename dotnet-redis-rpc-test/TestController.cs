using dotnet_redis_rpc_test;
using WebIOS.Packages.RedisRPC.Attributes;
using WebIOS.Packages.RedisRPC.Interfaces;
using WebIOS.Packages.RedisRPC.Models;
using static RedisRPC_Test.TestModels;

namespace RedisRPC_Test
{
    [RpcController("test")]
    public class TestController : IRedisRpcController
    {
        static Random rnd = new Random();

        [RpcMethod("add")]
        public async Task<RpcActionResult> Add(int input)
        {
            await Task.Delay(rnd.Next(300, 5000));
            return RpcActionResult.Ok(input + 10);
        }

        [RpcMethod("addtwo")]
        public RpcActionResult AddTwo(int a, int b)
        {
            return RpcActionResult.Ok(a + b);
        }

        [RpcMethod("complex")]
        public RpcActionResult ComplexModel(MyModel model)
        {
            return RpcActionResult.Ok(model.A == int.MaxValue &&
                model.B == long.MaxValue &&
                model.C == "test1" &&
                model.D["testKey"].Count == 5 &&
                model.E.A == "test2");
        }

        [RpcMethod("error")]
        public RpcActionResult Error(int error)
        {
            return RpcActionResult.Error(error, "Some error...");
        }

        [RpcMethod("timing")]
        public RpcActionResult Timing(DateTime msgTime)
        {
            return RpcActionResult.Ok(DateTime.UtcNow - msgTime);
        }
    }
}
