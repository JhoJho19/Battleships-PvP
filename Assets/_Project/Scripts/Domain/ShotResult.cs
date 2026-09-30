namespace Battleships.Domain
{
    public enum ShotResult { miss, hit, sunk }
    public enum PlayerId { One, Two }

    public enum ShotRejection
    {
        None, UnknownPlayer, OutOfBounds, NotYourTurn, AlreadyShot, MatchFinished
    }

    public readonly struct ShotOutcome
    {
        public bool Accepted => Rejection == ShotRejection.None;
        public ShotResult? Result { get; }
        public ShotRejection Rejection { get; }

        private ShotOutcome(ShotResult? result, ShotRejection rejection)
        {
            Result = result;
            Rejection = rejection;
        }

        public static ShotOutcome Accept(ShotResult result) => new ShotOutcome(result, ShotRejection.None);
        public static ShotOutcome Reject(ShotRejection reason) => new ShotOutcome(null, reason);
    }
}
