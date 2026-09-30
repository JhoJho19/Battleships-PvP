using System;
using System.Collections.Generic;
using Battleships.Domain;

namespace Battleships.Server
{
    internal sealed class PlayerSession
    {
        internal string Token { get; }
        internal PlayerId Player { get; }
        internal string MatchId { get; }

        internal PlayerSession(string token, PlayerId player, string matchId)
        {
            Token = token;
            Player = player;
            MatchId = matchId;
        }
    }

    public sealed class SessionManager
    {
        private readonly Dictionary<string, PlayerSession> sessions =
            new Dictionary<string, PlayerSession>(StringComparer.Ordinal);
        // Join has no session token yet. Its operation ID identifies the initial join in this runtime.
        private readonly Dictionary<string, PlayerSession> joins =
            new Dictionary<string, PlayerSession>(StringComparer.Ordinal);
        private readonly string matchId = Guid.NewGuid().ToString("N");

        internal int Count => sessions.Count;

        internal bool TryJoin(string requestId, out PlayerSession session)
        {
            if (joins.TryGetValue(requestId, out session)) return true;
            if (sessions.Count == 2) return false;
            session = new PlayerSession(Guid.NewGuid().ToString("N"),
                sessions.Count == 0 ? PlayerId.One : PlayerId.Two, matchId);
            sessions.Add(session.Token, session);
            joins.Add(requestId, session);
            return true;
        }

        internal bool TryResolve(string token, out PlayerSession session)
        {
            session = null;
            return !string.IsNullOrWhiteSpace(token) && sessions.TryGetValue(token, out session);
        }
    }
}
