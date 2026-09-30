using System;
using System.Linq;
using Battleships.Domain;
using NUnit.Framework;

namespace Battleships.Tests.Server
{
    public sealed class ExpireTurnTests
    {
        [Test]
        public void ExpireTurnSwitchesPlayerWithoutChangingBoards()
        {
            var match = GameRules.CreateMatch(new GameRulesConfig(6, 3, 2, 2, 1), new Random(104));
            Assert.That(GameRules.ExpireTurn(match), Is.True);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.Two));
            Assert.That(GameRules.ExpireTurn(match), Is.True);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
            {
                Assert.That(match.PlayerOne.Board.GetCell(new Position(x, y)).Shot, Is.Null);
                Assert.That(match.PlayerTwo.Board.GetCell(new Position(x, y)).Shot, Is.Null);
            }
        }

        [Test]
        public void ExpireTurnDoesNothingAfterWin()
        {
            var match = GameRules.CreateMatch(new GameRulesConfig(2, 1), new Random(1));
            var target = match.PlayerTwo.Board.Ships.Single().Positions.Single();
            Assert.That(GameRules.Fire(match, PlayerId.One, target).Accepted, Is.True);
            Assert.That(GameRules.ExpireTurn(match), Is.False);
            Assert.That(match.CurrentPlayer, Is.EqualTo(PlayerId.One));
            Assert.That(match.Winner, Is.EqualTo(PlayerId.One));
        }

        [Test]
        public void ExpireTurnRequiresMatch()
        {
            Assert.Throws<ArgumentNullException>(() => GameRules.ExpireTurn(null));
        }
    }
}
