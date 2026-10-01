using System;
using System.Collections.Generic;
using System.Linq;
using Battleships.Protocol;
using Battleships.Server;

namespace Battleships.Networking.Integration
{
    public sealed class BattleServerTransportAdapter : ITransportMessageReceiver
    {
        private readonly BattleServer server;
        private readonly IServerTransportSender sender;
        private readonly Dictionary<EndpointIdentity, string> sessions =
            new Dictionary<EndpointIdentity, string>();

        public ServerTransportStatus Status { get; private set; } = ServerTransportStatus.Waiting(0);
        public event Action<ServerTransportStatus> StatusChanged;

        public BattleServerTransportAdapter(BattleServer server, IServerTransportSender sender)
        {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        public bool ProcessDeadlines()
        {
            if (!server.ProcessDeadlines()) return false;
            BroadcastSnapshots();
            return true;
        }

        public void Receive(TransportDelivery delivery)
        {
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            if (delivery.Direction != TransportDirection.ClientToServer)
                throw new InvalidOperationException("The server adapter accepts only Client -> Server deliveries.");

            switch (delivery.Message)
            {
                case JoinRequest request:
                    HandleJoin(delivery.Endpoint, request);
                    break;
                case ResumeRequest request:
                    HandleResume(delivery.Endpoint, request);
                    break;
                case FireRequest request:
                    HandleFire(delivery.Endpoint, request);
                    break;
                case HeartbeatRequest request:
                    sender.Send(delivery.Endpoint, Select(server.Handle(request)));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported server message: {delivery.Message?.GetType().FullName ?? "null"}");
            }
        }

        private void HandleJoin(EndpointIdentity endpoint, JoinRequest request)
        {
            var result = server.Handle(request);
            sender.Send(endpoint, Select(result));
            if (!result.IsSuccess) return;

            sessions[endpoint] = result.Response.SessionToken;
            PublishWaitingStatus();
            if (sessions.Values.Distinct(StringComparer.Ordinal).Count() == 2)
                BroadcastSnapshots();
        }

        private void HandleResume(EndpointIdentity endpoint, ResumeRequest request)
        {
            var result = server.Handle(request);
            sender.Send(endpoint, Select(result));
            if (!result.IsSuccess) return;
            sessions[endpoint] = request.SessionToken;
            PublishStatus(result.Response);
        }

        private void HandleFire(EndpointIdentity endpoint, FireRequest request)
        {
            var result = server.HandleWithStateChange(request);
            sender.Send(endpoint, result.Response);
            if (result.StateChanged) BroadcastSnapshots();
        }

        private void BroadcastSnapshots()
        {
            MatchSnapshot statusSource = null;
            foreach (var pair in sessions)
            {
                var snapshot = server.GetSnapshot(pair.Value);
                if (!snapshot.IsSuccess) continue;
                statusSource = statusSource ?? snapshot.Response;
                sender.Send(pair.Key, snapshot.Response);
            }

            if (statusSource != null) PublishStatus(statusSource);
        }

        private void PublishWaitingStatus()
        {
            Status = ServerTransportStatus.Waiting(sessions.Count);
            StatusChanged?.Invoke(Status);
        }

        private void PublishStatus(MatchSnapshot snapshot)
        {
            Status = ServerTransportStatus.FromSnapshot(sessions.Count, snapshot);
            StatusChanged?.Invoke(Status);
        }

        private static object Select<T>(ServerResult<T> result) where T : class =>
            result.IsSuccess ? (object)result.Response : result.Error;
    }

    public sealed class ServerTransportStatus
    {
        public int ConnectedClients { get; }
        public bool MatchReady { get; }
        public PlayerSlot CurrentPlayer { get; }
        public bool HasWinner { get; }
        public PlayerSlot Winner { get; }
        public long TurnId { get; }
        public long StateVersion { get; }

        private ServerTransportStatus(int connectedClients, bool matchReady, PlayerSlot currentPlayer,
            bool hasWinner, PlayerSlot winner, long turnId, long stateVersion)
        {
            ConnectedClients = connectedClients;
            MatchReady = matchReady;
            CurrentPlayer = currentPlayer;
            HasWinner = hasWinner;
            Winner = winner;
            TurnId = turnId;
            StateVersion = stateVersion;
        }

        internal static ServerTransportStatus Waiting(int connectedClients) =>
            new ServerTransportStatus(connectedClients, false, default, false, default, 0, 0);

        internal static ServerTransportStatus FromSnapshot(int connectedClients, MatchSnapshot snapshot) =>
            new ServerTransportStatus(connectedClients, true, snapshot.CurrentPlayer, snapshot.HasWinner,
                snapshot.Winner, snapshot.TurnId, snapshot.StateVersion);
    }
}
