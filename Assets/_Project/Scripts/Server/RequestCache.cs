using System;
using System.Collections.Generic;
using Battleships.Protocol;

namespace Battleships.Server
{
    public sealed class RequestCache
    {
        // One cache per BattleServer/current match. Entries live until that runtime is released.
        // Never evict an older operation while the session can retry it.
        private readonly Dictionary<string, Dictionary<string, FireResponse>> responses =
            new Dictionary<string, Dictionary<string, FireResponse>>(StringComparer.Ordinal);

        internal bool TryGet(string sessionToken, string requestId, out FireResponse response)
        {
            response = null;
            if (!responses.TryGetValue(sessionToken, out var sessionRequests) ||
                !sessionRequests.TryGetValue(requestId, out var cached)) return false;
            response = Copy(cached);
            return true;
        }

        internal void Store(string sessionToken, FireResponse response)
        {
            if (!responses.TryGetValue(sessionToken, out var sessionRequests))
            {
                sessionRequests = new Dictionary<string, FireResponse>(StringComparer.Ordinal);
                responses.Add(sessionToken, sessionRequests);
            }
            sessionRequests.Add(response.RequestId, Copy(response));
        }

        private static FireResponse Copy(FireResponse value) => new FireResponse
        {
            RequestId = value.RequestId,
            Accepted = value.Accepted,
            ErrorCode = value.ErrorCode,
            Target = value.Target == null ? null : new BoardPosition { X = value.Target.X, Y = value.Target.Y },
            Result = value.Result,
            CurrentPlayer = value.CurrentPlayer,
            HasWinner = value.HasWinner,
            Winner = value.Winner,
            TurnId = value.TurnId,
            StateVersion = value.StateVersion,
            TurnDeadlineUnixTimeMilliseconds = value.TurnDeadlineUnixTimeMilliseconds
        };
    }
}
