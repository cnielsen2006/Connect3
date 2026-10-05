using System;
using System.Collections.Generic;
using System.Globalization;
using Cascade.Core;

namespace Cascade.Gameplay
{
    /// <summary>Where a level stands (PRD §2.5).</summary>
    public enum SessionStatus { Playing, Won, Lost }

    /// <summary>What <see cref="Session.TrySwap"/> did with a swap request.</summary>
    public enum MoveOutcome
    {
        /// <summary>The swap was legal: the board changed and a move was used.</summary>
        Played,
        /// <summary>The swap was refused (not neighbors, a hole, or no match). Nothing changed and no move was used.</summary>
        Rejected,
        /// <summary>The level is already over; nothing happened.</summary>
        Ignored,
    }

    /// <summary>
    /// Everything a view needs to animate one swap request: the board before, the Core events, the
    /// board after, and the state of the level after the move. The boards are snapshots the session
    /// never mutates again (a played move produces a new board); treat them as read-only.
    /// For <see cref="MoveOutcome.Rejected"/> and <see cref="MoveOutcome.Ignored"/>,
    /// <see cref="Before"/> and <see cref="After"/> are the same board.
    /// </summary>
    public sealed record MoveResult(
        MoveOutcome Outcome,
        Board Before,
        IReadOnlyList<Event> Events,
        Board After,
        int ScoreDelta,
        int Score,
        ValueList<int> Progress,
        int MovesLeft,
        SessionStatus Status)
    {
        /// <summary>True if the move was played (a move was used).</summary>
        public bool Played => Outcome == MoveOutcome.Played;
    }

    /// <summary>
    /// A running level (TRD §2): applies swap intents to Core, and tracks moves left, goal progress,
    /// score and win/lose. The view talks to this; the bot drives it too. Not thread-safe; one owner
    /// at a time.
    /// </summary>
    public sealed class Session
    {
        readonly GoalTracker _tracker;
        IReadOnlyList<(CellId A, CellId B)>? _legal;

        /// <summary>
        /// Starts a level. <paramref name="streams"/> defaults to <c>new Streams(level.Seed)</c>, used
        /// for the starting board (the <c>tiles</c> stream) and then for play. Pass
        /// <paramref name="board"/> to start from an existing board (the bot does, so every run shares
        /// one initial board while using different play streams); the session takes ownership of it.
        /// </summary>
        public Session(Level level, RuleSet rules, MoveWeights? weights = null, Streams? streams = null, Board? board = null)
        {
            Level = level;
            Rules = rules;
            Weights = weights ?? new MoveWeights();
            Streams = streams ?? new Streams(level.Seed);
            Board = board ?? level.BuildBoard(Streams);
            MovesLeft = level.MoveLimit;
            _tracker = new GoalTracker(level.Goals, Board);
            Status = SessionStatus.Playing;
        }

        Session(Session other)
        {
            Level = other.Level;
            Rules = other.Rules;
            Weights = other.Weights;
            Streams = other.Streams.Copy();
            Board = other.Board.Copy();
            MovesLeft = other.MovesLeft;
            _tracker = other._tracker.Copy();
            Status = other.Status;
            _legal = other._legal;
        }

        // -- state -----------------------------------------------------------------------------

        public Level Level { get; }

        /// <summary>The rule numbers used for every move from now on. Assign a new snapshot after a live dial change.</summary>
        public RuleSet Rules { get; set; }

        /// <summary>The move value weights used by <see cref="Hint"/> and <see cref="BestMoves"/>.</summary>
        public MoveWeights Weights { get; set; }

        /// <summary>The play RNG streams (advance as moves are played).</summary>
        public Streams Streams { get; }

        /// <summary>The current board. A played move replaces it with a new object.</summary>
        public Board Board { get; private set; }

        public int MovesLeft { get; private set; }
        public int MovesUsed => Level.MoveLimit - MovesLeft;
        public SessionStatus Status { get; private set; }
        public bool IsPlaying => Status == SessionStatus.Playing;
        public int Score => _tracker.Score;

        /// <summary>The level's goals.</summary>
        public IReadOnlyList<Goal> Goals => Level.Goals;

        /// <summary>Per-goal progress, clamped to each goal's target (parallel to <see cref="Goals"/>).</summary>
        public ValueList<int> Progress => _tracker.Progress();

        /// <summary>Cleared tile counts by color index.</summary>
        public IReadOnlyList<int> Collected => _tracker.Collected;

        /// <summary>Total tiles cleared, all colors.</summary>
        public int TilesCollected
        {
            get
            {
                int n = 0;
                foreach (int c in _tracker.Collected) n += c;
                return n;
            }
        }

        /// <summary>Colors of unfinished collect goals, ascending.</summary>
        public IReadOnlyList<int> RemainingColors => _tracker.RemainingColors();

        /// <summary>0 until the level is won; then 1, 2 or 3 by the level's score thresholds.</summary>
        public int Stars
        {
            get
            {
                if (Status != SessionStatus.Won) return 0;
                if (Score >= Level.Star3) return 3;
                if (Score >= Level.Star2) return 2;
                return 1;
            }
        }

        // -- play ------------------------------------------------------------------------------

        /// <summary>
        /// Tries swapping cells a and b. A rejected swap costs nothing and returns
        /// <see cref="MoveOutcome.Rejected"/> with a single <see cref="SwapRejected"/> event; once the
        /// level is over nothing happens (<see cref="MoveOutcome.Ignored"/>, no events). Never throws
        /// for bad cells.
        /// </summary>
        public MoveResult TrySwap(CellId a, CellId b)
        {
            var before = Board;
            if (Status != SessionStatus.Playing) return Result(MoveOutcome.Ignored, before, new List<Event>(), 0);
            var topo = before.Topology;
            bool ok = topo.Contains(a) && topo.Contains(b) && before.TileAt(a) != null && before.TileAt(b) != null;
            if (ok)
            {
                ok = false;
                foreach (var n in topo.Neighbors(a)) if (n == b) { ok = true; break; }
            }
            if (!ok) return Result(MoveOutcome.Rejected, before, new List<Event> { new SwapRejected(a, b) }, 0);

            var (board, events) = Core.Rules.Resolve(before, a, b, Streams, Rules);
            if (events.Count > 0 && events[0] is SwapRejected) return Result(MoveOutcome.Rejected, before, events, 0);

            int scoreBefore = _tracker.Score;
            Board = board;
            _legal = board.LegalCache;
            _tracker.Apply(events);
            MovesLeft--;
            if (_tracker.Met()) Status = SessionStatus.Won;
            else if (MovesLeft <= 0) Status = SessionStatus.Lost;
            return Result(MoveOutcome.Played, before, events, _tracker.Score - scoreBefore);
        }

        MoveResult Result(MoveOutcome outcome, Board before, IReadOnlyList<Event> events, int scoreDelta) =>
            new MoveResult(outcome, before, events, Board, scoreDelta, _tracker.Score, Progress, MovesLeft, Status);

        // -- moves and hints -------------------------------------------------------------------

        /// <summary>Every legal swap on the current board (neighbor swaps that match, plus special pairs).</summary>
        public IReadOnlyList<(CellId A, CellId B)> LegalMoves => _legal ??= Moves.FindLegal(Board);

        /// <summary>The legal moves that tie for the highest shallow value (<see cref="MoveEvaluator"/>).</summary>
        public List<(CellId A, CellId B)> BestMoves()
        {
            var legal = LegalMoves;
            var wanted = _tracker.RemainingColors();
            var scratch = Board.Copy(); // the evaluator swaps in place; keep Board untouched
            var values = new double[legal.Count];
            double top = 0; // max of values, default 0
            for (int i = 0; i < legal.Count; i++)
            {
                values[i] = MoveEvaluator.Value(scratch, legal[i].A, legal[i].B, Rules, Weights, wanted);
                if (i == 0 || values[i] > top) top = values[i];
            }
            var best = new List<(CellId A, CellId B)>();
            for (int i = 0; i < legal.Count; i++) if (values[i] == top) best.Add(legal[i]);
            return best;
        }

        /// <summary>
        /// The <paramref name="count"/> legal moves with the highest shallow value, best first (equal values keep the
        /// <see cref="LegalMoves"/> order, so the result is deterministic). The planner's candidate list.
        /// </summary>
        public List<(CellId A, CellId B)> RankedMoves(int count)
        {
            var legal = LegalMoves;
            var wanted = _tracker.RemainingColors();
            var scratch = Board.Copy(); // the evaluator swaps in place; keep Board untouched
            var scored = new List<(double Value, int Index)>(legal.Count);
            for (int i = 0; i < legal.Count; i++)
                scored.Add((MoveEvaluator.Value(scratch, legal[i].A, legal[i].B, Rules, Weights, wanted), i));
            scored.Sort((x, y) => x.Value != y.Value ? y.Value.CompareTo(x.Value) : x.Index.CompareTo(y.Index));
            var result = new List<(CellId A, CellId B)>(Math.Min(count, scored.Count));
            for (int i = 0; i < scored.Count && i < count; i++) result.Add(legal[scored[i].Index]);
            return result;
        }

        /// <summary>A good legal move (deterministic: the first of <see cref="BestMoves"/>), or null if the level is over.</summary>
        public (CellId A, CellId B)? Hint()
        {
            if (Status != SessionStatus.Playing) return null;
            var best = BestMoves();
            if (best.Count == 0) return null;
            return best[0];
        }

        /// <summary>The goals as text, optionally with progress, for example "Score 1,000 points (250/1000)".</summary>
        public List<string> GoalDescriptions(bool withProgress = false, IReadOnlyList<string>? colorNames = null)
        {
            var lines = new List<string>();
            var progress = Progress;
            for (int i = 0; i < Level.Goals.Count; i++)
            {
                var g = Level.Goals[i];
                string text = g.Describe(colorNames);
                lines.Add(withProgress
                    ? text + " (" + progress[i].ToString(CultureInfo.InvariantCulture) + "/" + g.Target.ToString(CultureInfo.InvariantCulture) + ")"
                    : text);
            }
            return lines;
        }

        /// <summary>An independent copy (own board, streams and tracker), for look-ahead.</summary>
        public Session Copy() => new Session(this);
    }
}
