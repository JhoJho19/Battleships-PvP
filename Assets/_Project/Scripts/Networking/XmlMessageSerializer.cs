using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Battleships.Networking
{
    public sealed class XmlMessageSerializer : IMessageSerializer
    {
        private readonly Dictionary<Type, XmlSerializer> serializersByType =
            new Dictionary<Type, XmlSerializer>();
        private readonly Dictionary<string, Type> typesById = new Dictionary<string, Type>();

        public XmlMessageSerializer(IEnumerable<Type> messageTypes)
        {
            if (messageTypes == null) throw new ArgumentNullException(nameof(messageTypes));

            foreach (var type in messageTypes)
            {
                if (type == null) throw new ArgumentException("Message types cannot contain null.", nameof(messageTypes));
                var id = type.FullName;
                if (string.IsNullOrEmpty(id) || typesById.ContainsKey(id))
                    throw new ArgumentException("Message types must have unique full names.", nameof(messageTypes));
                typesById.Add(id, type);
                serializersByType.Add(type, new XmlSerializer(type));
            }
        }

        public SerializedMessage Serialize(object message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var type = message.GetType();
            if (!serializersByType.TryGetValue(type, out var serializer))
                throw new InvalidOperationException($"Message type is not registered: {type.FullName}");

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, message);
                return new SerializedMessage(type.FullName, stream.ToArray());
            }
        }

        public object Deserialize(string messageType, byte[] payload)
        {
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!typesById.TryGetValue(messageType, out var type))
                throw new InvalidOperationException($"Message type is not registered: {messageType}");

            using (var stream = new MemoryStream(payload, false))
                return serializersByType[type].Deserialize(stream);
        }
    }
}
