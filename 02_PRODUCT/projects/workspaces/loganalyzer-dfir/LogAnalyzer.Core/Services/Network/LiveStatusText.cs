using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzer.Core.Services.Network
{
    /// <summary>
    /// Decides which status text the live page shows on each drain tick (senior review L10 follow-up): the "repeats were folded" line is informational
    /// and must never hide a dropped-events warning or an error / status text set since the previous tick, and is rewritten only when its count changed.
    /// </summary>
    public sealed class LiveStatusText
    {
        private bool _pinned;
        private int _repeatsShown;

        /// <summary>Call when an error or status text has just been set by the watcher: the next tick leaves it alone.</summary>
        public void Pin() => _pinned = true;

        /// <summary>The text to show after this tick, or null to leave the current text unchanged.</summary>
        public string? Next(int dropped, int suppressed)
        {
            if (dropped > 0)
            {
                _pinned = true;
                return $"Prea multe evenimente deodată: {dropped} nu au mai putut fi evaluate. Pe această stație, monitorizarea live nu este completă.";
            }
            if (_pinned) { _pinned = false; return null; }
            if (suppressed <= 0 || suppressed == _repeatsShown) return null;
            _repeatsShown = suppressed;
            return $"🔴 Monitorizare activă · {suppressed} repetări ale unor alerte deja afișate au fost adăugate la alerta inițială (nu au deschis alerte noi).";
        }

        /// <summary>The newest <paramref name="rows"/> events of a batch (oldest first), the only ones worth inserting in the visible stream.</summary>
        public static IReadOnlyList<T> Newest<T>(IReadOnlyList<T> batch, int rows) => batch.Count <= rows ? batch : batch.Skip(batch.Count - rows).ToList();
    }
}
