using System;
using System.Globalization;
using Battleships.Networking;

namespace Battleships.Presentation
{
    public static class DebugLogFormatting
    {
        public static string FormatTimestamp(DateTimeOffset timestamp) =>
            timestamp.ToString("HH':'mm':'ss", CultureInfo.InvariantCulture);

        public static string FormatTransportEntry(TransportLogEntry entry) =>
            $"[{FormatTimestamp(entry.LocalTimestamp)}] " +
            $"{entry.Status.ToString().ToUpperInvariant()} " +
            $"{entry.Direction} {entry.Endpoint} {entry.MessageType}" +
            (entry.DropReason == TransportDropReason.None ? string.Empty : $" ({entry.DropReason})");
    }
}
