using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebIOS.Packages.RedisRPC.Models
{
    public class RpcMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Action { get; set; } = null;
        public JToken? Payload { get; set; } = null;
        public string Target { get; set; }
        public string Origin { get; set; }
        public string? ReplyTo { get; set; } = null;
        public RpcError? Error { get; set; } = null;

        public bool IsError { get { return Error != null; } }

        public RpcMessage(string origin, string target, object? payload = null)
        {
            Target = target;
            Origin = origin;

            if (payload != null) {
                Payload = JToken.FromObject(payload);
            }
        }

        public T? DeserializePayload<T>()
        {
            if (Payload == null) {
                return default;
            }

            try {
                return Payload.ToObject<T>();
            } catch {
                return default;
            }
        }

        public object? DeserializePayload(Type type)
        {
            if (Payload == null) {
                return null;
            }

            try {
                return Payload.ToObject(type);
            } catch {
                return null;
            }
        }
    }
}
