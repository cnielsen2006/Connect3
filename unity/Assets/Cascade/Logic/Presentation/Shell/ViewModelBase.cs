using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cascade.Presentation.Shell
{
    /// <summary>
    /// Base of every view-model. The binding convention is the same everywhere (TRD 2.1, 12.2):
    /// <list type="bullet">
    /// <item>All text and numbers a screen shows are read from <b>properties</b> of the view-model
    /// (pull). Properties are computed from the live models, so they are never stale.</item>
    /// <item><see cref="Changed"/> is a coarse "something you show may have changed" event, raised on
    /// the thread that changed the model (the main thread). The binder re-reads the properties it
    /// shows; it does not need to know which one changed.</item>
    /// <item>User actions are <b>methods</b> (<c>Play()</c>, <c>Resume()</c>); they return true when the
    /// command was accepted.</item>
    /// <item>A view-model that subscribes to models is <see cref="IDisposable"/>: dispose it when its
    /// screen is destroyed. Screens that are kept alive for the whole run need not.</item>
    /// </list>
    /// </summary>
    public abstract class ViewModelBase : IDisposable
    {
        readonly List<Action> _unsubscribe = new List<Action>();

        /// <summary>Raised after anything this view-model shows may have changed.</summary>
        public event Action? Changed;

        protected void RaiseChanged() => Changed?.Invoke();

        /// <summary>Registers how to undo a subscription made by a subclass; run by <see cref="Dispose"/>.</summary>
        protected void OnDispose(Action unsubscribe) => _unsubscribe.Add(unsubscribe);

        public void Dispose()
        {
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
            Changed = null;
        }
    }

    /// <summary>
    /// Number formatting for screen text. Always <see cref="CultureInfo.InvariantCulture"/>, so a
    /// player's locale never changes what the game prints (and tests are stable).
    /// </summary>
    public static class Fmt
    {
        /// <summary>A whole number with thousands separators: 12345 becomes "12,345".</summary>
        public static string Int(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A whole number without separators (level numbers, move counts).</summary>
        public static string Plain(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>A decimal number with a fixed number of digits: Fixed(0.5, 2) is "0.50".</summary>
        public static string Fixed(double value, int digits) =>
            value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        /// <summary>The shortest text that round-trips (dial values on the dev page).</summary>
        public static string Round(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A progress pair: Pair(5, 12) is "5/12".</summary>
        public static string Pair(int value, int target) => Int(value) + "/" + Int(target);

        /// <summary>Parses a number typed by the player or developer (invariant culture; no thousands separators).</summary>
        public static bool TryParseDouble(string? text, out double value) =>
            double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value) && !double.IsInfinity(value);

        public static bool TryParseInt(string? text, out int value) =>
            int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
