using Newtonsoft.Json;
using WebIOS.Packages.RedisRPC.Attributes;
using WebIOS.Packages.RedisRPC.Interfaces;
using WebIOS.Packages.RedisRPC.Models;
using StackExchange.Redis;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace WebIOS.Packages
{
    public class RedisRpc
    {
        #region Members
        private readonly ConnectionMultiplexer _redisConnection;
        private readonly int _redisDb;
        private CancellationTokenSource? _cancellationTokenSource = null;
        private readonly Dictionary<string, Action<RpcMessage>> _callbacks = new();
        private object _callbacksSync = new();
        private readonly Dictionary<string, Func<RpcMessage, Task>> _actionSubscribers = new();
        private IServiceProvider? _serviceProvider = null;

        public delegate void IncomingMessageHandler(RedisRpc redisRpc, RpcMessage rpcMessage);
        public event IncomingMessageHandler? OnMessage;
        public event IncomingMessageHandler? OnError;

        public string ServiceName { get; private set; }
        public string? GroupName { get; private set; } = null;

        private IDatabase Redis { get { return _redisConnection.GetDatabase(_redisDb); } }
        #endregion Members

        #region Constructor
        public RedisRpc(string serviceName, ConnectionMultiplexer redisConnection, int redisDb = 0)
        {
            _redisConnection = redisConnection;
            _redisDb = redisDb;
            ServiceName = serviceName;
        }

        public RedisRpc(string serviceName, string redisHost, int redisDb = 0)
            : this(serviceName, ConnectionMultiplexer.Connect(redisHost), redisDb) { }
        #endregion Constructor

        internal RedisRpc AddServiceProviderSupport(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            return this;
        }

        private Func<RpcMessage, Task> GetControllerMethodHandler(object controllerInstance, MethodInfo method)
        {
            var methodParams = method.GetParameters();
            var isAwaitable = method.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;
            var isGenericType = method.ReturnType.IsGenericType;
            var isVoidType = method.ReturnType == typeof(void);

            if (methodParams == null) {
                throw new Exception($"Unknown error while retreiving parameters for method {method.Name}");
            }

            return async (rpcMessage) => {
                var parameters = new List<object?>();

                if (methodParams.Length == 1) {
                    var rpcMessagePayload = rpcMessage.DeserializePayload(methodParams[0].ParameterType);
                    if (rpcMessagePayload == null) {
                        throw new Exception($"Could not deserialize single parameter payload for action {method.Name}");
                    }

                    parameters.Add(rpcMessagePayload);
                } else if (methodParams.Length > 1) {
                    var rpcMessagePayload = rpcMessage.DeserializePayload<Dictionary<string, JToken>>();
                    if (rpcMessagePayload == null) {
                        throw new Exception($"Could not deserialize multiple parameters payload for action {method.Name}");
                    }

                    parameters.AddRange(methodParams.Select(p => {
                        if (p.Name == null) {
                            throw new Exception($"Cannot invoke {method.Name}: found parameter without a name.");
                        }

                        if (rpcMessagePayload.TryGetValue(p.Name, out var pValue)) {
                            return Convert.ChangeType(pValue, p.ParameterType);
                        } else {
                            throw new Exception($"Cannot invoke {method.Name}: missing parameter {p.Name}");
                        }
                    }));
                }

                RpcActionResult? invocationResult = null;

                try {
                    if (isAwaitable) {
                        var task = method.Invoke(controllerInstance, parameters.ToArray());
                        if (isGenericType) {
                            var t = task as Task<RpcActionResult>;
                            if (t != null) {
                                invocationResult = await t;
                            } else {
                                throw new Exception($"Error while casting method {method.Name} to awaitbale generic method.");
                            }
                        } else {
                            var t = task as Task;
                            if (t != null) {
                                await t.ConfigureAwait(false);
                            } else {
                                throw new Exception($"Error while casting method {method.Name} to awaitbale method.");
                            }
                        }
                    } else {
                        if (isVoidType) {
                            method.Invoke(controllerInstance, parameters.ToArray());
                        } else {
                            invocationResult = method.Invoke(controllerInstance, parameters.ToArray()) as RpcActionResult;
                        }
                    }
                } catch (Exception ex) {
                    throw new Exception($"Error while trying to invoke method {method.Name}: {ex.Message}");
                }

                if (invocationResult != null) {
                    if (invocationResult.IsError) {
                        RespondTo(rpcMessage, new RpcError {
                            Message = invocationResult.ErrorMessage,
                            Code = invocationResult.ErrorCode
                        });
                    } else {
                        RespondTo(rpcMessage, invocationResult.Result);
                    }
                } else if ((isAwaitable && isGenericType) || !isVoidType) {
                    throw new Exception($"Got null response from non void / Task method {method.Name}.");
                }
            };
        }

        private object? CreateControllerInstance(Type controllerType)
        {
            if (_serviceProvider != null) {
                return ActivatorUtilities.CreateInstance(_serviceProvider, controllerType);
            } else {
                return Activator.CreateInstance(controllerType);
            }
        }

        public void RegisterController<T>()
            where T : IRedisRpcController
        {
            var controllerType = typeof(T);
            if (controllerType == null) {
                throw new Exception("Could not determine the controller type.");
            }

            var controllerAttribute = controllerType.GetCustomAttribute<RpcControllerAttribute>();
            if (controllerAttribute == null) {
                throw new Exception($"Cannot register controller {controllerType.Name}: RpcController attribute is missing.");
            }

            object? controllerInstance;

            try {
                controllerInstance = CreateControllerInstance(controllerType);
            } catch (Exception ex) {
                throw new Exception($"Error while initializing controller {controllerType.Name}: {ex.Message}");
            }

            if (controllerInstance == null) {
                throw new Exception($"Unknown error while initializing controller {controllerType.Name}.");
            }

            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods) {
                var attr = method.GetCustomAttribute<RpcMethodAttribute>();
                if (attr == null) { continue; }

                if (method.ReturnType != typeof(RpcActionResult) &&
                    method.ReturnType != typeof(Task<RpcActionResult>) &&
                    method.ReturnType != typeof(void) &&
                    method.ReturnType != typeof(Task)) {
                    throw new Exception($"Cannot register method {method.Name}: return type must be one of: void, Task, RpcActionResult or Task<RpcActionResult>.");
                }

                var prefix = attr.MethodName;
                if (controllerAttribute.Prefix != null) {
                    prefix = controllerAttribute.Prefix + "/" + prefix;
                }

                On(prefix, GetControllerMethodHandler(controllerInstance, method));
            }
        }

        public RedisRpc Group(string name)
        {
            GroupName = name;
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

        public RedisRpc On(string action, Func<RpcMessage, Task> handler)
        {
            if (GroupName != null) {
                action = GroupName + "/" + action;
            }
            _actionSubscribers.TryAdd(action, handler);
            return this;
        }

        private void Listen()
        {
            if (_cancellationTokenSource != null) {
                throw new Exception("Already started.");
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(async () => {
                while (!_cancellationTokenSource.IsCancellationRequested) {
                    var element = await Redis.ListLeftPopAsync(ServiceName);
                    if (!element.IsNullOrEmpty) {
                        var message = JsonConvert.DeserializeObject<RpcMessage>(element.ToString());
                        if (message != null) {
                            if (message.IsError) {
                                OnError?.Invoke(this, message);
                                return;
                            }

                            if (message.ReplyTo != null) {
                                Action<RpcMessage>? callback = null;
                                lock (_callbacksSync) {
                                    if (_callbacks.TryGetValue(message.ReplyTo, out var value)) {
                                        callback = value;
                                        _callbacks.Remove(message.ReplyTo);
                                    }
                                }
                                callback?.Invoke(message);
                            }

                            if (message.Action != null) {
                                _ = Task.Run(async () => {
                                    try {
                                        if (_actionSubscribers.TryGetValue(message.Action, out var handler)) {
                                            await handler.Invoke(message);
                                        } else {
                                            RespondTo(message, RpcError.ActionNotFound(message.Action));
                                        }
                                    } catch (Exception ex) {
                                        RespondTo(message, RpcError.InvokeActionError(message.Action, ex.Message));
                                    }
                                });
                            }

                            OnMessage?.Invoke(this, message);
                        }
                    }
                }
                _cancellationTokenSource = null;
            }, _cancellationTokenSource.Token);
        }

        private void Send(RpcMessage message)
        {
            if (message.Target == ServiceName) {
                throw new Exception("Cannot send messages to yourself");
            }

            try {
                var json = JsonConvert.SerializeObject(message);
                Redis.ListRightPush(message.Target, json, When.Always, CommandFlags.FireAndForget);
            } catch {
                throw new Exception("Cannot serialize message");
            }
        }

        public void Send(string target, string action, object message)
        {
            Send(new RpcMessage(ServiceName, target, message) { Action = action });
        }

        public Task<RpcMessage> SendAsync(string target, string action, object message)
        {
            var tcs = new TaskCompletionSource<RpcMessage>();

            var rpcMessage = new RpcMessage(ServiceName, target, message) { Action = action };

            lock (_callbacksSync) {
                _callbacks.Add(rpcMessage.Id, (response) => {
                    tcs.TrySetResult(response);
                });
            }

            try {
                Send(rpcMessage);
            } catch (Exception ex) {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        public void RespondTo(RpcMessage rpcMessage, object? message)
        {
            Send(new RpcMessage(ServiceName, rpcMessage.Origin, message) {
                ReplyTo = rpcMessage.Id
            });
        }

        public void RespondTo(RpcMessage rpcMessage, RpcError error)
        {
            Send(new RpcMessage(ServiceName, rpcMessage.Origin, null) {
                ReplyTo = rpcMessage.Id,
                Error = error
            });
        }
    }
}
