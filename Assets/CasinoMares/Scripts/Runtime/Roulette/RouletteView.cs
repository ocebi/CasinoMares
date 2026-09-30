using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Npc;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>
    /// Renders a <see cref="RouletteTable"/> on every client: the spinning wheel and ball (landing in the networked
    /// result pocket), chip stacks per seat, the croupier and NPC seat cats, and reactions to the result.
    /// </summary>
    public class RouletteView : MonoBehaviour
    {
        const float IdleRotorSpeed = 12f;
        const float RimRadius = 0.25f;
        const float PocketRadius = 0.19f;

        [SerializeField] RouletteTable _table;
        [SerializeField] Transform _rotor;
        [SerializeField] MeshRenderer _wheelFace;
        [SerializeField] Transform _ball;
        [SerializeField] Transform[] _chipSpots = new Transform[RouletteTable.SeatCount];
        [SerializeField] Material _chipMaterial;
        [SerializeField] Mesh _chipMesh;
        [SerializeField] NpcCatVisual _npcCatPrefab;
        [SerializeField] NpcCatVisual _croupier;

        readonly ChipStack[] _stacks = new ChipStack[RouletteTable.SeatCount];
        SeatCats _seatCats;

        float _rotorAngle;
        float _spinStartRotor;
        float _ballAngle;
        RoulettePhase _lastPhase;
        int _lastSettled;
        bool _ballDropped;
        bool _initialized;

        public RouletteTable Table => _table;
        public Vector3 ResultLabelPosition => _rotor.position + Vector3.up * 0.45f;

        void Awake()
        {
            var face = _wheelFace.GetComponent<MeshFilter>();
            face.sharedMesh = RouletteWheelTexture.DiscMesh(0.3f);
            var material = new Material(_wheelFace.sharedMaterial) { name = "RouletteWheel (runtime)" };
            material.SetTexture("_BaseMap", RouletteWheelTexture.Texture);
            _wheelFace.sharedMaterial = material;
            _seatCats = new SeatCats(_npcCatPrefab, transform, RouletteTable.SeatCount, _table.GetSeatAnchor);
        }

        void LateUpdate()
        {
            if (_table.Object == null || !_table.Object.IsValid)
            {
                IdleSpin();
                return;
            }

            var phase = _table.Phase;
            if (!_initialized)
            {
                _initialized = true;
                _lastPhase = phase;
                _lastSettled = _table.SettledRoundId;
            }

            if (phase == RoulettePhase.Spinning && _lastPhase != RoulettePhase.Spinning)
            {
                _spinStartRotor = _rotorAngle;
                _ballDropped = false;
                Sfx.PlayAt(SoundId.WheelSpin, _rotor.position);
            }

            if (phase == RoulettePhase.Spinning)
                AnimateSpin(_table.SpinProgress, _table.LandingNumber);
            else
                IdleSpin();

            if (_table.SettledRoundId != _lastSettled)
            {
                _lastSettled = _table.SettledRoundId;
                if (phase == RoulettePhase.Result)
                    OnLanded();
            }

            _lastPhase = phase;
            UpdateStacks(phase);
            UpdateCats();
        }

        void IdleSpin()
        {
            _rotorAngle += IdleRotorSpeed * Time.deltaTime;
            _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);

            // Between spins the ball rests in the last result's pocket and rides the rotor.
            var last = _table.Object != null && _table.Object.IsValid ? _table.HistoryAt(0) : -1;
            _ball.gameObject.SetActive(last >= 0);
            if (last >= 0)
                PlaceBall(_rotorAngle + RouletteWheelTexture.PocketAngle(last), PocketRadius, 0f);
        }

        void AnimateSpin(float progress, int landing)
        {
            // Rotor decelerates through 1.5 turns; the ball counter-rotates, then drops into the landing pocket.
            var eased = 1f - (1f - progress) * (1f - progress);
            _rotorAngle = _spinStartRotor + 540f * eased;
            _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);

            var finalRotor = _spinStartRotor + 540f;
            var finalBall = finalRotor + RouletteWheelTexture.PocketAngle(Mathf.Max(0, landing));
            var ballEase = 1f - Mathf.Pow(1f - progress, 3f);
            _ballAngle = finalBall - 1440f * (1f - ballEase);

            var drop = Mathf.InverseLerp(0.62f, 0.95f, progress);
            var radius = Mathf.Lerp(RimRadius, PocketRadius, drop);
            var bounce = drop > 0f && drop < 1f ? Mathf.Abs(Mathf.Sin(drop * Mathf.PI * 4f)) * 0.02f * (1f - drop) : 0f;
            // The click of the ball settling; the result is only called a moment later (GameTables.ResultPause).
            if (drop >= 1f && !_ballDropped)
            {
                _ballDropped = true;
                Sfx.PlayAt(SoundId.BallDrop, _rotor.position);
            }

            _ball.gameObject.SetActive(true);
            PlaceBall(_ballAngle, radius, bounce);
        }

        void PlaceBall(float angle, float radius, float lift)
        {
            var radians = angle * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Sin(radians) * radius, 0.025f + lift, Mathf.Cos(radians) * radius);
            _ball.position = _rotor.parent.TransformPoint(local);
        }

        void OnLanded()
        {
            var result = _table.Result;

            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var bet = _table.TotalBetAt(seat);
                if (bet == 0)
                    continue;
                var net = _table.PayoutAt(seat, result) - bet;
                var reactions = ReactionsFor(seat);
                if (reactions == null)
                    continue;
                if (net > 0)
                    reactions.PlayHappy(net >= bet * 10 ? 3 : 1, CatReactions.StaggerDelay());
                else
                    reactions.PlayAngry(CatReactions.StaggerDelay());
            }

            _croupier?.Reactions?.PlayHappy(1, CatReactions.StaggerDelay() + 0.3f);
        }

        CatReactions ReactionsFor(int seat)
        {
            var owner = _table.RoundOwnerAt(seat);
            if (owner != Fusion.PlayerRef.None)
                return PlayerCat.Find(owner)?.Reactions;
            return _seatCats.ReactionsAt(seat);
        }

        void UpdateStacks(RoulettePhase phase)
        {
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var amount = _table.TotalBetAt(seat);
                if (phase == RoulettePhase.Result)
                    amount = _table.PayoutAt(seat, _table.Result);

                var stack = _stacks[seat];
                if (amount <= 0)
                {
                    if (stack != null)
                        stack.gameObject.SetActive(false);
                    continue;
                }

                if (stack == null)
                {
                    stack = ChipStack.Create(transform, _chipMesh, _chipMaterial);
                    stack.transform.position = _chipSpots[seat].position;
                    _stacks[seat] = stack;
                }

                if (!stack.gameObject.activeSelf)
                    stack.gameObject.SetActive(true);
                if (stack.Amount != amount && stack.Amount >= 0)
                    Sfx.PlayAt(SoundId.ChipBet, _chipSpots[seat].position, 0.6f);
                stack.SetAmount(amount);
                stack.MoveTo(_chipSpots[seat].position, false);
            }
        }

        void UpdateCats()
        {
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
                _seatCats.UpdateSeat(seat, _table.OccupantAt(seat) == RouletteOccupant.Npc, _table.NpcFurAt(seat), _table.NpcArrivalAt(seat));
            _seatCats.Tick();

            if (_croupier != null)
            {
                _croupier.Sitting = true;
                _croupier.FaceTowards(_table.Phase == RoulettePhase.Spinning ? _rotor.position : transform.position, 90f);
            }
        }
    }
}
