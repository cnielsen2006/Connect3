using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>
    /// <see cref="Resolve"/>: swap, match, clear, fire specials, settle, loop, events (TRD §3.1).
    /// Pure and deterministic: all randomness comes from the <see cref="Streams"/> passed in and all
    /// numbers from the <see cref="RuleSet"/>.
    /// </summary>
    public static class Rules
    {
        /// <summary>
        /// Plays one swap. Returns the new board (the input is untouched) and the event list. An
        /// illegal swap (no match and not a special pair) returns the board unchanged with a single
        /// <see cref="SwapRejected"/> event. Throws <see cref="ArgumentException"/> if the cells are not
        /// swappable neighbors holding tiles.
        /// </summary>
        public static (Board Board, List<Event> Events) Resolve(Board board, (CellId A, CellId B) swap, Streams streams, RuleSet ruleSet, int startLevel = 0)
            => Resolve(board, swap.A, swap.B, streams, ruleSet, startLevel);

        /// <summary>Same as the tuple overload, with the two cells as separate arguments.</summary>
        public static (Board Board, List<Event> Events) Resolve(Board board, CellId a, CellId b, Streams streams, RuleSet ruleSet, int startLevel = 0)
        {
            var topo = board.Topology;
            board = board.Copy();
            var ta = board.TileAt(a);
            var tb = board.TileAt(b);
            bool adjacent = false;
            if (topo.Contains(a)) foreach (var n in topo.Neighbors(a)) if (n == b) adjacent = true;
            if (ta == null || tb == null || !adjacent)
                throw new ArgumentException("cannot swap " + a + " and " + b);

            bool bothSpecial = ta.Special != null && tb.Special != null;
            board.Swap(a, b);
            if (!bothSpecial && !Match.HasMatchThrough(board, new[] { a, b }))
            {
                board.Swap(a, b);
                return (board, new List<Event> { new SwapRejected(a, b) });
            }

            var events = new List<Event> { new Swapped(a, b) };
            var queued = new bool[topo.CellCapacity];
            int flatPoints = 0;
            // the color each swapped tile was swapped with (this move only)
            int partnerIdA = ta.Id, partnerColorA = tb.Color, partnerIdB = tb.Id, partnerColorB = ta.Color;
            bool havePartner = true;
            if (bothSpecial)
            {
                bool bothColor = ta.Special is Special.Color && tb.Special is Special.Color;
                if (bothColor)
                {
                    foreach (var c in topo.Cells) queued[c.Value] = true;
                    flatPoints = ruleSet.Scoring.ComboColorColor;
                }
                else
                {
                    queued[a.Value] = true;
                    queued[b.Value] = true;
                }
            }

            int depth = 0;
            while (true)
            {
                var groups = Match.FindGroups(board);
                var waveCells = (bool[])queued.Clone();
                queued = new bool[topo.CellCapacity];
                bool any = false;
                foreach (var c in topo.Cells) if (waveCells[c.Value]) { any = true; break; }
                if (groups.Count == 0 && !any) break;

                double roundMult = Scoring.Multiplier(startLevel + depth, groups.Count > 0 ? groups.Count - 1 : 0, ruleSet.Scoring);
                var earned = new List<string>();
                var groupScores = new List<Scored>(); // one per group, in group order, emitted after the clears
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    events.Add(new Matched(g.Cells, g.Qualifiers));
                    double mult = Scoring.Multiplier(startLevel + depth, gi, ruleSet.Scoring);
                    groupScores.Add(new Scored(Scoring.Score(g.Qualifiers, mult, ruleSet.Scoring), depth, mult));
                    earned.AddRange(Specials.Earned(g.Qualifiers));
                    foreach (var c in g.Cells) waveCells[c.Value] = true;
                }

                int bySpecial = 0;
                int wave = 0;
                while (true)
                {
                    var cells = new List<CellId>(); // ascending: topology.Cells is ascending
                    foreach (var c in topo.Cells) if (waveCells[c.Value]) cells.Add(c);
                    if (cells.Count == 0) break;

                    var fired = new List<(int TileId, List<CellId> Hit)>();
                    var ids = new int[cells.Count];
                    for (int i = 0; i < cells.Count; i++)
                    {
                        var t = board.TileAt(cells[i]) ?? throw new InvalidOperationException("queued cell " + cells[i] + " is empty");
                        ids[i] = t.Id;
                        if (t.Special != null)
                        {
                            int? partner = null;
                            if (havePartner)
                            {
                                if (t.Id == partnerIdA) partner = partnerColorA;
                                else if (t.Id == partnerIdB) partner = partnerColorB;
                            }
                            fired.Add((t.Id, Specials.Fire(board, cells[i], t, ruleSet.Specials, partner)));
                        }
                    }
                    events.Add(new Cleared(ids, wave));
                    if (wave > 0) bySpecial += cells.Count;
                    foreach (var c in cells) board.Put(c, null);
                    waveCells = new bool[topo.CellCapacity];
                    foreach (var (tileId, hit) in fired)
                    {
                        events.Add(new SpecialFired(tileId, hit));
                        foreach (var c in hit) if (board.TileAt(c) != null) waveCells[c.Value] = true;
                    }
                    wave++;
                }

                events.AddRange(groupScores);
                int bonus = Scoring.Points(Scoring.SpecialCleared(bySpecial, ruleSet.Scoring), roundMult);
                if (bonus > 0) events.Add(new Scored(bonus, depth, roundMult));
                if (flatPoints > 0) events.Add(new Scored(flatPoints, depth)); // flat: no multiplier
                flatPoints = 0;

                var (settled, settleEvents) = Flow.Settle(board, streams);
                board = settled;
                events.AddRange(settleEvents);
                PlaceSpecials(board, earned, settleEvents, streams, events, ruleSet);
                havePartner = false;
                depth++;
            }

            var legal = Moves.FindLegal(board);
            if (legal.Count == 0)
            {
                var (shuffledBoard, shuffled) = Moves.Shuffle(board, streams);
                board = shuffledBoard;
                events.Add(shuffled);
                legal = Moves.FindLegal(board);
            }
            board.LegalCache = legal;
            return (board, events);
        }

        /// <summary>
        /// Gives each earned special to a random host: a tile spawning this step, or (with probability
        /// 1 - IncomingChance) a normal tile already on the board (TRD §3.3). Mutates <paramref name="board"/>.
        /// </summary>
        static void PlaceSpecials(Board board, List<string> earnedKinds, List<Event> settleEvents, Streams streams,
            List<Event> events, RuleSet ruleSet)
        {
            var earned = new List<string>();
            foreach (var k in earnedKinds) if (board.IsSpecialEnabled(k)) earned.Add(k);
            if (earned.Count == 0) return;

            var rng = streams.Specials;
            var newIds = new List<int>();
            foreach (var e in settleEvents) if (e is Spawned s) newIds.Add(s.TileId);

            foreach (var kind in earned)
            {
                var incoming = new List<int>();
                foreach (int id in newIds)
                {
                    var c = board.FindTile(id);
                    if (c != null && board.TileAt(c.Value)!.Special == null) incoming.Add(id);
                }
                var onBoard = new List<CellId>();
                foreach (var c in board.Topology.Cells)
                {
                    var t = board.TileAt(c);
                    if (t != null && t.Special == null && !newIds.Contains(t.Id)) onBoard.Add(c);
                }

                bool useIncoming = rng.Random() < ruleSet.Specials.IncomingChance;
                bool poolIsCells;
                if (useIncoming) poolIsCells = incoming.Count == 0;
                else poolIsCells = onBoard.Count != 0;
                if (poolIsCells ? onBoard.Count == 0 : incoming.Count == 0) continue; // nothing to host it: the special is lost

                CellId cell;
                if (poolIsCells) cell = rng.Choice(onBoard);
                else cell = board.FindTile(rng.Choice(incoming))!.Value;

                var special = Specials.Make(kind, rng);
                var old = board.TileAt(cell)!;
                board.Put(cell, new Tile(old.Id, old.Color, special));
                events.Add(new SpecialCreated(old.Id, special));
            }
        }
    }
}
