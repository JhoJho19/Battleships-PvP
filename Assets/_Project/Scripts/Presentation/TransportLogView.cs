using System.Collections.Generic;
using Battleships.Networking;
using UnityEngine;

namespace Battleships.Presentation
{
    public sealed class TransportLogView : MonoBehaviour
    {
        [SerializeField] private LogScrollView log;
        [SerializeField] private int maximumLines = 200;

        private readonly Queue<string> lines = new Queue<string>();
        private int renderedEntries;
        private bool hasRendered;

        public void Render(IReadOnlyList<TransportLogEntry> entries)
        {
            var firstRender = !hasRendered;
            if (!firstRender && renderedEntries == entries.Count) return;
            hasRendered = true;
            while (renderedEntries < entries.Count)
            {
                var entry = entries[renderedEntries++];
                if (lines.Count == maximumLines) lines.Dequeue();
                lines.Enqueue(DebugLogFormatting.FormatTransportEntry(entry));
            }
            log.Render(string.Join("\n", lines), resetScroll: firstRender);
        }
    }
}
