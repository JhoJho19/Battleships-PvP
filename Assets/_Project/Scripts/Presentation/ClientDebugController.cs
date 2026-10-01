using System;
using System.Collections.Generic;
using System.Globalization;
using Battleships.Client;
using Battleships.Networking;

namespace Battleships.Presentation
{
    public sealed class ClientDebugController : IDisposable
    {
        private const int MaximumRecentEvents = 5;
        private readonly BattleClient client;
        private readonly ClientTransportEndpoint endpoint;
        private readonly Queue<string> recentEvents = new Queue<string>();
        private bool disposed;

        public EndpointIdentity Endpoint => endpoint.Identity;
        public NetworkSettings Settings => endpoint.Settings;
        public bool IsDeliveryEnabled => !Settings.SilentlyDisconnected;
        public bool IsApplicationConnected => client.Connection.State == ClientConnectionState.Connected;
        public bool CanResume => client.SessionIdentity.HasSession;
        public bool IsNetworkLogEnabled => endpoint.LoggingEnabled;
        public string EndpointText => $"{FriendlyEndpoint(Endpoint.EndpointId)} / gen {Endpoint.Generation}";
        public string ConnectedText => ConnectionText(client.Connection.State);
        public string LastRequestIdText => string.IsNullOrWhiteSpace(client.LastRequestId)
            ? "-"
            : $"#{ShortId(client.LastRequestId)}";
        public string RecentEventsText => string.Join("\n", recentEvents);

        public event Action Changed;
        public event Action<ClientEndpointId> RecreateRequested;

        public ClientDebugController(BattleClient client, ClientTransportEndpoint endpoint)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            if (!client.Connection.IsConnected || client.Connection.Endpoint != endpoint.Identity)
                throw new InvalidOperationException("The debug endpoint is not attached to this client.");

            client.StateChanged += NotifyChanged;
            client.RuntimeEvent += AddRecentEvent;
        }

        public string SetLatency(string value)
        {
            var parsed = ParseClamped(value, double.MaxValue);
            var current = Settings;
            Configure(new NetworkSettings(parsed, current.JitterMilliseconds, current.LossRate,
                current.DuplicateRate, current.SilentlyDisconnected, current.RandomSeed));
            return Format(parsed);
        }

        public string SetJitter(string value)
        {
            var parsed = ParseClamped(value, double.MaxValue);
            var current = Settings;
            Configure(new NetworkSettings(current.LatencyMilliseconds, parsed, current.LossRate,
                current.DuplicateRate, current.SilentlyDisconnected, current.RandomSeed));
            return Format(parsed);
        }

        public string SetLossPercent(string value)
        {
            var percentage = ParseClamped(value, 100d);
            var current = Settings;
            Configure(new NetworkSettings(current.LatencyMilliseconds, current.JitterMilliseconds,
                percentage / 100d, current.DuplicateRate, current.SilentlyDisconnected,
                current.RandomSeed));
            return Format(percentage);
        }

        public string SetDuplicatePercent(string value)
        {
            var percentage = ParseClamped(value, 100d);
            var current = Settings;
            Configure(new NetworkSettings(current.LatencyMilliseconds, current.JitterMilliseconds,
                current.LossRate, percentage / 100d, current.SilentlyDisconnected,
                current.RandomSeed));
            return Format(percentage);
        }

        public void Disconnect()
        {
            SetDeliveryEnabled(false);
            AddRecentEvent("Delivery disabled");
        }

        public void Connect()
        {
            SetDeliveryEnabled(true);
            AddRecentEvent("Delivery restored");
            if (!CanResume)
            {
                AddRecentEvent("No joined session to resume");
                return;
            }
            client.Resume();
        }

        public void SetNetworkLogEnabled(bool enabled)
        {
            ThrowIfDisposed();
            endpoint.LoggingEnabled = enabled;
            Changed?.Invoke();
        }

        public void RequestRecreate()
        {
            ThrowIfDisposed();
            if (!CanResume)
            {
                AddRecentEvent("No joined session to recreate");
                return;
            }
            AddRecentEvent("Client recreation requested");
            RecreateRequested?.Invoke(Endpoint.EndpointId);
        }

        public void ReportRuntimeEvent(string value)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(value)) return;
            AddRecentEvent(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.StateChanged -= NotifyChanged;
            client.RuntimeEvent -= AddRecentEvent;
            Changed = null;
            RecreateRequested = null;
        }

        private void SetDeliveryEnabled(bool enabled)
        {
            var current = Settings;
            Configure(new NetworkSettings(current.LatencyMilliseconds, current.JitterMilliseconds,
                current.LossRate, current.DuplicateRate, !enabled, current.RandomSeed));
        }

        private void Configure(NetworkSettings settings)
        {
            ThrowIfDisposed();
            endpoint.Configure(settings);
            Changed?.Invoke();
        }

        private void AddRecentEvent(string value)
        {
            if (recentEvents.Count == MaximumRecentEvents) recentEvents.Dequeue();
            recentEvents.Enqueue(value);
            Changed?.Invoke();
        }

        private void NotifyChanged() => Changed?.Invoke();

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClientDebugController));
        }

        private static double ParseClamped(string value, double maximum)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return 0;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return 0;
            return Math.Min(Math.Max(0, parsed), maximum);
        }

        private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string FriendlyEndpoint(ClientEndpointId id) =>
            id == ClientEndpointId.ClientA ? "Client A" : "Client B";

        private static string ConnectionText(ClientConnectionState state)
        {
            switch (state)
            {
                case ClientConnectionState.Connected: return "Connected";
                case ClientConnectionState.ConnectionLost: return "Connection lost";
                case ClientConnectionState.Resuming: return "Resuming";
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private static string ShortId(string requestId) =>
            requestId.Length <= 8 ? requestId : requestId.Substring(0, 8);
    }
}
