using System;
using Battleships.Networking;
using Battleships.Protocol;

namespace Battleships.Client
{
    public sealed class BattleClient : ITransportMessageReceiver, IDisposable
    {
        private readonly Func<string> requestIdFactory;
        private readonly Func<double> timeMilliseconds;
        private readonly ClientSessionIdentity sessionIdentity;
        private ClientTransportEndpoint endpoint;
        private bool disposed;
        private bool resumePending;
        private string initialJoinRequestId;
        private MatchSnapshot deferredInitialSnapshot;

        public ClientState State { get; } = new ClientState();
        public ClientConnectionMonitor Connection { get; }
        public ClientSessionIdentity SessionIdentity => sessionIdentity;
        public string LastRequestId { get; private set; }

        public event Action StateChanged;
        public event Action<string> RuntimeEvent;
        public event Action RequestSent;

        public BattleClient(Func<string> requestIdFactory = null) : this(
            new ClientSessionIdentity(), 5000d,
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), requestIdFactory)
        {
        }

        public BattleClient(ClientSessionIdentity sessionIdentity, double connectionTimeoutMilliseconds,
            Func<double> timeMilliseconds, Func<string> requestIdFactory = null)
        {
            this.sessionIdentity = sessionIdentity ?? throw new ArgumentNullException(nameof(sessionIdentity));
            this.timeMilliseconds = timeMilliseconds ?? throw new ArgumentNullException(nameof(timeMilliseconds));
            this.requestIdFactory = requestIdFactory ?? (() => Guid.NewGuid().ToString("N"));
            Connection = new ClientConnectionMonitor(connectionTimeoutMilliseconds);
        }

        public void AttachEndpoint(ClientTransportEndpoint transportEndpoint)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleClient));
            if (endpoint != null) throw new InvalidOperationException("A transport endpoint is already attached.");
            endpoint = transportEndpoint ?? throw new ArgumentNullException(nameof(transportEndpoint));
            Connection.Register(endpoint.Identity, Now());
            StateChanged?.Invoke();
        }

        public void Connect()
        {
            if (sessionIdentity.HasSession) Resume();
            else Join();
        }

        public void Join()
        {
            EnsureReady();
            if (sessionIdentity.HasSession)
                throw new InvalidOperationException("An existing session must use Resume.");
            initialJoinRequestId = initialJoinRequestId ?? NextRequestId();
            LastRequestId = initialJoinRequestId;
            Connection.BeginConnecting(Now());
            RuntimeEvent?.Invoke("Join sent");
            StateChanged?.Invoke();
            Send(new JoinRequest { RequestId = initialJoinRequestId });
        }

        public void Resume()
        {
            EnsureReady();
            if (!sessionIdentity.HasSession)
                throw new InvalidOperationException("The client has no session to resume.");

            Connection.BeginResuming(Now());
            resumePending = true;
            var requestId = NextRequestId();
            LastRequestId = requestId;
            RuntimeEvent?.Invoke("Resume sent");
            StateChanged?.Invoke();
            Send(new ResumeRequest
            {
                RequestId = requestId,
                SessionToken = sessionIdentity.SessionToken
            });
        }

        public bool SendHeartbeat()
        {
            EnsureReady();
            if (!sessionIdentity.HasSession || Connection.State != ClientConnectionState.Connected)
                return false;
            Send(new HeartbeatRequest { SessionToken = sessionIdentity.SessionToken });
            return true;
        }

        public bool CheckConnection()
        {
            EnsureReady();
            if (!Connection.Evaluate(Now())) return false;
            RuntimeEvent?.Invoke("Connection lost");
            StateChanged?.Invoke();
            return true;
        }

        public bool TryFire(ClientPosition target)
        {
            EnsureReady();
            if (Connection.State != ClientConnectionState.Connected) return false;
            if (!State.CanFire(target)) return false;
            var requestId = NextRequestId();
            var turnId = State.TurnId;
            if (!State.TryBeginShot(requestId, turnId, target))
                throw new InvalidOperationException("The client state changed while creating a shot request.");

            LastRequestId = requestId;
            StateChanged?.Invoke();
            RuntimeEvent?.Invoke($"Fire {target} sent");
            Send(new FireRequest
            {
                RequestId = requestId,
                SessionToken = sessionIdentity.SessionToken,
                TurnId = turnId,
                Target = new BoardPosition { X = target.X, Y = target.Y }
            });
            return true;
        }

        public bool RetryPendingShot()
        {
            EnsureReady();
            if (Connection.State != ClientConnectionState.Connected) return false;
            var pending = State.PendingShot;
            if (pending == null) return false;

            RuntimeEvent?.Invoke($"Fire {pending.Target} retry sent");
            Send(new FireRequest
            {
                RequestId = pending.RequestId,
                SessionToken = sessionIdentity.SessionToken,
                TurnId = pending.TurnId,
                Target = new BoardPosition { X = pending.Target.X, Y = pending.Target.Y }
            });
            return true;
        }

        public void Receive(TransportDelivery delivery)
        {
            if (disposed) return;
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            if (delivery.Direction != TransportDirection.ServerToClient ||
                endpoint == null || delivery.Endpoint != endpoint.Identity) return;

            var now = Now();

            switch (delivery.Message)
            {
                case JoinResponse response:
                    if (initialJoinRequestId != null && response.RequestId != initialJoinRequestId) break;
                    if (sessionIdentity.Apply(response) && State.ApplyJoin(response))
                    {
                        Connection.ObserveServerMessage(now);
                        if (!resumePending) Connection.CompleteSynchronization(now);
                        if (deferredInitialSnapshot != null)
                        {
                            if (deferredInitialSnapshot.PlayerSlot == sessionIdentity.PlayerSlot)
                                State.ApplySnapshot(deferredInitialSnapshot);
                            deferredInitialSnapshot = null;
                        }
                        RuntimeEvent?.Invoke("Joined match");
                        StateChanged?.Invoke();
                    }
                    break;
                case MatchSnapshot snapshot:
                    if (snapshot == null) break;
                    if (!sessionIdentity.HasSession)
                    {
                        // Jitter may deliver the initial snapshot before its JoinResponse.
                        // Keep it private until the server-assigned identity is confirmed.
                        if (initialJoinRequestId != null && (deferredInitialSnapshot == null ||
                            snapshot.StateVersion >= deferredInitialSnapshot.StateVersion))
                            deferredInitialSnapshot = snapshot;
                        break;
                    }
                    if (snapshot.PlayerSlot != sessionIdentity.PlayerSlot) break;
                    Connection.ObserveServerMessage(now);
                    var resuming = resumePending;
                    if (State.ApplySnapshot(snapshot, resuming))
                    {
                        if (resuming)
                        {
                            resumePending = false;
                            Connection.CompleteSynchronization(now);
                            RuntimeEvent?.Invoke("Resume accepted");
                            RuntimeEvent?.Invoke("Snapshot synchronized");
                        }
                        else
                            RuntimeEvent?.Invoke("Snapshot received");
                        StateChanged?.Invoke();
                    }
                    break;
                case FireResponse response:
                    Connection.ObserveServerMessage(now);
                    if (Connection.State != ClientConnectionState.Connected) break;
                    var pending = State.PendingShot;
                    if (!State.ApplyFireResponse(response)) break;
                    RuntimeEvent?.Invoke(response.Accepted
                        ? $"Fire {pending.Target} confirmed: {response.Result}"
                        : $"Fire {pending.Target} rejected: {response.ErrorCode}");
                    StateChanged?.Invoke();
                    break;
                case ErrorResponse response:
                    Connection.ObserveServerMessage(now);
                    if (resumePending && response.RequestId == LastRequestId &&
                        response.ErrorCode == ProtocolErrorCode.MatchNotReady)
                    {
                        State.ApplyJoin(new JoinResponse
                        {
                            SessionToken = sessionIdentity.SessionToken,
                            PlayerSlot = sessionIdentity.PlayerSlot
                        });
                        resumePending = false;
                        Connection.CompleteSynchronization(now);
                        RuntimeEvent?.Invoke("Session restored; waiting for players");
                        StateChanged?.Invoke();
                        break;
                    }
                    RuntimeEvent?.Invoke($"Request rejected: {response.ErrorCode}");
                    break;
                case HeartbeatResponse _:
                    Connection.ObserveServerMessage(now);
                    break;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            endpoint?.Dispose();
            endpoint = null;
            Connection.Unregister();
            StateChanged = null;
            RuntimeEvent = null;
            RequestSent = null;
        }

        private string NextRequestId()
        {
            var requestId = requestIdFactory();
            if (string.IsNullOrWhiteSpace(requestId))
                throw new InvalidOperationException("The request id factory returned an empty value.");
            return requestId;
        }

        private void Send(object message)
        {
            endpoint.Send(message);
            RequestSent?.Invoke();
        }

        private void EnsureReady()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleClient));
            if (endpoint == null) throw new InvalidOperationException("No transport endpoint is attached.");
        }

        private double Now()
        {
            var value = timeMilliseconds();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("The client time provider returned a non-finite value.");
            return value;
        }
    }
}
