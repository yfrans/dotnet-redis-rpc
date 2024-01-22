using dotnet_redis_rpc;
using StackExchange.Redis;

var connection = ConnectionMultiplexer.Connect("localhost:6379");
var rpc1 = new RedisRpc(connection, "test1").Start();
var rpc2 = new RedisRpc(connection, "test2").Start();

RedisRpc.IncomingMessageHandler onMessageHanlder = (redisRpc, message) =>
{
    // Console.WriteLine($"Service {redisRpc.ServiceName} got {message.Id}: {message.Message}");
    if (message.Message.ToString() == "Hello")
    {
        redisRpc.RespondTo(message, "World");
    }
};

rpc1.OnMessage += onMessageHanlder;
rpc2.OnMessage += onMessageHanlder;

Console.Write("Hello ");
var response1 = await rpc1.SendAsync("test2", "Hello");
Console.WriteLine(response1.Message.ToString());