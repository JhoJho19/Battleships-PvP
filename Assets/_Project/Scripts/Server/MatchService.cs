using System;
using Battleships.Domain;
using Battleships.Protocol;

namespace Battleships.Server
{
    internal sealed class AuthoritativeMatch
    {
        internal MatchState State { get; }
        internal long StateVersion { get; set; } = 1;
        internal long TurnId { get; set; } = 1;
        internal long Deadline { get; set; }

        internal AuthoritativeMatch(MatchState state, long deadline)
        {
            State = state;
            Deadline = deadline;
        }
    }

    public sealed class MatchService
    {
        private readonly GameRulesConfig config;
        private readonly Random random;
        private readonly IServerClock clock;
        private readonly long turnDurationMilliseconds;
        private AuthoritativeMatch match;
        private string matchId;

        public MatchService(GameRulesConfig config, Random random, IServerClock clock,
            long turnDurationMilliseconds)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (turnDurationMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(turnDurationMilliseconds));
            this.turnDurationMilliseconds = turnDurationMilliseconds;
        }

        internal void Start(string id)
        {
            if (match != null) return;
            match = new AuthoritativeMatch(GameRules.CreateMatch(config, random),
                NewDeadline(clock.UnixTimeMilliseconds));
            matchId = id;
        }

        internal AuthoritativeMatch Resolve(PlayerSession session) =>
            match != null && session.MatchId == matchId ? match : null;

        internal FireResponse Fire(PlayerSession session, FireRequest request)
        {
            var current = Resolve(session);
            var error = ProtocolErrorCode.None;
            ShotOutcome outcome = default;
            if (current == null) error = ProtocolErrorCode.MatchNotReady;
            else if (request.Target == null) error = ProtocolErrorCode.InvalidRequest;
            else if (current.State.Winner.HasValue) error = ProtocolErrorCode.MatchFinished;
            else if (request.TurnId != current.TurnId) error = ProtocolErrorCode.StaleTurn;
            else if (session.Player != current.State.CurrentPlayer) error = ProtocolErrorCode.NotYourTurn;
            else
            {
                var now = clock.UnixTimeMilliseconds;
                if (now >= current.Deadline) error = ProtocolErrorCode.TurnExpired;
                else
                {
                    // Calculate before mutation so overflow cannot leave a partially updated turn.
                    var nextDeadline = NewDeadline(now);
                    outcome = GameRules.Fire(current.State, session.Player,
                        new Position(request.Target.X, request.Target.Y));
                    error = ToProtocol(outcome.Rejection);
                    if (outcome.Accepted)
                    {
                        current.StateVersion++;
                        if (!current.State.Winner.HasValue)
                        {
                            current.TurnId++;
                            current.Deadline = nextDeadline;
                        }
                    }
                }
            }

            return new FireResponse
            {
                RequestId = request.RequestId,
                Accepted = error == ProtocolErrorCode.None,
                ErrorCode = error,
                Target = request.Target == null ? null :
                    new BoardPosition { X = request.Target.X, Y = request.Target.Y },
                Result = outcome.Result.HasValue ? SnapshotBuilder.ToProtocol(outcome.Result.Value) : default,
                CurrentPlayer = current == null ? default : SnapshotBuilder.ToProtocol(current.State.CurrentPlayer),
                HasWinner = current != null && current.State.Winner.HasValue,
                Winner = current != null && current.State.Winner.HasValue ?
                    SnapshotBuilder.ToProtocol(current.State.Winner.Value) : default,
                TurnId = current?.TurnId ?? 0,
                StateVersion = current?.StateVersion ?? 0,
                TurnDeadlineUnixTimeMilliseconds = current?.Deadline ?? 0
            };
        }

        internal bool ProcessDeadlines()
        {
            if (match == null || match.State.Winner.HasValue) return false;
            var now = clock.UnixTimeMilliseconds;
            if (now < match.Deadline) return false;
            var nextDeadline = NewDeadline(now);
            if (!GameRules.ExpireTurn(match.State)) return false;
            match.StateVersion++;
            match.TurnId++;
            match.Deadline = nextDeadline;
            return true;
        }

        private long NewDeadline(long now) => checked(now + turnDurationMilliseconds);

        private static ProtocolErrorCode ToProtocol(ShotRejection rejection)
        {
            switch (rejection)
            {
                case ShotRejection.None: return ProtocolErrorCode.None;
                case ShotRejection.UnknownPlayer: return ProtocolErrorCode.InvalidSession;
                case ShotRejection.OutOfBounds: return ProtocolErrorCode.OutOfBounds;
                case ShotRejection.NotYourTurn: return ProtocolErrorCode.NotYourTurn;
                case ShotRejection.AlreadyShot: return ProtocolErrorCode.AlreadyShot;
                case ShotRejection.MatchFinished: return ProtocolErrorCode.MatchFinished;
                default: throw new ArgumentOutOfRangeException(nameof(rejection));
            }
        }
    }
}
