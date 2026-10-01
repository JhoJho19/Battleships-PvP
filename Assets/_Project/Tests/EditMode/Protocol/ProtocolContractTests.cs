using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
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

        [Test]
        public void ExistingMessagesSupportXmlSerializationRoundTripWithoutContractChanges()
        {
            var messages = new object[]
            {
                new JoinRequest { RequestId = "join" },
                new ResumeRequest { RequestId = "resume", SessionToken = "session" },
                new FireRequest
                {
                    RequestId = "fire",
                    SessionToken = "session",
                    TurnId = 7,
                    Target = new BoardPosition { X = 2, Y = 4 }
                },
                new HeartbeatRequest { SessionToken = "session" },
                new JoinResponse
                {
                    RequestId = "join",
                    SessionToken = "session",
                    PlayerSlot = PlayerSlot.PlayerTwo
                },
                new FireResponse
                {
                    RequestId = "fire",
                    Accepted = true,
                    Target = new BoardPosition { X = 2, Y = 4 },
                    Result = ShotResultCode.Sunk,
                    CurrentPlayer = PlayerSlot.PlayerTwo,
                    HasWinner = true,
                    Winner = PlayerSlot.PlayerOne,
                    TurnId = 7,
                    StateVersion = 9,
                    TurnDeadlineUnixTimeMilliseconds = 123456
                },
                new MatchSnapshot
                {
                    PlayerSlot = PlayerSlot.PlayerOne,
                    BoardSize = 6,
                    OwnBoardCells = new[]
                    {
                        new OwnBoardCell
                        {
                            Position = new BoardPosition { X = 1, Y = 3 },
                            HasShip = true,
                            HasShotResult = true,
                            ShotResult = ShotResultCode.Hit
                        }
                    },
                    OpponentShots = new[]
                    {
                        new OpponentShotResult
                        {
                            Position = new BoardPosition { X = 5, Y = 0 },
                            Result = ShotResultCode.Miss
                        }
                    },
                    CurrentPlayer = PlayerSlot.PlayerTwo,
                    TurnId = 11,
                    StateVersion = 13,
                    TurnDeadlineUnixTimeMilliseconds = 654321
                },
                new HeartbeatResponse(),
                new ErrorResponse { RequestId = "bad", ErrorCode = ProtocolErrorCode.StaleTurn }
            };

            foreach (var message in messages)
            {
                var serializer = new XmlSerializer(message.GetType());
                byte[] payload;
                using (var stream = new MemoryStream())
                {
                    serializer.Serialize(stream, message);
                    payload = stream.ToArray();
                }

                object copy;
                using (var stream = new MemoryStream(payload))
                    copy = serializer.Deserialize(stream);

                Assert.That(copy, Is.TypeOf(message.GetType()), message.GetType().Name);
                Assert.That(Encoding.UTF8.GetString(payload), Does.Contain(message.GetType().Name));
            }
        }
    }
}
