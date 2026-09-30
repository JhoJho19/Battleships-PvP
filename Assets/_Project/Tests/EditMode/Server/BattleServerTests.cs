using System;
using System.Linq;
using System.Reflection;
using Battleships.Domain;
using Battleships.Protocol;
using Battleships.Server;
using NUnit.Framework;

namespace Battleships.Tests.Server
{
    public sealed class BattleServerTests
    {
        private sealed class FakeClock : IServerClock
        {
            public long UnixTimeMilliseconds { get; set; } = 100000;
        }

        private FakeClock clock;
        private BattleServer server;
        private JoinResponse one;
        private JoinResponse two;
        private int requestNumber;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            server = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1), new Random(104), clock);
            one = server.Handle(new JoinRequest { RequestId = "join-one" }).Response;
            two = server.Handle(new JoinRequest { RequestId = "join-two" }).Response;
        }

        [Test]
        public void MatchStartsOnlyAfterSecondJoinAndUsesServerClock()
        {
            var waiting = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1), new Random(104), clock);
            var first = waiting.Handle(new JoinRequest { RequestId = "first" }).Response;
            Assert.That(waiting.Handle(new ResumeRequest { RequestId = "read", SessionToken = first.SessionToken })
                .Error.ErrorCode, Is.EqualTo(ProtocolErrorCode.MatchNotReady));
            Assert.That(waiting.ProcessDeadlines(), Is.False);
            clock.UnixTimeMilliseconds += 20000;
            waiting.Handle(new JoinRequest { RequestId = "second" });
            var snapshot = waiting.Handle(new ResumeRequest
                { RequestId = "read", SessionToken = first.SessionToken }).Response;
            Assert.That(snapshot.StateVersion, Is.EqualTo(1));
            Assert.That(snapshot.TurnId, Is.EqualTo(1));
            Assert.That(snapshot.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(135000));
            Assert.That(snapshot.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerOne));
        }

        [Test]
        public void ValidShotChangesStateOnceAndCreatesANewDeadline()
        {
            var before = Snapshot(one);
            clock.UnixTimeMilliseconds += 1000;
            var result = server.Handle(Fire(one, before, 0, 0));
            Assert.That(result.Accepted, Is.True);
            var after = Snapshot(one);
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(116000));
            Assert.That(after.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(after.OpponentShots.Length, Is.EqualTo(1));
            Assert.That(after.OpponentShots[0].Result, Is.EqualTo(result.Result));
        }

        [Test]
        public void DuplicateOlderThanLastRequestReturnsOriginalResultWithoutMutation()
        {
            var request = Fire(one, Snapshot(one), 0, 0, "original");
            var original = server.Handle(request);
            server.Handle(Fire(two, Snapshot(two), 0, 0, "other"));
            var before = Snapshot(one);
            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds + 1;
            request.Target.X = 5;
            request.TurnId = -100;
            var duplicate = server.Handle(request);
            Assert.That(duplicate.Accepted, Is.EqualTo(original.Accepted));
            Assert.That(duplicate.Result, Is.EqualTo(original.Result));
            Assert.That(duplicate.Target.X, Is.Zero);
            Assert.That(duplicate.StateVersion, Is.EqualTo(original.StateVersion));
            Assert.That(duplicate.TurnId, Is.EqualTo(original.TurnId));
            Assert.That(duplicate.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(original.TurnDeadlineUnixTimeMilliseconds));
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void ResponseMutationCannotCorruptCachedResult()
        {
            var request = Fire(one, Snapshot(one), 0, 0, "copy");
            var response = server.Handle(request);
            var version = response.StateVersion;
            response.Target.X = 99;
            response.Accepted = false;
            response.StateVersion = -1;
            var duplicate = server.Handle(request);
            Assert.That(duplicate.Accepted, Is.True);
            Assert.That(duplicate.StateVersion, Is.EqualTo(version));
            Assert.That(duplicate.Target.X, Is.Zero);
            duplicate.Target.X = 88;
            Assert.That(server.Handle(request).Target.X, Is.Zero);
        }

        [Test]
        public void SameRequestIdIsIndependentForDifferentSessions()
        {
            Assert.That(server.Handle(Fire(one, Snapshot(one), 0, 0, "same")).Accepted, Is.True);
            Assert.That(server.Handle(Fire(two, Snapshot(two), 1, 0, "same")).Accepted, Is.True);
            Assert.That(Snapshot(one).StateVersion, Is.EqualTo(3));
            Assert.That(Snapshot(two).OpponentShots.Length, Is.EqualTo(1));
        }

        [TestCase("stale", ProtocolErrorCode.StaleTurn)]
        [TestCase("wrong-player", ProtocolErrorCode.NotYourTurn)]
        [TestCase("bounds", ProtocolErrorCode.OutOfBounds)]
        [TestCase("target", ProtocolErrorCode.InvalidRequest)]
        [TestCase("session", ProtocolErrorCode.InvalidSession)]
        [TestCase("id", ProtocolErrorCode.InvalidRequest)]
        public void RejectedShotPreservesStateTurnAndDeadline(string kind, ProtocolErrorCode error)
        {
            var before = Snapshot(one);
            var request = Fire(kind == "wrong-player" ? two : one, before, 0, 0);
            if (kind == "stale") request.TurnId--;
            if (kind == "bounds") request.Target.X = 6;
            if (kind == "target") request.Target = null;
            if (kind == "session") request.SessionToken = "unknown";
            if (kind == "id") request.RequestId = " ";
            Assert.That(server.Handle(request).ErrorCode, Is.EqualTo(error));
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void CachedRejectionDoesNotBecomeAcceptedInALaterTurn()
        {
            var rejected = Fire(two, Snapshot(two), 0, 0, "rejected");
            var result = server.Handle(rejected);
            Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCode.NotYourTurn));
            server.Handle(Fire(one, Snapshot(one), 0, 0));
            var before = Snapshot(two);
            rejected.TurnId = before.TurnId;
            Assert.That(server.Handle(rejected).ErrorCode, Is.EqualTo(ProtocolErrorCode.NotYourTurn));
            AssertUnchanged(before, Snapshot(two));
        }

        [Test]
        public void AlreadyShotRejectsWithoutChangingState()
        {
            server.Handle(Fire(one, Snapshot(one), 0, 0));
            server.Handle(Fire(two, Snapshot(two), 0, 0));
            var before = Snapshot(one);
            Assert.That(server.Handle(Fire(one, before, 0, 0)).ErrorCode,
                Is.EqualTo(ProtocolErrorCode.AlreadyShot));
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void SessionTokensResolveTheirOriginalPlayersAndMatch()
        {
            Assert.That(one.SessionToken, Is.Not.EqualTo(two.SessionToken));
            Assert.That(Snapshot(one).PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(Snapshot(two).PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
            server.Handle(Fire(one, Snapshot(one), 0, 0));
            Assert.That(Snapshot(one).StateVersion, Is.EqualTo(Snapshot(two).StateVersion));
            Assert.That(server.Handle(new ResumeRequest { RequestId = "r", SessionToken = "bad" })
                .Error.ErrorCode, Is.EqualTo(ProtocolErrorCode.InvalidSession));
            Assert.That(server.Handle(new HeartbeatRequest { SessionToken = one.SessionToken }).IsSuccess, Is.True);
            Assert.That(server.Handle(new HeartbeatRequest { SessionToken = "bad" }).IsSuccess, Is.False);
        }

        [Test]
        public void JoinRetryKeepsIdentityAndDoesNotCreateOrRestartMatch()
        {
            var before = Snapshot(one);
            var retry = server.Handle(new JoinRequest { RequestId = "join-one" });
            Assert.That(retry.Response.SessionToken, Is.EqualTo(one.SessionToken));
            Assert.That(retry.Response.PlayerSlot, Is.EqualTo(one.PlayerSlot));
            Assert.That(server.Handle(new JoinRequest { RequestId = "third" }).IsSuccess, Is.False);
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void TypedResultsExposeExactlyOneBranchAndNullRequestsAreRejected()
        {
            var success = server.Handle(new HeartbeatRequest { SessionToken = one.SessionToken });
            Assert.That(success.Response, Is.Not.Null);
            Assert.That(success.Error, Is.Null);
            var failure = server.Handle((JoinRequest)null);
            Assert.That(failure.Response, Is.Null);
            Assert.That(failure.Error.ErrorCode, Is.EqualTo(ProtocolErrorCode.InvalidRequest));
            Assert.That(server.Handle((ResumeRequest)null).IsSuccess, Is.False);
            Assert.That(server.Handle((HeartbeatRequest)null).IsSuccess, Is.False);
            Assert.That(server.Handle((FireRequest)null).Accepted, Is.False);
        }

        [Test]
        public void SnapshotsShowOwnFleetAndOnlyRevealedOpponentShotResults()
        {
            var expected = GameRules.CreateMatch(new GameRulesConfig(6, 3, 2, 2, 1), new Random(104));
            foreach (var player in new[] { one, two })
            {
                var snapshot = Snapshot(player);
                var board = expected.GetPlayer(player == one ? PlayerId.One : PlayerId.Two).Board;
                Assert.That(snapshot.OwnBoardCells.Length, Is.EqualTo(36));
                foreach (var cell in snapshot.OwnBoardCells)
                    Assert.That(cell.HasShip, Is.EqualTo(board.GetCell(
                        new Position(cell.Position.X, cell.Position.Y)).HasShip));
                Assert.That(snapshot.OpponentShots, Is.Empty);
            }
            var target = Snapshot(two).OwnBoardCells.First(cell => cell.HasShip).Position;
            var response = server.Handle(Fire(one, Snapshot(one), target.X, target.Y));
            var oneView = Snapshot(one);
            var twoView = Snapshot(two);
            Assert.That(oneView.OpponentShots.Length, Is.EqualTo(1));
            Assert.That(oneView.OpponentShots[0].Position.X, Is.EqualTo(target.X));
            Assert.That(oneView.OpponentShots[0].Position.Y, Is.EqualTo(target.Y));
            Assert.That(oneView.OpponentShots[0].Result, Is.EqualTo(response.Result));
            Assert.That(twoView.OpponentShots, Is.Empty);
            var ownHit = twoView.OwnBoardCells.Single(cell =>
                cell.Position.X == target.X && cell.Position.Y == target.Y);
            Assert.That(ownHit.HasShip, Is.True);
            Assert.That(ownHit.HasShotResult, Is.True);
            Assert.That(ownHit.ShotResult, Is.EqualTo(response.Result));
            CollectionAssert.AreEquivalent(new[] { "Position", "Result" },
                typeof(OpponentShotResult).GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Select(field => field.Name));
            Assert.That(typeof(BattleServer).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.ReturnType == typeof(MatchState)), Is.False);
        }

        [Test]
        public void MutatingASnapshotCannotChangeAuthoritativeStateOrAnotherSnapshot()
        {
            var before = Snapshot(one);
            var detached = Snapshot(one);
            detached.OwnBoardCells[0].HasShip = !detached.OwnBoardCells[0].HasShip;
            detached.OwnBoardCells[0].Position.X = 99;
            detached.OpponentShots = new[] { new OpponentShotResult() };
            detached.StateVersion = 999;
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void DeadlineBoundaryRejectsFireWithoutAutomaticallyProcessingTimeout()
        {
            var before = Snapshot(one);
            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;
            Assert.That(server.Handle(Fire(one, before, 0, 0)).ErrorCode,
                Is.EqualTo(ProtocolErrorCode.TurnExpired));
            AssertUnchanged(before, Snapshot(one));
        }

        [Test]
        public void ProcessingTimeoutCreatesExactlyOneNewTurnAndDeadlineFromServerTime()
        {
            var before = Snapshot(one);
            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds - 1;
            Assert.That(server.ProcessDeadlines(), Is.False);
            AssertUnchanged(before, Snapshot(one));
            clock.UnixTimeMilliseconds += 1;
            Assert.That(server.ProcessDeadlines(), Is.True);
            var after = Snapshot(one);
            Assert.That(after.StateVersion, Is.EqualTo(2));
            Assert.That(after.TurnId, Is.EqualTo(2));
            Assert.That(after.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(130000));
            Assert.That(after.OpponentShots, Is.Empty);
            Assert.That(server.ProcessDeadlines(), Is.False);
            AssertUnchanged(after, Snapshot(one));

            clock.UnixTimeMilliseconds = 200000;
            Assert.That(server.ProcessDeadlines(), Is.True);
            after = Snapshot(one);
            Assert.That(after.StateVersion, Is.EqualTo(3));
            Assert.That(after.TurnId, Is.EqualTo(3));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(215000));
        }

        [Test]
        public void TerminalShotChangesVersionButKeepsTurnAndDeadlineAndStopsTimeouts()
        {
            var targets = Snapshot(two).OwnBoardCells.Where(cell => cell.HasShip)
                .Select(cell => cell.Position).ToArray();
            foreach (var target in targets)
            {
                var before = Snapshot(one);
                clock.UnixTimeMilliseconds += 1;
                var response = server.Handle(Fire(one, before, target.X, target.Y));
                Assert.That(response.Accepted, Is.True);
                if (response.HasWinner)
                {
                    Assert.That(response.Winner, Is.EqualTo(PlayerSlot.PlayerOne));
                    Assert.That(response.StateVersion, Is.EqualTo(before.StateVersion + 1));
                    Assert.That(response.TurnId, Is.EqualTo(before.TurnId));
                    Assert.That(response.TurnDeadlineUnixTimeMilliseconds,
                        Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
                }
                else
                {
                    var opponent = Snapshot(two);
                    var replyTarget = Snapshot(one).OwnBoardCells.First(cell => !cell.HasShip && !cell.HasShotResult);
                    Assert.That(server.Handle(Fire(two, opponent,
                        replyTarget.Position.X, replyTarget.Position.Y)).Accepted, Is.True);
                }
            }
            var finished = Snapshot(one);
            Assert.That(finished.HasWinner, Is.True);
            clock.UnixTimeMilliseconds = finished.TurnDeadlineUnixTimeMilliseconds + 100000;
            Assert.That(server.ProcessDeadlines(), Is.False);
            Assert.That(server.Handle(Fire(one, finished, 5, 5)).ErrorCode,
                Is.EqualTo(ProtocolErrorCode.MatchFinished));
            AssertUnchanged(finished, Snapshot(one));
        }

        [Test]
        public void ResumeAndHeartbeatAreReadsAndNeverAdvanceTimeState()
        {
            var before = Snapshot(one);
            clock.UnixTimeMilliseconds += 100000;
            server.Handle(new HeartbeatRequest { SessionToken = one.SessionToken });
            AssertUnchanged(before, Snapshot(one));
        }

        private MatchSnapshot Snapshot(JoinResponse player) =>
            server.Handle(new ResumeRequest { RequestId = "snapshot", SessionToken = player.SessionToken }).Response;

        private FireRequest Fire(JoinResponse player, MatchSnapshot snapshot, int x, int y, string id = null) =>
            new FireRequest
            {
                RequestId = id ?? "fire-" + ++requestNumber,
                SessionToken = player.SessionToken,
                TurnId = snapshot.TurnId,
                Target = new BoardPosition { X = x, Y = y }
            };

        private static void AssertUnchanged(MatchSnapshot before, MatchSnapshot after)
        {
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
            Assert.That(after.CurrentPlayer, Is.EqualTo(before.CurrentPlayer));
            Assert.That(after.HasWinner, Is.EqualTo(before.HasWinner));
            Assert.That(after.Winner, Is.EqualTo(before.Winner));
            CollectionAssert.AreEqual(before.OwnBoardCells.Select(CellKey), after.OwnBoardCells.Select(CellKey));
            CollectionAssert.AreEqual(before.OpponentShots.Select(ShotKey), after.OpponentShots.Select(ShotKey));
        }

        private static string CellKey(OwnBoardCell cell) =>
            $"{cell.Position.X},{cell.Position.Y}:{cell.HasShip}:{cell.HasShotResult}:{cell.ShotResult}";
        private static string ShotKey(OpponentShotResult shot) =>
            $"{shot.Position.X},{shot.Position.Y}:{shot.Result}";
    }
}
