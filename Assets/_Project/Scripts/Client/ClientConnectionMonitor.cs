using Battleships.Networking;

namespace Battleships.Client
{
    public enum ClientConnectionState
    {
        Connected,
        ConnectionLost,
        Resuming,
        Disconnected,
        Connecting
    }

    public sealed class ClientConnectionMonitor
    {
        private readonly double timeoutMilliseconds;
        private double lastServerMessageMilliseconds;
        private double resumingSinceMilliseconds;

        public ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;
        public bool IsConnected => State == ClientConnectionState.Connected;
        public EndpointIdentity Endpoint { get; private set; }

        public ClientConnectionMonitor(double timeoutMilliseconds)
        {
            if (double.IsNaN(timeoutMilliseconds) || double.IsInfinity(timeoutMilliseconds) ||
                timeoutMilliseconds <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        internal bool Register(EndpointIdentity endpoint, double nowMilliseconds)
        {
            Endpoint = endpoint;
            lastServerMessageMilliseconds = nowMilliseconds;
            State = ClientConnectionState.Disconnected;
            return true;
        }

        internal bool BeginResuming(double nowMilliseconds)
        {
            resumingSinceMilliseconds = nowMilliseconds;
            if (State == ClientConnectionState.Resuming) return false;
            State = ClientConnectionState.Resuming;
            return true;
        }

        internal void BeginConnecting(double nowMilliseconds)
        {
            resumingSinceMilliseconds = nowMilliseconds;
            State = ClientConnectionState.Connecting;
        }

        internal void ObserveServerMessage(double nowMilliseconds) =>
            lastServerMessageMilliseconds = nowMilliseconds;

        internal bool CompleteSynchronization(double nowMilliseconds)
        {
            lastServerMessageMilliseconds = nowMilliseconds;
            if (State == ClientConnectionState.Connected) return false;
            State = ClientConnectionState.Connected;
            return true;
        }

        internal bool Evaluate(double nowMilliseconds)
        {
            if (State == ClientConnectionState.ConnectionLost || State == ClientConnectionState.Disconnected) return false;
            var reference = State == ClientConnectionState.Resuming || State == ClientConnectionState.Connecting
                ? System.Math.Max(lastServerMessageMilliseconds, resumingSinceMilliseconds)
                : lastServerMessageMilliseconds;
            if (nowMilliseconds - reference <= timeoutMilliseconds) return false;
            State = ClientConnectionState.ConnectionLost;
            return true;
        }

        internal void Unregister() => State = ClientConnectionState.Disconnected;
    }
}
