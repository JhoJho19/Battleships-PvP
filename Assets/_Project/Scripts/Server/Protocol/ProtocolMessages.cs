using System;

namespace Battleships.Protocol
{
    public enum PlayerSlot
    {
        PlayerOne,
        PlayerTwo
    }

    public enum ShotResultCode
    {
        Miss,
        Hit,
        Sunk
    }

    public enum ProtocolErrorCode
    {
        None,
        InvalidRequest,
        InvalidSession,
        MatchNotReady,
        StaleTurn,
        TurnExpired,
        NotYourTurn,
        OutOfBounds,
        AlreadyShot,
        MatchFinished
    }

    [Serializable]
    public sealed class BoardPosition
    {
        public int X;
        public int Y;
    }

    [Serializable]
    public sealed class JoinRequest
    {
        public string RequestId;
    }

    [Serializable]
    public sealed class ResumeRequest
    {
        public string RequestId;
        public string SessionToken;
    }

    [Serializable]
    public sealed class FireRequest
    {
        public string RequestId;
        public string SessionToken;
        public long TurnId;
        public BoardPosition Target;
    }

    [Serializable]
    public sealed class HeartbeatRequest
    {
        public string SessionToken;
    }

    [Serializable]
    public sealed class JoinResponse
    {
        public string RequestId;
        public string SessionToken;
        public PlayerSlot PlayerSlot;
    }

    [Serializable]
    public sealed class FireResponse
    {
        public string RequestId;
        public bool Accepted;
        public ProtocolErrorCode ErrorCode;
        public BoardPosition Target;
        public ShotResultCode Result;
        public PlayerSlot CurrentPlayer;
        public bool HasWinner;
        public PlayerSlot Winner;
        public long TurnId;
        public long StateVersion;
        public long TurnDeadlineUnixTimeMilliseconds;
    }

    [Serializable]
    public sealed class OwnBoardCell
    {
        public BoardPosition Position;
        public bool HasShip;
        public bool HasShotResult;
        public ShotResultCode ShotResult;
    }

    [Serializable]
    public sealed class OpponentShotResult
    {
        public BoardPosition Position;
        public ShotResultCode Result;
    }

    [Serializable]
    public sealed class MatchSnapshot
    {
        public PlayerSlot PlayerSlot;
        public int BoardSize;
        public OwnBoardCell[] OwnBoardCells;
        public OpponentShotResult[] OpponentShots;
        public PlayerSlot CurrentPlayer;
        public bool HasWinner;
        public PlayerSlot Winner;
        public long TurnId;
        public long StateVersion;
        public long TurnDeadlineUnixTimeMilliseconds;
    }

    [Serializable]
    public sealed class HeartbeatResponse
    {
    }

    [Serializable]
    public sealed class ErrorResponse
    {
        public string RequestId;
        public ProtocolErrorCode ErrorCode;
    }
}
