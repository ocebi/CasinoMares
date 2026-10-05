using CasinoMares.Audio;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// Spins the three reels on every client and stops them one by one on the networked symbols. When the first two
    /// match, the third slows down instead, ticking and glowing, before it stops: will it make three?
    /// </summary>
    public class SlotMachineView : MonoBehaviour
    {
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        /// <summary>Symbols per second at full speed.</summary>
        const float ReelSpeed = 14f;
        /// <summary>A stopping reel overshoots a little and settles back over this long.</summary>
        const float SettleTime = 0.22f;
        const float SettleOvershoot = 0.15f;
        static readonly Color TeaseGlow = new(1f, 0.8f, 0.45f);

        [SerializeField] SlotMachine _machine;
        /// <summary>None in scenes built before there were celebrations (the classic lounge, until it's rebuilt).</summary>
        [SerializeField] SlotCelebration _celebration;
        [SerializeField] MeshRenderer[] _reels = new MeshRenderer[SlotRules.Reels];

        MaterialPropertyBlock _block;
        readonly bool[] _stopped = new bool[SlotRules.Reels];
        readonly float[] _stoppedAt = new float[SlotRules.Reels];
        int _lastSpin = -1;
        int _lastTick;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            var material = new Material(_reels[0].sharedMaterial) { name = "SlotReel (runtime)" };
            material.SetTexture("_BaseMap", SlotSymbols.Strip);
            foreach (var reel in _reels)
                reel.sharedMaterial = material;
        }

        void LateUpdate()
        {
            if (_machine.Object == null || !_machine.Object.IsValid)
            {
                for (var i = 0; i < _reels.Length; i++)
                    Show(i, i * 2, 0f);
                return;
            }

            if (_lastSpin < 0)
            {
                _lastSpin = _machine.SpinId;
                for (var i = 0; i < _reels.Length; i++)
                {
                    _stopped[i] = true;
                    _stoppedAt[i] = float.NegativeInfinity;
                }
            }

            if (_machine.SpinId != _lastSpin)
            {
                _lastSpin = _machine.SpinId;
                for (var i = 0; i < _reels.Length; i++)
                    _stopped[i] = false;
                _lastTick = int.MinValue;
                Sfx.PlayAt(SoundId.WheelSpin, transform.position, 0.8f);
                if (_celebration != null)
                    _celebration.OnSpinStarted();
            }

            var elapsed = _machine.SpinElapsed;
            var teases = _machine.Teases;
            var glow = 0f;
            for (var i = 0; i < _reels.Length; i++)
            {
                var target = _machine.ReelAt(i);
                var slows = teases && i == _reels.Length - 1;
                var stop = SlotMachine.ReelStops[i] + (slows ? SlotMachine.TeaseDuration : 0f);
                float position;
                if (_stopped[i] || elapsed >= stop)
                {
                    if (!_stopped[i])
                    {
                        _stopped[i] = true;
                        _stoppedAt[i] = Time.time;
                        Sfx.PlayAt(SoundId.CardPlace, transform.position, 0.9f);
                        if (i == _reels.Length - 1 && _celebration != null)
                            _celebration.OnLanded();
                    }

                    var settle = (Time.time - _stoppedAt[i]) / SettleTime;
                    position = target + (settle < 1f ? SettleOvershoot * Mathf.Sin(settle * Mathf.PI) * (1f - settle) : 0f);
                }
                else
                {
                    var slowFrom = SlotMachine.ReelStops[i - (slows ? 1 : 0)];
                    position = target - Remaining(elapsed, stop, slows ? stop - slowFrom : 0f);
                    if (slows && elapsed > slowFrom)
                    {
                        // A ratchet click for every symbol that goes by, slower and slower.
                        var symbol = Mathf.FloorToInt(position);
                        if (symbol != _lastTick && _lastTick != int.MinValue)
                            Sfx.PlayAt(SoundId.BallTick, transform.position + Vector3.up, 0.7f);
                        _lastTick = symbol;
                        glow = Mathf.Clamp01((elapsed - slowFrom) * 3f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 14f));
                    }
                }

                Show(i, position, slows && !_stopped[i] ? glow : 0f);
            }

            if (_celebration != null)
                _celebration.SetGlow(glow);
        }

        /// <summary>
        /// How many symbols a reel still has to go before it stops at <paramref name="stop"/>. A reel that slows down
        /// does so over its last <paramref name="slowTime"/> seconds, from full speed to nothing.
        /// </summary>
        static float Remaining(float elapsed, float stop, float slowTime)
        {
            if (slowTime <= 0f)
                return ReelSpeed * (stop - elapsed);

            var slowDistance = ReelSpeed * slowTime / 2f;
            var slowFrom = stop - slowTime;
            if (elapsed < slowFrom)
                return slowDistance + ReelSpeed * (slowFrom - elapsed);
            var left = (stop - elapsed) / slowTime;
            return slowDistance * left * left;
        }

        void Show(int reel, float position, float glow)
        {
            var count = SlotRules.SymbolCount;
            position = (position % count + count) % count;
            _reels[reel].GetPropertyBlock(_block);
            _block.SetVector(BaseMapStId, SlotSymbols.ScaleOffset(position));
            _block.SetColor(BaseColorId, Color.Lerp(Color.white, TeaseGlow * 1.8f, glow));
            _reels[reel].SetPropertyBlock(_block);
        }
    }
}
