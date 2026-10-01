using System;
using System.Collections.Generic;
using System.Linq;
using Battleships.Domain;
using NUnit.Framework;

namespace Battleships.Tests
{
    public sealed class GameRulesTests
    {
        private GameRulesConfig config;
        private MatchState match;

        [SetUp]
        public void SetUp()
        {
            config = new GameRulesConfig(6, 3, 2, 2, 1);
            match = GameRules.CreateMatch(config, new Random(104));
        }

        [Test]
        public void BothPlayersHaveExactlyTheRequiredFleetOnSixBySixBoards()
        {
            Assert.That(match.PlayerOne.Board.Size, Is.EqualTo(config.BoardSize));
            foreach (var player in new[] { match.PlayerOne, match.PlayerTwo })
            {
                CollectionAssert.AreEqual(new[] { 3, 2, 2, 1 }, player.Board.Ships.Select(s => s.Positions.Count));
                Assert.That(AllPositions().Count(p => player.Board.GetCell(p).HasShip), Is.EqualTo(8));
                Assert.That(AllPositions().Any(p => player.Board.GetCell(p).Shot.HasValue), Is.False);
            }
        }

        [Test]
        public void SeededPlacementStaysInBoundsStraightAndSeparated()
        {
            for (var seed = 0; seed < 64; seed++)
            {
                var seeded = GameRules.CreateMatch(config, new Random(seed));
                foreach (var board in new[] { seeded.PlayerOne.Board, seeded.PlayerTwo.Board })
                {
                    var occupied = new HashSet<Position>();
                    foreach (var ship in board.Ships)
                    {
                        var horizontal = ship.Positions.All(p => p.Y == ship.Positions[0].Y);
                        var vertical = ship.Positions.All(p => p.X == ship.Positions[0].X);
                        Assert.That(horizontal || vertical, Is.True, $"seed {seed}");
                        for (var i = 0; i < ship.Positions.Count; i++)
                        {
                            var position = ship.Positions[i];
                            Assert.That(GameRules.IsOnBoard(position, config.BoardSize), Is.True);
                            Assert.That(occupied.Add(position), Is.True, "Ships overlap.");
                            if (i > 0)
                                Assert.That(Math.Abs(position.X - ship.Positions[i - 1].X) +
                                            Math.Abs(position.Y - ship.Positions[i - 1].Y), Is.EqualTo(1));
                        }
                    }
                    for (var i = 0; i < board.Ships.Count; i++)
                    for (var j = i + 1; j < board.Ships.Count; j++)
                    foreach (var a in board.Ships[i].Positions)
                    foreach (var b in board.Ships[j].Positions)
                        Assert.That(Math.Abs(a.X - b.X) > 1 || Math.Abs(a.Y - b.Y) > 1,
                                    Is.True, "Different ships touch.");
                }
            }
        }

        [Test]
        public void RandomPlacementIsReproducibleAndVariesAcrossSeeds()
        {
            var same = GameRules.CreateMatch(config, new Random(104));
            var other = GameRules.CreateMatch(config, new Random(105));
            CollectionAssert.AreEqual(Occupied(match.PlayerTwo.Board), Occupied(same.PlayerTwo.Board));
            Assert.That(Occupied(match.PlayerTwo.Board).SequenceEqual(Occupied(other.PlayerTwo.Board)), Is.False);
        }

        [Test]
        public void PlayerOneStarts()
        {
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            Assert.That(match.Winner, Is.Null);
        }

        [Test]
        public void MissRecordsResultAndPassesTurn()
        {
            var position = AllPositions().First(p => !match.PlayerTwo.Board.GetCell(p).HasShip);
            AssertAccepted(GameRules.Fire(match, PlayerId.One, position), ShotResult.miss);
            Assert.That(match.PlayerTwo.Board.GetCell(position).Shot, Is.EqualTo(ShotResult.miss));
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.Two));
        }

        [Test]
        public void HitDoesNotGrantAnotherTurn()
        {
            var position = match.PlayerTwo.Board.Ships[0].Positions[0];
            AssertAccepted(GameRules.Fire(match, PlayerId.One, position), ShotResult.hit);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.Two));
            Assert.That(GameRules.Fire(match, PlayerId.One, match.PlayerTwo.Board.Ships[0].Positions[1]).Rejection,
                        Is.EqualTo(ShotRejection.NotYourTurn));
        }

        [Test]
        public void LastCellOfShipReturnsSunkAndPassesTurn()
        {
            var ship = match.PlayerTwo.Board.Ships[0];
            for (var i = 0; i < ship.Positions.Count; i++)
            {
                AssertAccepted(GameRules.Fire(match, PlayerId.One, ship.Positions[i]),
                               i == ship.Positions.Count - 1 ? ShotResult.sunk : ShotResult.hit);
                Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.Two));
                if (i < ship.Positions.Count - 1)
                {
                    Assert.That(match.PlayerTwo.Board.GetCell(ship.Positions[i]).Shot,
                        Is.EqualTo(ShotResult.hit));
                    ReplyWithMiss();
                }
            }
            Assert.That(ship.IsSunk, Is.True);
            foreach (var position in ship.Positions)
                Assert.That(match.PlayerTwo.Board.GetCell(position).Shot, Is.EqualTo(ShotResult.sunk));
            Assert.That(match.Winner, Is.Null);
        }

        [Test]
        public void SingleCellShipIsSunkImmediately()
        {
            AssertAccepted(GameRules.Fire(match, PlayerId.One, match.PlayerTwo.Board.Ships[3].Positions[0]), ShotResult.sunk);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.Two));
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(6, 0)]
        [TestCase(0, 6)]
        public void OutOfBoundsShotDoesNotConsumeTurn(int x, int y)
        {
            var outcome = GameRules.Fire(match, PlayerId.One, new Position(x, y));
            Assert.That(outcome.Rejection, Is.EqualTo(ShotRejection.OutOfBounds));
            Assert.That(outcome.Result, Is.Null);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            Assert.That(AllPositions().Any(p => match.PlayerTwo.Board.GetCell(p).Shot.HasValue), Is.False);
        }

        [Test]
        public void WrongPlayerShotLeavesBothBoardsUnchanged()
        {
            Assert.That(GameRules.Fire(match, PlayerId.Two, new Position(0, 0)).Rejection,
                        Is.EqualTo(ShotRejection.NotYourTurn));
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            Assert.That(AllPositions().Any(p => match.PlayerOne.Board.GetCell(p).Shot.HasValue ||
                                               match.PlayerTwo.Board.GetCell(p).Shot.HasValue), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedShotDoesNotConsumeTurnOrDamageShipAgain(bool targetShip)
        {
            var position = AllPositions().First(p => match.PlayerTwo.Board.GetCell(p).HasShip == targetShip);
            var first = GameRules.Fire(match, PlayerId.One, position);
            ReplyWithMiss();
            Assert.That(GameRules.Fire(match, PlayerId.One, position).Rejection, Is.EqualTo(ShotRejection.AlreadyShot));
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            Assert.That(match.PlayerTwo.Board.GetCell(position).Shot, Is.EqualTo(first.Result));
        }

        [TestCase(PlayerId.One)]
        [TestCase(PlayerId.Two)]
        public void SinkingEntireEnemyFleetWinsAndRejectsFurtherShots(PlayerId winner)
        {
            var target = match.GetPlayer(GameRules.Opponent(winner)).Board;
            if (winner == PlayerId.Two) PassTurnWithMiss(PlayerId.One);
            var positions = Occupied(target);
            for (var i = 0; i < positions.Length; i++)
            {
                var outcome = GameRules.Fire(match, winner, positions[i]);
                Assert.That(outcome.Accepted, Is.True);
                if (i == positions.Length - 1)
                    Assert.That(outcome.Result, Is.EqualTo(ShotResult.sunk));
                else
                {
                    Assert.That(match.Winner, Is.Null);
                    PassTurnWithMiss(GameRules.Opponent(winner));
                }
            }
            Assert.That(target.AllShipsSunk, Is.True);
            Assert.That(match.Winner, Is.EqualTo(winner));
            var previousPlayer = match.CurrentPlayer;
            Assert.That(GameRules.Fire(match, GameRules.Opponent(winner), new Position(0, 0)).Rejection,
                        Is.EqualTo(ShotRejection.MatchFinished));
            Assert.That(match.CurrentPlayer, Is.EqualTo(previousPlayer));
            Assert.That(match.Winner, Is.EqualTo(winner));
        }

        [Test]
        public void RulesUseProvidedBoardAndFleetConfiguration()
        {
            var custom = new GameRulesConfig(4, 2, 1);
            var customMatch = GameRules.CreateMatch(custom, new Random(7));

            Assert.That(customMatch.PlayerOne.Board.Size, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 2, 1 },
                customMatch.PlayerOne.Board.Ships.Select(ship => ship.Positions.Count));
            Assert.That(GameRules.Fire(customMatch, PlayerId.One, new Position(4, 0)).Rejection,
                Is.EqualTo(ShotRejection.OutOfBounds));
        }

        internal static Position[] Occupied(Board board) =>
            board.Ships.SelectMany(s => s.Positions).ToArray();

        private IEnumerable<Position> AllPositions()
        {
            for (var y = 0; y < config.BoardSize; y++)
            for (var x = 0; x < config.BoardSize; x++) yield return new Position(x, y);
        }

        private void ReplyWithMiss()
        {
            PassTurnWithMiss(PlayerId.Two);
        }

        private void PassTurnWithMiss(PlayerId shooter)
        {
            var target = match.GetPlayer(GameRules.Opponent(shooter)).Board;
            var position = AllPositions().First(p => !target.GetCell(p).HasShip &&
                                                     !target.GetCell(p).Shot.HasValue);
            AssertAccepted(GameRules.Fire(match, shooter, position), ShotResult.miss);
        }

        private static void AssertAccepted(ShotOutcome outcome, ShotResult expected)
        {
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Result, Is.EqualTo(expected));
        }
    }
}
