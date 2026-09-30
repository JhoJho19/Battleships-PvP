using System;
using System.Collections.Generic;

namespace Battleships.Domain
{
    // Randomized finite backtracking avoids an unbounded retry loop on a small board.
    internal static class FleetPlacement
    {
        internal static Board Create(GameRulesConfig config, Random random)
        {
            var placements = new Position[config.ShipLengths.Count][];
            if (!Place(0, placements, new bool[config.BoardSize, config.BoardSize], config, random))
                throw new InvalidOperationException("The specified fleet cannot fit on the board.");
            return new Board(config.BoardSize, placements);
        }

        private static bool Place(int index, Position[][] placements, bool[,] occupied,
                                  GameRulesConfig config, Random random)
        {
            if (index == placements.Length) return true;
            var length = config.ShipLengths[index];
            var candidates = new List<Position[]>();
            for (var y = 0; y < config.BoardSize; y++)
            for (var x = 0; x < config.BoardSize; x++)
            for (var orientation = 0; orientation < (length == 1 ? 1 : 2); orientation++)
            {
                var positions = new Position[length];
                for (var cell = 0; cell < length; cell++)
                    positions[cell] = new Position(x + (orientation == 0 ? cell : 0),
                                                  y + (orientation == 1 ? cell : 0));
                if (Fits(positions, occupied, config.BoardSize)) candidates.Add(positions);
            }

            for (var i = candidates.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temporary = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = temporary;
            }

            foreach (var candidate in candidates)
            {
                placements[index] = candidate;
                SetOccupied(candidate, occupied, true);
                if (Place(index + 1, placements, occupied, config, random)) return true;
                SetOccupied(candidate, occupied, false);
            }
            return false;
        }

        private static bool Fits(Position[] positions, bool[,] occupied, int boardSize)
        {
            foreach (var position in positions)
            {
                if (!GameRules.IsOnBoard(position, boardSize)) return false;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var neighbor = new Position(position.X + dx, position.Y + dy);
                    if (GameRules.IsOnBoard(neighbor, boardSize) && occupied[neighbor.X, neighbor.Y]) return false;
                }
            }
            return true;
        }

        private static void SetOccupied(Position[] positions, bool[,] occupied, bool value)
        {
            foreach (var position in positions) occupied[position.X, position.Y] = value;
        }
    }
}
