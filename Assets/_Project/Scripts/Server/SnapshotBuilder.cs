using System;
using System.Collections.Generic;
using Battleships.Domain;
using Battleships.Protocol;

namespace Battleships.Server
{
    public sealed class SnapshotBuilder
    {
        internal MatchSnapshot Build(AuthoritativeMatch match, PlayerId recipient)
        {
            var own = match.State.GetPlayer(recipient).Board;
            var opponent = match.State.GetPlayer(GameRules.Opponent(recipient)).Board;
            var ownCells = new OwnBoardCell[own.Size * own.Size];
            var opponentShots = new List<OpponentShotResult>();
            for (var y = 0; y < own.Size; y++)
            for (var x = 0; x < own.Size; x++)
            {
                var position = new Position(x, y);
                var ownCell = own.GetCell(position);
                ownCells[y * own.Size + x] = new OwnBoardCell
                {
                    Position = new BoardPosition { X = x, Y = y },
                    HasShip = ownCell.HasShip,
                    HasShotResult = ownCell.Shot.HasValue,
                    ShotResult = ownCell.Shot.HasValue ? ToProtocol(ownCell.Shot.Value) : default
                };
                var revealedShot = opponent.GetCell(position).Shot;
                if (revealedShot.HasValue)
                    opponentShots.Add(new OpponentShotResult
                    {
                        Position = new BoardPosition { X = x, Y = y },
                        Result = ToProtocol(revealedShot.Value)
                    });
            }

            return new MatchSnapshot
            {
                PlayerSlot = ToProtocol(recipient),
                BoardSize = own.Size,
                OwnBoardCells = ownCells,
                OpponentShots = opponentShots.ToArray(),
                CurrentPlayer = ToProtocol(match.State.CurrentPlayer),
                HasWinner = match.State.Winner.HasValue,
                Winner = match.State.Winner.HasValue ? ToProtocol(match.State.Winner.Value) : default,
                TurnId = match.TurnId,
                StateVersion = match.StateVersion,
                TurnDeadlineUnixTimeMilliseconds = match.Deadline
            };
        }

        internal static PlayerSlot ToProtocol(PlayerId player)
        {
            switch (player)
            {
                case PlayerId.One: return PlayerSlot.PlayerOne;
                case PlayerId.Two: return PlayerSlot.PlayerTwo;
                default: throw new ArgumentOutOfRangeException(nameof(player));
            }
        }

        internal static ShotResultCode ToProtocol(ShotResult result)
        {
            switch (result)
            {
                case ShotResult.miss: return ShotResultCode.Miss;
                case ShotResult.hit: return ShotResultCode.Hit;
                case ShotResult.sunk: return ShotResultCode.Sunk;
                default: throw new ArgumentOutOfRangeException(nameof(result));
            }
        }
    }
}
