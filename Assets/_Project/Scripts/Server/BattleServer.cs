using System;
using Battleships.Domain;
using Battleships.Protocol;

namespace Battleships.Server
{
    // Application calls must be serialized by the caller (future transport/server runtime).
    // This object owns one two-player match for its entire lifetime.
    public sealed class BattleServer
    {
        private readonly SessionManager sessions = new SessionManager();
        private readonly RequestCache requests = new RequestCache();
        private readonly SnapshotBuilder snapshots = new SnapshotBuilder();
        private readonly MatchService matches;

        public BattleServer(GameRulesConfig config, Random random, IServerClock clock,
            long turnDurationMilliseconds = 15000)
        {
            matches = new MatchService(config, random, clock, turnDurationMilliseconds);
        }

        public ServerResult<JoinResponse> Handle(JoinRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
                return ServerResult<JoinResponse>.Failure(request?.RequestId, ProtocolErrorCode.InvalidRequest);
            if (!sessions.TryJoin(request.RequestId, out var session))
                return ServerResult<JoinResponse>.Failure(request.RequestId, ProtocolErrorCode.InvalidRequest);
            if (sessions.Count == 2) matches.Start(session.MatchId);
            return ServerResult<JoinResponse>.Success(new JoinResponse
            {
                RequestId = request.RequestId,
                SessionToken = session.Token,
                PlayerSlot = SnapshotBuilder.ToProtocol(session.Player)
            });
        }

        public ServerResult<MatchSnapshot> Handle(ResumeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
                return ServerResult<MatchSnapshot>.Failure(request?.RequestId, ProtocolErrorCode.InvalidRequest);
            var result = GetSnapshot(request.SessionToken);
            return result.IsSuccess
                ? result
                : ServerResult<MatchSnapshot>.Failure(request.RequestId, result.Error.ErrorCode);
        }

        // Server-side transport adapters use this read-only projection API for push snapshots.
        // It never exposes MatchState and always builds a recipient-specific protocol DTO.
        public ServerResult<MatchSnapshot> GetSnapshot(string sessionToken)
        {
            if (!sessions.TryResolve(sessionToken, out var session))
                return ServerResult<MatchSnapshot>.Failure(null, ProtocolErrorCode.InvalidSession);
            var match = matches.Resolve(session);
            if (match == null)
                return ServerResult<MatchSnapshot>.Failure(null, ProtocolErrorCode.MatchNotReady);
            return ServerResult<MatchSnapshot>.Success(snapshots.Build(match, session.Player));
        }

        public FireResponse Handle(FireRequest request) => HandleWithStateChange(request).Response;

        public FireHandlingResult HandleWithStateChange(FireRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
                return new FireHandlingResult(Reject(request, ProtocolErrorCode.InvalidRequest), false);
            if (!sessions.TryResolve(request.SessionToken, out var session))
                return new FireHandlingResult(Reject(request, ProtocolErrorCode.InvalidSession), false);
            if (requests.TryGet(session.Token, request.RequestId, out var cached))
                return new FireHandlingResult(cached, false);
            var response = matches.Fire(session, request, out var stateChanged);
            requests.Store(session.Token, response);
            return new FireHandlingResult(response, stateChanged);
        }

        public ServerResult<HeartbeatResponse> Handle(HeartbeatRequest request)
        {
            if (request == null)
                return ServerResult<HeartbeatResponse>.Failure(null, ProtocolErrorCode.InvalidRequest);
            if (!sessions.TryResolve(request.SessionToken, out _))
                return ServerResult<HeartbeatResponse>.Failure(null, ProtocolErrorCode.InvalidSession);
            return ServerResult<HeartbeatResponse>.Success(new HeartbeatResponse());
        }

        // No scheduling, Unity frame loop, callbacks, or physical message delivery here.
        public bool ProcessDeadlines() => matches.ProcessDeadlines();

        private static FireResponse Reject(FireRequest request, ProtocolErrorCode error) => new FireResponse
        {
            RequestId = request?.RequestId,
            Accepted = false,
            ErrorCode = error,
            Target = request?.Target == null ? null :
                new BoardPosition { X = request.Target.X, Y = request.Target.Y }
        };
    }

    public readonly struct FireHandlingResult
    {
        public FireResponse Response { get; }
        public bool StateChanged { get; }

        internal FireHandlingResult(FireResponse response, bool stateChanged)
        {
            Response = response ?? throw new ArgumentNullException(nameof(response));
            StateChanged = stateChanged;
        }
    }
}
