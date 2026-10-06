using System;
using CasinoMares.Player;
using Fusion;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Shows table events (a card, a decision, a result) at the same moment on every screen. The authority stamps
    /// each event with a reveal tick a little in the future, late enough for the slowest player in the room to have
    /// received it by then, and every client, the authority included, holds the event back until its reveal clock
    /// reaches that tick (<see cref="RevealQueue{T}"/>). An event that arrives after its tick shows at once.
    /// </summary>
    /// <remarks>
    /// Why not plain <c>Runner.Tick</c>: in Shared mode each client's tick runs ahead of the server's clock by
    /// about its own one-way latency plus a jitter margin (so its state reaches the server in time), and the remote
    /// render time runs behind it by about the same amount. Taking half the client's own round trip off its tick
    /// puts every client close to the server's clock, give or take those jitter margins.
    /// </remarks>
    public static class TableReveal
    {
        /// <summary>The longest an event is held back, so one player on a poor connection can't slow the room much.</summary>
        public const float MaxDelay = 0.3f;

        /// <summary>On top of the slowest round trip: the server's send interval and some jitter.</summary>
        const float Margin = 0.03f;

        /// <summary>The reveal clock in ticks: about the same on every client at the same moment.</summary>
        public static int Now(NetworkRunner runner)
        {
            var halfRtt = runner.GetPlayerRtt(runner.LocalPlayer) * 0.5;
            return runner.Tick.Raw - (int)Math.Round(halfRtt / runner.DeltaTime);
        }

        /// <summary>
        /// How long the authority holds events back: the slowest round trip among the players in front of their game
        /// (<see cref="PlayerCat.RttMs"/>), capped at <see cref="MaxDelay"/>. Nothing when nobody else is watching.
        /// </summary>
        public static float Delay()
        {
            var watching = 0;
            var slowest = 0;
            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.Object == null || !cat.Object.IsValid || cat.IsAway)
                    continue;
                watching++;
                slowest = Math.Max(slowest, cat.RttMs);
            }

            return watching < 2 ? 0f : Mathf.Min(slowest / 1000f + Margin, MaxDelay);
        }

        /// <summary>
        /// Authority: the reveal tick for what's being written this tick. Later than <paramref name="current"/>, so
        /// each event gets a stamp of its own; several events in one tick share one.
        /// </summary>
        public static int Stamp(NetworkRunner runner, int current, ref int stampedTick)
        {
            var tick = runner.Tick.Raw;
            if (stampedTick == tick)
                return current;
            stampedTick = tick;
            return Math.Max(Now(runner) + Mathf.CeilToInt(Delay() / runner.DeltaTime), current + 1);
        }

        /// <summary>Seconds until the reveal clock reaches <paramref name="tick"/> (0 once it has).</summary>
        public static float SecondsUntil(NetworkRunner runner, int tick) => Mathf.Max(0, tick - Now(runner)) * runner.DeltaTime;
    }

    /// <summary>
    /// What one table shows on this client: copies of its networked state, each held back until the reveal clock
    /// reaches the tick the authority stamped on it (<see cref="TableReveal"/>). The newest copy keeps following the
    /// networked state, so changes without a stamp of their own (seats, bets while betting is open) show as soon as
    /// they arrive, or together with a stamped event that is still waiting. Never early, at worst a little late.
    /// </summary>
    public sealed class RevealQueue<T> where T : class, new()
    {
        const int Capacity = 8;

        readonly Action<T> _capture;
        readonly T[] _states = new T[Capacity];
        readonly int[] _ticks = new int[Capacity];
        int _shown;
        int _count;
        int _stamp;
        int _frame = -1;

        public RevealQueue(Action<T> capture)
        {
            _capture = capture;
            for (var i = 0; i < Capacity; i++)
                _states[i] = new T();
        }

        int Newest => (_shown + _count - 1) % Capacity;

        /// <summary>The state to show this frame. <paramref name="stamp"/> is the table's networked reveal tick.</summary>
        public T Get(NetworkRunner runner, int stamp)
        {
            if (_frame == Time.frameCount && _count > 0)
                return _states[_shown];
            _frame = Time.frameCount;

            if (_count == 0 || runner == null || !runner.IsRunning)
            {
                // First look (just joined): show the table as it is, no waiting.
                _count = 1;
                _stamp = stamp;
                _capture(_states[_shown]);
                return _states[_shown];
            }

            if (stamp != _stamp)
            {
                _stamp = stamp;
                // When full, the newest copy waits for this later stamp too: late, never early.
                if (_count < Capacity)
                    _count++;
                _ticks[Newest] = stamp;
            }

            _capture(_states[Newest]);

            var now = TableReveal.Now(runner);
            while (_count > 1 && _ticks[(_shown + 1) % Capacity] <= now)
            {
                _shown = (_shown + 1) % Capacity;
                _count--;
            }

            return _states[_shown];
        }
    }
}
