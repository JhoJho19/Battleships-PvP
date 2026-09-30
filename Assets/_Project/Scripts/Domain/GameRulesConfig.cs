using System;
using System.Collections.ObjectModel;

namespace Battleships.Domain
{
    public sealed class GameRulesConfig
    {
        public int BoardSize { get; }
        public ReadOnlyCollection<int> ShipLengths { get; }

        public GameRulesConfig(int boardSize, params int[] shipLengths)
        {
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));
            if (shipLengths == null) throw new ArgumentNullException(nameof(shipLengths));
            if (shipLengths.Length == 0) throw new ArgumentException("At least one ship is required.", nameof(shipLengths));

            var copy = (int[])shipLengths.Clone();
            foreach (var length in copy)
            {
                if (length <= 0 || length > boardSize)
                    throw new ArgumentOutOfRangeException(nameof(shipLengths),
                        "Every ship length must be positive and fit on the board.");
            }

            BoardSize = boardSize;
            ShipLengths = Array.AsReadOnly(copy);
        }
    }
}
