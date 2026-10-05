using Cascade.Core;
using Cascade.Presentation;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 12.3: one slot, last request wins, played when the board has settled.</summary>
    public class SwapQueueTests
    {
        [Test]
        public void An_idle_board_plays_the_swap_at_once_and_queues_nothing()
        {
            var q = new SwapQueue();
            var play = q.Request(1, 2, animating: false);
            Assert.That(play, Is.EqualTo(((CellId, CellId)?)(new CellId(1), new CellId(2))));
            Assert.That(q.HasPending, Is.False);
        }

        [Test]
        public void While_animating_a_swap_waits_and_is_handed_out_once_when_the_board_settles()
        {
            var q = new SwapQueue();
            Assert.That(q.Request(1, 2, animating: true), Is.Null);
            Assert.That(q.HasPending, Is.True);
            Assert.That(q.Pending, Is.EqualTo(((CellId, CellId)?)(new CellId(1), new CellId(2))));
            Assert.That(q.TakePending(), Is.EqualTo(((CellId, CellId)?)(new CellId(1), new CellId(2))));
            Assert.That(q.HasPending, Is.False);
            Assert.That(q.TakePending(), Is.Null);
        }

        [Test]
        public void The_last_request_wins()
        {
            var q = new SwapQueue();
            q.Request(1, 2, true);
            q.Request(3, 4, true);
            q.Request(5, 6, true);
            Assert.That(q.TakePending(), Is.EqualTo(((CellId, CellId)?)(new CellId(5), new CellId(6))));
        }

        [Test]
        public void A_request_on_an_idle_board_drops_a_stale_queued_swap()
        {
            var q = new SwapQueue();
            q.Request(1, 2, true);
            Assert.That(q.Request(3, 4, false), Is.Not.Null);
            Assert.That(q.HasPending, Is.False);
        }

        [Test]
        public void Clear_forgets_the_queued_swap()
        {
            var q = new SwapQueue();
            q.Request(1, 2, true);
            q.Clear();
            Assert.That(q.TakePending(), Is.Null);
        }
    }
}
