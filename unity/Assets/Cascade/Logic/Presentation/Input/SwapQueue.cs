using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// The one-slot queue of TRD 12.3: a swap requested while the board is animating waits (a newer request
    /// replaces it, "last one wins") and is played when the board has settled.
    /// <code>
    /// var play = queue.Request(a, b, animating);   // from InputIntent.Swap
    /// if (play != null) session.TrySwap(play.Value);
    /// // when a timeline finishes (Timeline.TotalMs):
    /// var next = queue.TakePending(); if (next != null) session.TrySwap(next.Value);
    /// </code>
    /// </summary>
    public sealed class SwapQueue
    {
        (CellId A, CellId B)? _pending;

        public bool HasPending => _pending != null;
        public (CellId A, CellId B)? Pending => _pending;

        /// <summary>The swap to play now (the board is idle), or null when it was queued (the board is animating).</summary>
        public (CellId A, CellId B)? Request(CellId a, CellId b, bool animating)
        {
            if (animating)
            {
                _pending = (a, b);
                return null;
            }
            _pending = null; // an idle board plays the newest request and drops any stale one
            return (a, b);
        }

        /// <summary>The board settled: the queued swap to play now, if any (it is removed from the queue).</summary>
        public (CellId A, CellId B)? TakePending()
        {
            var p = _pending;
            _pending = null;
            return p;
        }

        /// <summary>Forget the queued swap (a new level started, the player quit).</summary>
        public void Clear() => _pending = null;
    }
}
