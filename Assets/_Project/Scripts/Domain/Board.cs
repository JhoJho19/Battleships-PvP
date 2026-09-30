using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Battleships.Domain
{
    public readonly struct Cell
    {
        public Position Position { get; }
        public bool HasShip { get; }
        public ShotResult? Shot { get; }

        internal Cell(Position position, bool hasShip, ShotResult? shot)
        {
            Position = position;
            HasShip = hasShip;
            Shot = shot;
        }
    }

    public sealed class Ship
    {
        private readonly HashSet<Position> hits = new HashSet<Position>();
        public ReadOnlyCollection<Position> Positions { get; }
        public bool IsSunk => hits.Count == Positions.Count;

        internal Ship(Position[] positions)
        {
            Positions = Array.AsReadOnly((Position[])positions.Clone());
        }

        internal void Hit(Position position) => hits.Add(position);
    }

    public sealed class Board
    {
        private readonly Ship[,] occupants;
        private readonly ShotResult?[,] shots;
        public int Size { get; }
        public ReadOnlyCollection<Ship> Ships { get; }

        internal Board(int size, Position[][] placements)
        {
            Size = size;
            occupants = new Ship[size, size];
            shots = new ShotResult?[size, size];
            var ships = new Ship[placements.Length];
            for (var i = 0; i < placements.Length; i++)
            {
                ships[i] = new Ship(placements[i]);
                foreach (var position in placements[i])
                    occupants[position.X, position.Y] = ships[i];
            }
            Ships = Array.AsReadOnly(ships);
        }

        public Cell GetCell(Position position)
        {
            if (!GameRules.IsOnBoard(position, Size))
                throw new ArgumentOutOfRangeException(nameof(position));
            return new Cell(position, occupants[position.X, position.Y] != null, shots[position.X, position.Y]);
        }

        public bool AllShipsSunk
        {
            get
            {
                foreach (var ship in Ships)
                    if (!ship.IsSunk) return false;
                return true;
            }
        }

        internal ShotResult Fire(Position position)
        {
            var ship = occupants[position.X, position.Y];
            var result = ShotResult.miss;
            if (ship != null)
            {
                ship.Hit(position);
                result = ship.IsSunk ? ShotResult.sunk : ShotResult.hit;
            }
            shots[position.X, position.Y] = result;
            return result;
        }
    }
}
