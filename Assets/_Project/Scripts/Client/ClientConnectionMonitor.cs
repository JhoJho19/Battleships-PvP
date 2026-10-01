using Battleships.Networking;

namespace Battleships.Client
{
    public sealed class ClientConnectionMonitor
    {
        public bool IsConnected { get; private set; }
        public EndpointIdentity Endpoint { get; private set; }

        internal void Register(EndpointIdentity endpoint)
        {
            Endpoint = endpoint;
            IsConnected = true;
        }

        internal void Unregister() => IsConnected = false;
    }
}
