using System;

namespace Battleships.Domain
{
    public sealed class PlayerState
    {
        public PlayerId Id { get; }
        public Board Board { get; }
        internal PlayerState(PlayerId id, Board board) { Id = id; Board = board; }
    }

    public sealed class MatchState
    {
        public PlayerState PlayerOne { get; }
        public PlayerState PlayerTwo { get; }
        public PlayerId CurrentPlayer { get; internal set; } = PlayerId.One;
        public PlayerId? Winner { get; internal set; }

        internal MatchState(Board one, Board two)
        {
            PlayerOne = new PlayerState(PlayerId.One, one);
            PlayerTwo = new PlayerState(PlayerId.Two, two);
        }

        public PlayerState GetPlayer(PlayerId player)
        {
            if (player == PlayerId.One) return PlayerOne;
            if (player == PlayerId.Two) return PlayerTwo;
            throw new ArgumentOutOfRangeException(nameof(player));
        }
    }

    public static class GameRules
    {
        public static bool IsOnBoard(Position position, int boardSize) =>
            position.X >= 0 && position.Y >= 0 && position.X < boardSize && position.Y < boardSize;

        public static PlayerId Opponent(PlayerId player)
        {
            if (player == PlayerId.One) return PlayerId.Two;
            if (player == PlayerId.Two) return PlayerId.One;
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        public static MatchState CreateMatch(GameRulesConfig config, Random random)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (random == null) throw new ArgumentNullException(nameof(random));
            return new MatchState(FleetPlacement.Create(config, random), FleetPlacement.Create(config, random));
        }

        public static bool ExpireTurn(MatchState match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (match.Winner.HasValue) return false;
            match.CurrentPlayer = Opponent(match.CurrentPlayer);
            return true;
        }

        public static ShotOutcome Fire(MatchState match, PlayerId player, Position position)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (match.Winner.HasValue) return ShotOutcome.Reject(ShotRejection.MatchFinished);
            if (player != PlayerId.One && player != PlayerId.Two) return ShotOutcome.Reject(ShotRejection.UnknownPlayer);
            if (player != match.CurrentPlayer) return ShotOutcome.Reject(ShotRejection.NotYourTurn);
            var target = match.GetPlayer(Opponent(player)).Board;
            if (!IsOnBoard(position, target.Size)) return ShotOutcome.Reject(ShotRejection.OutOfBounds);

            if (target.GetCell(position).Shot.HasValue) return ShotOutcome.Reject(ShotRejection.AlreadyShot);
            var result = target.Fire(position);
            if (target.AllShipsSunk) match.Winner = player;
            else match.CurrentPlayer = Opponent(player);
            return ShotOutcome.Accept(result);
        }

    }
}
