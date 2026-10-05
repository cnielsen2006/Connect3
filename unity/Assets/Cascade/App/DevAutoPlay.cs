#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
// Dev builds only (like DevHotkeys): this file is not compiled into a release player, so auto-play cannot ship.
using Cascade.Gameplay;
using Cascade.Presentation.Shell;
using UnityEngine;

namespace Cascade.App
{
    /// <summary>
    /// Lets the app play itself while <see cref="DevViewModel.AutoPlay"/> is on (dev page toggle or F2): it leaves the map and intro, plays the
    /// session's suggested move whenever the board is still, then goes on to the next level after a win or retries after a loss. It acts through
    /// the same entry points a player uses (<c>RequestSwap</c>, <c>ScreenFlow.Next</c>, ...), one step per <see cref="stepSeconds"/>, so it
    /// exercises the real view and flow. It does nothing while the dev page or pause is open.
    /// With <see cref="DevViewModel.AutoPlayPlans"/> it plans instead (TRD 7.1): it searches the best line of moves, replays what it tried as a
    /// translucent ghost over the board (<see cref="PlanGhost"/>), shows the chosen line, and then swaps the first move.
    /// </summary>
    public sealed class DevAutoPlay : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds between auto-play actions (a swap, a screen change).")] float stepSeconds = 0.4f;

        enum Phase { Idle, Think, Show }

        AppBootstrap? app;
        float nextAt;
        PlanGhost? ghost;
        Phase phase;
        Plan? plan;
        Session? planned;
        int plannedMovesLeft;
        float phaseStart, probeStep;

        public void Bind(AppBootstrap bootstrap) => app = bootstrap;

        void Update()
        {
            var a = app;
            if (a == null || !a.Ready) return;
            var flow = a.Flow;
            if (!a.Models.Dev.AutoPlay || flow.DevOpen || flow.Paused || flow.Current != ScreenId.Play) CancelPlan();
            if (!a.Models.Dev.AutoPlay) return;
            if (Time.unscaledTime < nextAt) return;
            if (flow.DevOpen || flow.Paused) return;

            switch (flow.Current)
            {
                case ScreenId.Map:
                    if (a.Progress.CurrentLevel > 0) Step(flow.SelectLevel(a.Progress.CurrentLevel));
                    break;
                case ScreenId.Intro:
                    Step(flow.StartLevel());
                    break;
                case ScreenId.Result:
                    Step(flow.Won ? flow.Next() : flow.Retry());
                    break;
                case ScreenId.Play:
                    PlayMove(a);
                    break;
            }
        }

        void PlayMove(AppBootstrap a)
        {
            var session = a.Game.Session;
            if (session == null || !session.IsPlaying || a.Game.ResultPending) return;
            if (!a.Stage.Active || a.Stage.Controller.IsAnimating) return;
            if (a.Models.Dev.AutoPlayPlans) { PlanMove(a, session); return; }
            CancelPlan();
            var move = session.Hint();
            if (move == null) return;
            a.Stage.Controller.RequestSwap(move.Value.A, move.Value.B);
            Step(true);
        }

        /// <summary>Plan mode: Idle (search) -> Think (replay the probes) -> Show (the chosen line) -> swap its first move.</summary>
        void PlanMove(AppBootstrap a, Session session)
        {
            var settings = RuleSets.PlannerFrom(a.Dials);
            var pd = a.Dials.Planner;
            float now = Time.unscaledTime;
            if (phase != Phase.Idle && (planned != session || plannedMovesLeft != session.MovesLeft)) CancelPlan(); // the board moved on

            if (phase == Phase.Idle)
            {
                plan = Planner.Find(session, settings, recordProbes: true);
                if (plan == null) return;
                planned = session;
                plannedMovesLeft = session.MovesLeft;
                ghost ??= new PlanGhost(a.Board);
                int n = plan.Probes.Count;
                float think = pd.ThinkMs / 1000f;
                probeStep = n == 0 ? 0f : Mathf.Min(pd.ProbeMs / 1000f, think / n);
                phase = n == 0 || think <= 0f ? Phase.Show : Phase.Think;
                phaseStart = now;
            }

            var p = plan!;
            if (phase == Phase.Think)
            {
                int i = probeStep <= 0f ? p.Probes.Count : (int)((now - phaseStart) / probeStep);
                if (i < p.Probes.Count)
                {
                    ghost!.Show(p.Probes[i].Moves, 0.45f, arrowsForAll: false);
                    return;
                }
                phase = Phase.Show;
                phaseStart = now;
            }

            if (phase == Phase.Show)
            {
                ghost!.Show(p.Moves, 1f, arrowsForAll: true);
                if (now - phaseStart < pd.ShowMs / 1000f) return;
                var move = p.Moves[0];
                CancelPlan();
                a.Stage.Controller.RequestSwap(move.A, move.B);
                Step(true);
            }
        }

        /// <summary>Drops any plan in progress and hides the ghost.</summary>
        void CancelPlan()
        {
            phase = Phase.Idle;
            plan = null;
            planned = null;
            ghost?.Hide();
        }

        void Step(bool acted)
        {
            if (acted) nextAt = Time.unscaledTime + stepSeconds;
        }
    }
}
#endif
