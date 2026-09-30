using Battleships.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Battleships.Tests
{
    public sealed class GameConfigTests
    {
        [Test]
        public void DefaultsMatchProjectPlan()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                Assert.That(config.BoardSize, Is.EqualTo(6));
                CollectionAssert.AreEqual(new[] { 3, 2, 2, 1 }, config.ShipLengths);
                Assert.That(config.TurnDurationSeconds, Is.EqualTo(15f));
                Assert.That(config.LatencyMilliseconds, Is.Zero);
                Assert.That(config.JitterMilliseconds, Is.Zero);
                Assert.That(config.LossRate, Is.Zero);
                Assert.That(config.DuplicateRate, Is.Zero);
                Assert.That(config.HeartbeatIntervalSeconds, Is.EqualTo(1f));
                Assert.That(config.HeartbeatTimeoutSeconds, Is.EqualTo(5f));

                var rules = config.CreateGameRulesConfig();
                Assert.That(rules.BoardSize, Is.EqualTo(6));
                CollectionAssert.AreEqual(new[] { 3, 2, 2, 1 }, rules.ShipLengths);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
