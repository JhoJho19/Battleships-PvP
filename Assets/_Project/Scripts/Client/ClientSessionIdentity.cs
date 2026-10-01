using System;
using Battleships.Protocol;

namespace Battleships.Client
{
    public sealed class ClientSessionIdentity
    {
        public bool HasSession => !string.IsNullOrWhiteSpace(SessionToken);
        public string SessionToken { get; private set; }
        public PlayerSlot PlayerSlot { get; private set; }

        internal bool Apply(JoinResponse response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.SessionToken)) return false;
            if (HasSession)
                return string.Equals(SessionToken, response.SessionToken, StringComparison.Ordinal) &&
                       PlayerSlot == response.PlayerSlot;

            SessionToken = response.SessionToken;
            PlayerSlot = response.PlayerSlot;
            return true;
        }
    }
}
