using System;
using System.Linq;
using System.Reflection;
using Battleships.Protocol;
using NUnit.Framework;

namespace Battleships.Tests.Protocol
{
    public sealed class ProtocolContractTests
    {
        [Test]
        public void RequiredMessagesAreSerializableDataContracts()
        {
            var messageTypes = new[]
            {
                typeof(JoinRequest),
                typeof(ResumeRequest),
                typeof(FireRequest),
                typeof(HeartbeatRequest),
                typeof(JoinResponse),
                typeof(FireResponse),
                typeof(MatchSnapshot),
                typeof(HeartbeatResponse),
                typeof(ErrorResponse)
            };

            Assert.That(messageTypes.All(type => Attribute.IsDefined(type, typeof(SerializableAttribute))), Is.True);
        }

        [Test]
        public void SnapshotIdentifiesRecipientWithPlayerSlot()
        {
            Assert.That(typeof(MatchSnapshot).GetField(nameof(MatchSnapshot.PlayerSlot)).FieldType,
                Is.EqualTo(typeof(PlayerSlot)));
        }

        [Test]
        public void OpponentProjectionCannotCarryHiddenShipData()
        {
            Assert.That(typeof(MatchSnapshot).GetField(nameof(MatchSnapshot.OwnBoardCells)).FieldType,
                Is.EqualTo(typeof(OwnBoardCell[])));
            Assert.That(typeof(MatchSnapshot).GetField(nameof(MatchSnapshot.OpponentShots)).FieldType,
                Is.EqualTo(typeof(OpponentShotResult[])));

            var opponentFields = typeof(OpponentShotResult)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name);
            CollectionAssert.AreEquivalent(
                new[] { nameof(OpponentShotResult.Position), nameof(OpponentShotResult.Result) },
                opponentFields);
        }
    }
}
