using System.Collections.Generic;
using Battleships.Networking;
using TMPro;
using UnityEngine;

namespace Battleships.Presentation
{
    public sealed class TransportLogView : MonoBehaviour
    {
        [SerializeField] private TMP_Text logText;
        [SerializeField] private int maximumLines = 12;

        private readonly Queue<string> lines = new Queue<string>();
        private int renderedEntries;

        public void Render(IReadOnlyList<TransportLogEntry> entries)
        {
            while (renderedEntries < entries.Count)
            {
                var entry = entries[renderedEntries++];
                if (lines.Count == maximumLines) lines.Dequeue();
                lines.Enqueue($"[{entry.TimestampMilliseconds / 1000d:00.000}] " +
                              $"{entry.Status.ToString().ToUpperInvariant()} " +
                              $"{entry.Direction} {entry.Endpoint} {entry.MessageType}" +
                              (entry.DropReason == TransportDropReason.None ? string.Empty : $" ({entry.DropReason})"));
            }
            logText.text = string.Join("\n", lines);
        }
    }
}
