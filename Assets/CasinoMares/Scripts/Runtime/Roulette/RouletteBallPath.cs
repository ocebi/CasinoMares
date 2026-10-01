using System.Collections.Generic;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>
    /// One spin of the ball (GDD §13.1), worked out backwards from the pocket it has to end in, so every client
    /// watches it land in the networked result. The croupier flicks the ball out of the last pocket onto the track;
    /// it circles against the rotor, slows, spirals down the apron (sometimes clipping a diamond), skids along the
    /// rotor rim, hops across a few pockets and rattles to a stop. Hop lengths follow from their heights, as if
    /// under gravity. Positions are in the wheel's frame (<see cref="RouletteWheelGeometry"/>); everything
    /// random comes from the round's seed, apart from where the diamonds sit relative to the ball, which depends
    /// on this client's rotor angle and only changes when the ball ticks off one.
    /// </summary>
    public sealed class RouletteBallPath
    {
        /// <summary>The spin, from the croupier's push to the ball at rest (the result is called a moment later).</summary>
        public const float Duration = 7.5f;

        const float LaunchEnd = 0.35f;
        const float DropStart = 4.9f;
        const float RotorEntry = 5.9f;
        const float SettleTime = 7.15f;

        const float RotorPushSpeed = 120f;
        public const float RotorIdleSpeed = 12f;

        const float LaunchSweep = 120f;
        const float TrackTravel = 3100f;
        /// <summary>Ball speed leaving the apron, as a share of its launch speed.</summary>
        const float TrackSlowdown = 0.28f;
        const float EntryRadius = 0.275f;
        const float RimRadius = 0.248f;
        const float Gravity = 9.81f;
        /// <summary>Hops last this much longer than real ones would, so the eye can follow them.</summary>
        const float HopSlowMotion = 1.3f;
        const float DiamondHop = 0.016f;
        const float DiamondHopTime = 0.16f;

        struct Segment
        {
            public float Start;
            public float End;
            public float FromAngle;
            public float ToAngle;
            public float FromRadius;
            public float ToRadius;
            public float Height;
        }

        readonly float _rotorStart;
        readonly float _launchAngle;
        readonly float _trackStart;
        readonly float _trackTravel;
        readonly float _restAngle;
        readonly float _diamondTime = -1f;
        readonly List<Segment> _rotorSegments = new();
        readonly List<(float time, float volume)> _contacts = new();

        /// <param name="seed">Shared by every client for this spin (the round id).</param>
        /// <param name="fromNumber">Pocket the ball starts in (the last result), or -1.</param>
        /// <param name="toNumber">Pocket it must end in.</param>
        /// <param name="rotorStart">This client's rotor angle when the spin began.</param>
        public RouletteBallPath(int seed, int fromNumber, int toNumber, float rotorStart)
        {
            var random = new System.Random(seed);
            _rotorStart = rotorStart;
            _launchAngle = fromNumber >= 0 ? RouletteWheelTexture.PocketAngle(fromNumber) : 0f;
            _restAngle = RouletteWheelTexture.PocketAngle(toNumber);

            // On the rotor, angles are relative to it. Build the hops forwards with made-up offsets, then shift them
            // all so the last one ends in the result pocket.
            var entrySpeed = -(TrackSlowdown * 960f + RotorSpeed(RotorEntry));
            var speed = entrySpeed * Range(random, 0.55f, 0.7f);
            var hops = random.Next(3, 6);
            var heights = new float[hops];
            var hopTime = 0f;
            for (var i = 0; i < hops; i++)
            {
                heights[i] = Mathf.Max(0.013f, 0.03f * Mathf.Pow(0.72f, i) * Range(random, 0.85f, 1.15f));
                hopTime += FlightTime(heights[i]);
            }

            var rattles = new[] { 0.006f, 0.003f };
            foreach (var height in rattles)
                hopTime += FlightTime(height);

            var time = RotorEntry;
            var angle = 0f;
            var slideEnd = Mathf.Max(RotorEntry + 0.15f, SettleTime - hopTime);
            var slideSpeed = speed * Range(random, 0.7f, 0.85f);
            AddSegment(ref time, ref angle, slideEnd - RotorEntry, (speed + slideSpeed) / 2f, EntryRadius, RimRadius, 0f);
            speed = slideSpeed;
            _contacts.Add((RotorEntry, 1f));

            for (var i = 0; i < hops; i++)
            {
                var toRadius = Mathf.Lerp(RimRadius, RouletteWheelGeometry.PocketRadius, Mathf.Sqrt((i + 1f) / hops));
                AddSegment(ref time, ref angle, FlightTime(heights[i]), speed, _rotorSegments[^1].ToRadius, toRadius, heights[i]);
                _contacts.Add((time, Mathf.Lerp(0.9f, 0.5f, i / (float)hops)));

                // Each landing kills speed; now and then the ball kicks back off a fret.
                speed *= Range(random, 0.4f, 0.7f);
                if (i > 0 && random.NextDouble() < 0.25)
                    speed = -speed * 0.6f;
            }

            // Settling: tiny hops inside the pocket, in and out a few millimetres.
            for (var i = 0; i < rattles.Length; i++)
            {
                var radius = RouletteWheelGeometry.PocketRadius + (i == 0 ? 0.004f : 0f);
                AddSegment(ref time, ref angle, FlightTime(rattles[i]), (i == 0 ? 0.5f : -0.5f) / FlightTime(rattles[i]), _rotorSegments[^1].ToRadius, radius, rattles[i]);
                _contacts.Add((time, 0.35f));
            }

            var shift = _restAngle - angle;
            for (var i = 0; i < _rotorSegments.Count; i++)
            {
                var segment = _rotorSegments[i];
                segment.FromAngle += shift;
                segment.ToAngle += shift;
                _rotorSegments[i] = segment;
            }

            // The ball has to reach the rotor where the first segment starts. Its run around the track is about
            // TrackTravel degrees; stretch it up to one more turn so the angles meet.
            _trackStart = _launchAngle - LaunchSweep;
            var entryAngle = RotorTurn(RotorEntry) + _rotorSegments[0].FromAngle;
            _trackTravel = TrackTravel + Mathf.Repeat(_trackStart - entryAngle - TrackTravel, 360f);

            // Diamonds: the first one the ball passes while crossing their ring may kick it up.
            var kick = random.NextDouble() < 0.7;
            if (kick)
                _diamondTime = FindDiamondCrossing();
            if (_diamondTime >= 0f)
            {
                _contacts.Add((_diamondTime, 0.7f));
                _contacts.Sort((a, b) => a.time.CompareTo(b.time));
            }
        }

        /// <summary>How far the rotor has turned (degrees) this long after the croupier pushed it; it coasts at idle speed afterwards.</summary>
        public static float RotorTurn(float time)
        {
            if (time >= Duration)
                return RotorTurn(Duration - 0.0001f) + RotorIdleSpeed * (time - Duration);
            var left = 1f - Mathf.Max(0f, time) / Duration;
            return RotorIdleSpeed * time + (RotorPushSpeed - RotorIdleSpeed) * Duration / 3f * (1f - left * left * left);
        }

        static float RotorSpeed(float time)
        {
            var left = 1f - Mathf.Clamp01(time / Duration);
            return RotorIdleSpeed + (RotorPushSpeed - RotorIdleSpeed) * left * left;
        }

        /// <summary>The rotor's angle for this client at this point of the spin.</summary>
        public float RotorAngleAt(float time) => _rotorStart + RotorTurn(time);

        /// <summary>Ball centre in the wheel's frame.</summary>
        public Vector3 PositionAt(float time)
        {
            BallAt(Mathf.Max(0f, time), out var angle, out var radius, out var height);
            return RouletteWheelGeometry.OnCircle(_rotorStart + angle, radius, height);
        }

        /// <summary>How many times the ball has struck something (rotor, frets, a diamond) by this time.</summary>
        public int ContactsBefore(float time)
        {
            var count = 0;
            while (count < _contacts.Count && _contacts[count].time <= time)
                count++;
            return count;
        }

        public float ContactVolume(int index) => _contacts[index].volume;

        public bool IsLastContact(int index) => index == _contacts.Count - 1;

        /// <summary>Angle relative to the rotor start, radius and height of the ball's centre.</summary>
        void BallAt(float time, out float angle, out float radius, out float height)
        {
            if (time < LaunchEnd)
            {
                // Flicked out of the pocket, up over the rotor rim and onto the track, picking up speed.
                var u = time / LaunchEnd;
                angle = _launchAngle - LaunchSweep * (0.3f * u * u + 0.7f * u * u * u);
                radius = Mathf.Lerp(RouletteWheelGeometry.PocketRadius, RouletteWheelGeometry.TrackRadius, Mathf.SmoothStep(0f, 1f, u));
                height = Mathf.Lerp(RouletteWheelGeometry.RestingHeight(RouletteWheelGeometry.PocketRadius), RouletteWheelGeometry.RestingHeight(RouletteWheelGeometry.TrackRadius), u) +
                         0.05f * Mathf.Sin(u * Mathf.PI);
                return;
            }

            if (time < RotorEntry)
            {
                angle = TrackAngle(time);
                radius = TrackRadiusAt(time);
                height = RouletteWheelGeometry.RestingHeight(radius);
                if (_diamondTime >= 0f && time >= _diamondTime && time < _diamondTime + DiamondHopTime)
                {
                    var w = (time - _diamondTime) / DiamondHopTime;
                    height += DiamondHop * 4f * w * (1f - w);
                }

                return;
            }

            var turn = RotorTurn(time);
            foreach (var segment in _rotorSegments)
            {
                if (time >= segment.End)
                    continue;
                var w = (time - segment.Start) / (segment.End - segment.Start);
                angle = turn + Mathf.Lerp(segment.FromAngle, segment.ToAngle, w);
                radius = Mathf.Lerp(segment.FromRadius, segment.ToRadius, w);
                height = Mathf.Lerp(RouletteWheelGeometry.RestingHeight(segment.FromRadius), RouletteWheelGeometry.RestingHeight(segment.ToRadius), w) +
                         segment.Height * 4f * w * (1f - w);
                return;
            }

            angle = turn + _restAngle;
            radius = RouletteWheelGeometry.PocketRadius;
            height = RouletteWheelGeometry.RestingHeight(radius);
        }

        /// <summary>Around the track, slowing steadily (speed falls in a straight line), then down the apron.</summary>
        float TrackAngle(float time)
        {
            var u = (time - LaunchEnd) / (RotorEntry - LaunchEnd);
            var covered = (u - (1f - TrackSlowdown) * u * u / 2f) / ((1f + TrackSlowdown) / 2f);
            return _trackStart - _trackTravel * covered;
        }

        static float TrackRadiusAt(float time)
        {
            if (time < DropStart)
                return RouletteWheelGeometry.TrackRadius;
            var s = (time - DropStart) / (RotorEntry - DropStart);
            return Mathf.Lerp(RouletteWheelGeometry.TrackRadius, EntryRadius, Mathf.Pow(s, 1.6f));
        }

        float FindDiamondCrossing()
        {
            const float step = 0.004f;
            const float spacing = 360f / RouletteWheelGeometry.DiamondCount;
            var previous = -1;
            for (var time = DropStart; time < RotorEntry; time += step)
            {
                if (Mathf.Abs(TrackRadiusAt(time) - RouletteWheelGeometry.DiamondRadius) > 0.012f)
                {
                    previous = -1;
                    continue;
                }

                var slot = Mathf.FloorToInt((_rotorStart + TrackAngle(time)) / spacing);
                if (previous != -1 && slot != previous)
                    return time;
                previous = slot;
            }

            return -1f;
        }

        void AddSegment(ref float time, ref float angle, float duration, float speed, float fromRadius, float toRadius, float height)
        {
            var travel = speed * duration;
            _rotorSegments.Add(new Segment
            {
                Start = time,
                End = time + duration,
                FromAngle = angle,
                ToAngle = angle + travel,
                FromRadius = fromRadius,
                ToRadius = toRadius,
                Height = height,
            });
            time += duration;
            angle += travel;
        }

        static float FlightTime(float height) => HopSlowMotion * Mathf.Sqrt(8f * height / Gravity);

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
