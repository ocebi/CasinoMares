using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Npc;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>
    /// Renders a <see cref="RouletteTable"/> on every client from its shown round (<see cref="RouletteTable.Shown"/>):
    /// the wheel (bowl, rotor, frets), the ball running the spin's <see cref="RouletteBallPath"/> into the networked
    /// result pocket, chip stacks per seat, the croupier and NPC seat cats, and reactions to the result.
    /// </summary>
    public class RouletteView : MonoBehaviour
    {
        [SerializeField] RouletteTable _table;
        [Tooltip("The wheel's frame: centre of the bowl, on the felt.")]
        [SerializeField] MeshFilter _bowl;
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
        RouletteTable.Snapshot _shown;

        float _rotorAngle;
        RouletteBallPath _path;
        int _pathRound = -1;
        float _spinClock;
        int _contacts;
        int _lastSettled;
        bool _initialized;

        public RouletteTable Table => _table;
        public Vector3 ResultLabelPosition => _rotor.position + Vector3.up * 0.55f;

        void Awake()
        {
            _bowl.sharedMesh = RouletteWheelGeometry.Bowl;
            _wheelFace.GetComponent<MeshFilter>().sharedMesh = RouletteWheelGeometry.Rotor;
            var materials = _wheelFace.sharedMaterials;
            materials[0] = new Material(materials[0]) { name = "RouletteWheel (runtime)" };
            materials[0].SetTexture("_BaseMap", RouletteWheelTexture.Texture);
            _wheelFace.sharedMaterials = materials;
            _seatCats = new SeatCats(_npcCatPrefab, transform, RouletteTable.SeatCount, _table.GetSeatAnchor, _table);
        }

        void LateUpdate()
        {
            if (_table.Object == null || !_table.Object.IsValid)
            {
                IdleSpin();
                return;
            }

            _shown = _table.Shown;
            var phase = _shown.Phase;
            if (!_initialized)
            {
                _initialized = true;
                _lastSettled = _shown.SettledRoundId;
            }

            if (phase == RoulettePhase.Spinning)
                AnimateSpin();
            else
                IdleSpin();

            if (_shown.SettledRoundId != _lastSettled)
            {
                _lastSettled = _shown.SettledRoundId;
                if (phase == RoulettePhase.Result)
                    OnLanded();
            }

            UpdateStacks(phase);
            UpdateCats();
        }

        void IdleSpin()
        {
            _rotorAngle += RouletteBallPath.RotorIdleSpeed * Time.deltaTime;
            _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);

            // Between spins the ball rests in the last result's pocket and rides the rotor.
            var last = _table.Object != null && _table.Object.IsValid ? _table.Shown.HistoryAt(0) : -1;
            _ball.gameObject.SetActive(last >= 0);
            if (last >= 0)
            {
                var radius = RouletteWheelGeometry.PocketRadius;
                var local = RouletteWheelGeometry.OnCircle(_rotorAngle + RouletteWheelTexture.PocketAngle(last), radius, RouletteWheelGeometry.RestingHeight(radius));
                _ball.position = _bowl.transform.TransformPoint(local);
            }
        }

        void AnimateSpin()
        {
            var elapsed = _shown.SpinElapsed;
            if (_path == null || _pathRound != _shown.RoundId)
            {
                // A new spin, or one already under way when we arrived: keep the rotor where it is and pick up the
                // ball's path from here.
                _pathRound = _shown.RoundId;
                _spinClock = elapsed;
                _path = new RouletteBallPath(_shown.RoundId, _shown.HistoryAt(0), _shown.LandingNumber, _rotorAngle - RouletteBallPath.RotorTurn(elapsed));
                _contacts = _path.ContactsBefore(elapsed);
                if (elapsed < 0.5f)
                    Sfx.PlayAt(SoundId.WheelSpin, _rotor.position);
            }
            else
            {
                // The networked timer moves in ticks; run a local clock and ease it toward the network so the
                // fast-moving ball stays smooth.
                _spinClock += Time.deltaTime;
                var error = elapsed - _spinClock;
                if (Mathf.Abs(error) > 0.3f)
                    _spinClock = elapsed;
                else
                    _spinClock += error * Mathf.Min(1f, 4f * Time.deltaTime);
            }

            _rotorAngle = _path.RotorAngleAt(_spinClock);
            _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);
            _ball.gameObject.SetActive(true);
            _ball.position = _bowl.transform.TransformPoint(_path.PositionAt(_spinClock));

            // A tick for every strike on the rotor, a fret or a diamond; the last one is the ball settling. The
            // result is only called a moment later (GameTables.ResultPause).
            var contacts = _path.ContactsBefore(_spinClock);
            for (; _contacts < contacts; _contacts++)
            {
                if (_path.IsLastContact(_contacts))
                    Sfx.PlayAt(SoundId.BallDrop, _ball.position, 0.6f);
                else
                    Sfx.PlayAt(SoundId.BallTick, _ball.position, _path.ContactVolume(_contacts));
            }
        }

        void OnLanded()
        {
            var result = _shown.Result;

            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var bet = _shown.TotalBetAt(seat);
                if (bet == 0)
                    continue;
                var net = _shown.PayoutAt(seat, result) - bet;
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
            var owner = _shown.RoundOwnerAt(seat);
            if (owner != Fusion.PlayerRef.None)
                return PlayerCat.Find(owner)?.Reactions;
            return _seatCats.ReactionsAt(seat);
        }

        void UpdateStacks(RoulettePhase phase)
        {
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var amount = _shown.TotalBetAt(seat);
                if (phase == RoulettePhase.Result)
                    amount = _shown.PayoutAt(seat, _shown.Result);

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
                _seatCats.UpdateSeat(seat, _table.OccupantAt(seat) == RouletteOccupant.Npc, _table.NpcFurAt(seat), _table.NpcArrivalAt(seat), _table.NpcScaresAt(seat));
            _seatCats.Tick();

            if (_croupier != null)
            {
                _croupier.Sitting = true;
                _croupier.FaceTowards(_shown.Phase == RoulettePhase.Spinning ? _rotor.position : transform.position, 90f);
            }
        }
    }
}
