using CasinoMares.Audio;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// Spins the three reels on every client and stops them one by one on the networked symbols, then flashes
    /// the machine's light on a win.
    /// </summary>
    public class SlotMachineView : MonoBehaviour
    {
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        static readonly float[] StopAt = { 0.45f, 0.7f, 0.95f };

        [SerializeField] SlotMachine _machine;
        [SerializeField] MeshRenderer[] _reels = new MeshRenderer[SlotRules.Reels];
        [SerializeField] Light _light;

        MaterialPropertyBlock _block;
        readonly float[] _positions = new float[SlotRules.Reels];
        readonly bool[] _stopped = new bool[SlotRules.Reels];
        int _lastSpin = -1;
        float _baseIntensity;

        public Vector3 LabelPosition => transform.position + Vector3.up * 1.55f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            var material = new Material(_reels[0].sharedMaterial) { name = "SlotReel (runtime)" };
            material.SetTexture("_BaseMap", SlotSymbols.Strip);
            foreach (var reel in _reels)
                reel.sharedMaterial = material;
            if (_light != null)
                _baseIntensity = _light.intensity;
        }

        void LateUpdate()
        {
            if (_machine.Object == null || !_machine.Object.IsValid)
            {
                for (var i = 0; i < _reels.Length; i++)
                    Show(i, i * 2);
                return;
            }

            if (_lastSpin < 0)
            {
                _lastSpin = _machine.SpinId;
                for (var i = 0; i < _reels.Length; i++)
                    _stopped[i] = true;
            }

            if (_machine.SpinId != _lastSpin)
            {
                _lastSpin = _machine.SpinId;
                for (var i = 0; i < _reels.Length; i++)
                    _stopped[i] = false;
                Sfx.PlayAt(SoundId.WheelSpin, transform.position, 0.8f);
            }

            var progress = _machine.SpinProgress;
            for (var i = 0; i < _reels.Length; i++)
            {
                var target = _machine.ReelAt(i);
                if (_stopped[i] || progress >= StopAt[i])
                {
                    if (!_stopped[i])
                    {
                        _stopped[i] = true;
                        Sfx.PlayAt(SoundId.CardPlace, transform.position, 0.9f);
                        if (i == _reels.Length - 1)
                            OnAllStopped();
                    }

                    _positions[i] = target;
                }
                else
                {
                    _positions[i] = (_positions[i] + Time.deltaTime * 14f) % SlotRules.SymbolCount;
                }

                Show(i, _positions[i]);
            }

            if (_light != null)
            {
                var winning = !_machine.IsSpinning && _machine.LastPayout > _machine.Bet;
                _light.intensity = winning ? _baseIntensity * (1.5f + Mathf.Sin(Time.time * 10f)) : _baseIntensity;
            }
        }

        void Show(int reel, float position)
        {
            _reels[reel].GetPropertyBlock(_block);
            _block.SetVector(BaseMapStId, SlotSymbols.ScaleOffset(position));
            _reels[reel].SetPropertyBlock(_block);
        }

        void OnAllStopped()
        {
            var payout = _machine.LastPayout;
            if (payout <= _machine.Bet)
                return;
            Sfx.PlayAt(SoundId.ChipPayout, transform.position);
            var owner = PlayerCat.Find(_machine.SpinOwner);
            owner?.Reactions?.PlayHappy(payout >= _machine.Bet * 20 ? 3 : 1);
        }
    }
}
