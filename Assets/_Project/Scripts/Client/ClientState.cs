using System;
using System.Collections.Generic;
using Battleships.Protocol;

namespace Battleships.Client
{
    public enum ClientMatchStatus
    {
        WaitingForPlayers,
        InProgress,
        Finished
    }

    public readonly struct ClientPosition : IEquatable<ClientPosition>
    {
        public int X { get; }
        public int Y { get; }

        public ClientPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(ClientPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ClientPosition other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"{(char)('A' + X)}{Y + 1}";
    }

    public sealed class ClientOwnCell
    {
        public ClientPosition Position { get; }
        public bool HasShip { get; }
        public ShotResultCode? ShotResult { get; }

        internal ClientOwnCell(ClientPosition position, bool hasShip, ShotResultCode? shotResult)
        {
            Position = position;
            HasShip = hasShip;
            ShotResult = shotResult;
        }
    }

    public sealed class PendingShot
    {
        public string RequestId { get; }
        public long TurnId { get; }
        public ClientPosition Target { get; }

        internal PendingShot(string requestId, long turnId, ClientPosition target)
        {
            RequestId = requestId;
            TurnId = turnId;
            Target = target;
        }
    }

    public sealed class ClientState
    {
        private readonly Dictionary<ClientPosition, ClientOwnCell> ownBoard =
            new Dictionary<ClientPosition, ClientOwnCell>();
        private readonly Dictionary<ClientPosition, ShotResultCode> opponentShots =
            new Dictionary<ClientPosition, ShotResultCode>();

        public bool HasIdentity { get; private set; }
        public PlayerSlot PlayerSlot { get; private set; }
        public int BoardSize { get; private set; }
        public ClientMatchStatus MatchStatus { get; private set; } = ClientMatchStatus.WaitingForPlayers;
        public PlayerSlot CurrentPlayer { get; private set; }
        public long TurnId { get; private set; }
        public long StateVersion { get; private set; }
        public long TurnDeadlineUnixTimeMilliseconds { get; private set; }
        public bool HasWinner { get; private set; }
        public PlayerSlot Winner { get; private set; }
        public PendingShot PendingShot { get; private set; }
        public IReadOnlyDictionary<ClientPosition, ClientOwnCell> OwnBoard => ownBoard;
        public IReadOnlyDictionary<ClientPosition, ShotResultCode> OpponentShots => opponentShots;

        public bool IsYourTurn => HasIdentity && MatchStatus == ClientMatchStatus.InProgress &&
                                  CurrentPlayer == PlayerSlot;

        public long GetRemainingTurnMilliseconds(long nowUnixMilliseconds)
        {
            if (MatchStatus != ClientMatchStatus.InProgress || TurnDeadlineUnixTimeMilliseconds <= 0)
                return 0;
            return Math.Max(0, TurnDeadlineUnixTimeMilliseconds - nowUnixMilliseconds);
        }

        internal bool ApplyJoin(JoinResponse response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.SessionToken)) return false;
            HasIdentity = true;
            PlayerSlot = response.PlayerSlot;
            return true;
        }

        internal bool ApplySnapshot(MatchSnapshot snapshot, bool reconcilePending = false)
        {
            if (snapshot == null || snapshot.StateVersion < StateVersion) return false;
            if (HasIdentity && snapshot.PlayerSlot != PlayerSlot) return false;

            HasIdentity = true;
            PlayerSlot = snapshot.PlayerSlot;
            BoardSize = snapshot.BoardSize;
            CurrentPlayer = snapshot.CurrentPlayer;
            TurnId = snapshot.TurnId;
            StateVersion = snapshot.StateVersion;
            TurnDeadlineUnixTimeMilliseconds = snapshot.TurnDeadlineUnixTimeMilliseconds;
            HasWinner = snapshot.HasWinner;
            Winner = snapshot.Winner;
            MatchStatus = snapshot.HasWinner ? ClientMatchStatus.Finished : ClientMatchStatus.InProgress;

            ownBoard.Clear();
            foreach (var cell in snapshot.OwnBoardCells ?? Array.Empty<OwnBoardCell>())
            {
                if (cell?.Position == null) continue;
                var position = new ClientPosition(cell.Position.X, cell.Position.Y);
                ownBoard[position] = new ClientOwnCell(position, cell.HasShip,
                    cell.HasShotResult ? cell.ShotResult : (ShotResultCode?)null);
            }

            opponentShots.Clear();
            foreach (var shot in snapshot.OpponentShots ?? Array.Empty<OpponentShotResult>())
            {
                if (shot?.Position == null) continue;
                opponentShots[new ClientPosition(shot.Position.X, shot.Position.Y)] = shot.Result;
            }
            if (reconcilePending) PendingShot = null;
            return true;
        }

        internal bool TryBeginShot(string requestId, long turnId, ClientPosition target)
        {
            if (!CanFire(target) || string.IsNullOrWhiteSpace(requestId)) return false;
            PendingShot = new PendingShot(requestId, turnId, target);
            return true;
        }

        internal bool ApplyFireResponse(FireResponse response)
        {
            if (response == null || PendingShot == null ||
                !string.Equals(PendingShot.RequestId, response.RequestId, StringComparison.Ordinal)) return false;

            var pending = PendingShot;
            PendingShot = null;
            if (!response.Accepted || response.StateVersion < StateVersion) return true;

            opponentShots[pending.Target] = response.Result;
            CurrentPlayer = response.CurrentPlayer;
            TurnId = response.TurnId;
            StateVersion = response.StateVersion;
            TurnDeadlineUnixTimeMilliseconds = response.TurnDeadlineUnixTimeMilliseconds;
            HasWinner = response.HasWinner;
            Winner = response.Winner;
            MatchStatus = response.HasWinner ? ClientMatchStatus.Finished : ClientMatchStatus.InProgress;
            return true;
        }

        internal bool CanFire(ClientPosition target) =>
            IsYourTurn && PendingShot == null &&
            target.X >= 0 && target.Y >= 0 && target.X < BoardSize && target.Y < BoardSize &&
            !opponentShots.ContainsKey(target);
    }
}
