using System;
using System.Diagnostics;

namespace Battleships.Server
{
    public interface IServerClock
    {
        long UnixTimeMilliseconds { get; }
    }

    // UTC origin plus monotonic elapsed time prevents wall-clock adjustments from moving deadlines.
    public sealed class SystemServerClock : IServerClock
    {
        private readonly long origin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        public long UnixTimeMilliseconds => checked(origin + elapsed.ElapsedMilliseconds);
    }
}
