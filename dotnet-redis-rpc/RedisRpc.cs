using System.Text.Json;
using StackExchange.Redis;

namespace dotnet_redis_rpc;

public class RedisRpc
{
    private ConnectionMultiplexer _connection;
    private int _databaseNumber = 0;
    private CancellationTokenSource? _cancellationTokenSource = null;
    private Dictionary<string, Action<RpcMessage>> _callbacks = new();
    private IDatabase DB { get { return _connection.GetDatabase(_databaseNumber); } }

    public delegate void IncomingMessageHandler(RedisRpc redisRpc, RpcMessage rpcMessage);
    public event IncomingMessageHandler? OnMessage = null;

    public string ServiceName { get; private set; }

    public RedisRpc(ConnectionMultiplexer connection, string serviceName)
    {
        _connection = connection;
        ServiceName = serviceName;
    }

    public RedisRpc SetDB(int number)
    {
        _databaseNumber = number;
        return this;
    }

    public RedisRpc SetServiceName(string name)
    {
        ServiceName = name;
        return this;
    }

    public RedisRpc Start()
    {
        Listen();
        return this;
    }

    public RedisRpc Stop()
    {
        _cancellationTokenSource?.Cancel();
        return this;
    }

    private void Listen()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            // TODO Alreay listening
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var element = await DB.ListLeftPopAsync(ServiceName);
                if (!element.IsNullOrEmpty)
                {
                    var message = RpcMessage.FromJson(element.ToString());
                    if (message != null)
                    {
                        if (message.ReplyTo != null)
                        {
                            if (_callbacks.TryGetValue(message.ReplyTo, out var callback))
                            {
                                _callbacks.Remove(message.Id);
                                callback.Invoke(message);
                            }
                        }
                        OnMessage?.Invoke(this, message);
                    }
                }
            }
        }, _cancellationTokenSource.Token);
    }

    private void Send(RpcMessage message)
    {
        if (message.Target == ServiceName)
        {
            throw new Exception("Cannot send messages to yourself");
        }

        try
        {
            // Console.WriteLine($"Send from {ServiceName} to {message.Target}: {message.Message} ({message.Id})");
            var json = JsonSerializer.Serialize(message);
            DB.ListRightPush(message.Target, json, When.Always, CommandFlags.FireAndForget);
        }
        catch
        {
            throw new Exception("Cannot serialize message");
        }
    }

    public void Send(string target, object message)
    {
        Send(new RpcMessage(ServiceName, target, message));
    }

    public Task<RpcMessage> SendAsync(string target, object message)
    {
        var tcs = new TaskCompletionSource<RpcMessage>();

        var rpcMessage = new RpcMessage(ServiceName, target, message);
        _callbacks.Add(rpcMessage.Id, (response) =>
        {
            tcs.TrySetResult(response);
        });

        try
        {
            Send(rpcMessage);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    public void RespondTo(RpcMessage rpcMessage, object message)
    {
        Send(new RpcMessage(ServiceName, rpcMessage.Origin, message)
        {
            ReplyTo = rpcMessage.Id
        });
    }
}

public class RpcMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public object Message { get; set; }
    public string Target { get; set; }
    public string Origin { get; set; }
    public string? ReplyTo { get; set; } = null;

    public RpcMessage(string origin, string target, object message)
    {
        Target = target;
        Origin = origin;
        Message = message;
    }

    public static RpcMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RpcMessage>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while parsing JSON: " + ex.Message);
            return null;
        }
    }
}