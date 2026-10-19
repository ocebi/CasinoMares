using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using Fusion;
using UnityEngine;

namespace CasinoMares.Social
{
    /// <summary>Where a seat's glass of wine is, from the time its order was accepted.</summary>
    public enum WinePhase : byte
    {
        Idle,
        Pour,
        Paw,
        Slide,
        Drink,
        Empty,
    }

    /// <summary>
    /// Wine service at the bar (GDD §4.3): a cat on a stool orders a glass, the bartender pours it, swats it down the
    /// counter to the cat, the cat drinks it and the empty glass sits there a while. Lives on the bar's
    /// <see cref="TerraceTable"/> object with the same master-client authority. The authority only stamps an order
    /// id and the tick it was accepted; every client plays the whole sequence from the tick, so late joiners see the
    /// same glass in the same place. The orderer pays locally when it first sees its order accepted (trusted client,
    /// like bets), so a refused request costs nothing. A glass belongs to the cat that ordered it: if that cat leaves
    /// the stool, the glass is cleared and the stool is free for the next cat.
    /// </summary>
    public class WineBar : NetworkBehaviour
    {
        public const int WinePrice = 20;

        /// <summary>Seconds from the accepted order to the end of each phase (see the table in the design round).</summary>
        public const float PourEnd = 0.8f;
        public const float PawEnd = 1.3f;
        public const float SlideEnd = 2.5f;
        public const float DrinkEnd = 6.5f;
        /// <summary>The empty glass only lingers a moment, so a cat that wants to can order its next one at once.</summary>
        public const float EmptyEnd = 7.5f;
        /// <summary>Seconds into the paw before it hits the glass (the clink).</summary>
        const float PawHitOffset = 0.2f;
        /// <summary>How long a sent order waits for the authority's stamp before the HUD offers the button again.</summary>
        public const float OrderTimeout = 1f;

        /// <summary>The wine cylinder inside the glass prefab: where its bottom sits and its half-height when full.</summary>
        public const float LiquidBottom = 0.07f;
        public const float LiquidHalfHeight = 0.03f;
        /// <summary>
        /// Where the glass stands while the cat drinks: this far along the seat's forward from its anchor, on the counter
        /// top, under the cat's snout in the Drink pose. The counter top's front edge is 0.415 m ahead of the anchor (the
        /// CounterTop box in CasinoSceneBuilder.BuildBar), so the 0.035 m glass base stands fully on it with room to spare.
        /// </summary>
        public const float DrinkForward = 0.48f;
        /// <summary>Seconds the glass takes to move from the slide lane to the counter's edge, under the cat's snout.</summary>
        const float NudgeSeconds = 0.4f;
        /// <summary>Yaw the sliding glass turns through on its way down the counter.</summary>
        const float SlideSpin = 90f;
        /// <summary>The bartender stays up (and turned to the stool) this long after the glass leaves its paw.</summary>
        const float BartenderHoldAfterSlide = 1f;
        const float BartenderTurnSpeed = 240f;

        [SerializeField] TerraceTable _table;
        [SerializeField] StaffCat _bartender;
        [SerializeField] NpcCatVisual _bartenderVisual;
        [SerializeField] GameObject _glassPrefab;
        [Tooltip("Where a fresh glass appears on the counter, in front of the bartender.")]
        [SerializeField] Transform _pourPoint;
        [Tooltip("A point on the counter top: glasses slide along its height and depth to the stool's x.")]
        [SerializeField] Transform _slideLane;

        /// <summary>Per seat: a counter that changes on every accepted order (0 = never ordered), and when it was accepted.</summary>
        [Networked, Capacity(TerraceTable.MaxSeats)] NetworkArray<byte> OrderIds => default;
        [Networked, Capacity(TerraceTable.MaxSeats)] NetworkArray<int> OrderTicks => default;
        /// <summary>Per seat: who the current order is for.</summary>
        [Networked, Capacity(TerraceTable.MaxSeats)] NetworkArray<PlayerRef> OrderOwners => default;

        /// <summary>What this client has shown so far for a seat, so phase changes fire their one-off effects once.</summary>
        class SeatView
        {
            public Transform Glass;
            public Transform Liquid;
            public byte ShownId;
            public WinePhase ShownPhase;
            public bool PawClinked;
            public PlayerCat Drinker;
            /// <summary>The local player asked for a glass here and hasn't paid for one since.</summary>
            public bool Requested;
            /// <summary>The local player's order ids already paid for, announced and drunk, so each happens once.</summary>
            public byte PaidId;
            public byte AnnouncedId;
            public byte DrunkId;
        }

        SeatView[] _seats = System.Array.Empty<SeatView>();
        Quaternion _bartenderRest;
        int _pendingSeat = -1;
        float _pendingUntil;

        public TerraceTable Table => _table;

        // ---------------------------------------------------------------- Client API

        /// <summary>
        /// The phase a seat's glass is in and seconds since its order was accepted. Idle once the cat it was poured
        /// for has left the stool.
        /// </summary>
        public WinePhase PhaseAt(int seat, out float elapsed)
        {
            elapsed = 0f;
            if (Object == null || !Object.IsValid || seat < 0 || seat >= TerraceTable.MaxSeats || OrderIds[seat] == 0
                || _table.OccupantOf(seat) != OrderOwners[seat])
                return WinePhase.Idle;

            elapsed = (Runner.Tick.Raw - OrderTicks[seat]) * Runner.DeltaTime;
            if (elapsed < 0f)
                return WinePhase.Idle;
            if (elapsed < PourEnd)
                return WinePhase.Pour;
            if (elapsed < PawEnd)
                return WinePhase.Paw;
            if (elapsed < SlideEnd)
                return WinePhase.Slide;
            if (elapsed < DrinkEnd)
                return WinePhase.Drink;
            return elapsed < EmptyEnd ? WinePhase.Empty : WinePhase.Idle;
        }

        /// <summary>The local player sent an order for this seat and the authority hasn't stamped it yet.</summary>
        public bool IsOrderPending(int seat) => _pendingSeat == seat && Time.time < _pendingUntil;

        /// <summary>The seat's occupant can order right now: nothing in progress and no order on its way.</summary>
        public bool CanOrder(int seat) => PhaseAt(seat, out _) == WinePhase.Idle && !IsOrderPending(seat);

        /// <summary>
        /// The local player, seated here, asks for a glass. Pays when the order shows up accepted (see Render);
        /// refuses with a toast when the chips aren't there, silently when the seat is busy.
        /// </summary>
        public void RequestOrder(int seat)
        {
            if (Object == null || !Object.IsValid || !CanOrder(seat) || _table.OccupantOf(seat) != Runner.LocalPlayer)
                return;

            if (ProfileStore.Current.Balance < WinePrice)
            {
                Toasts.Show("Not enough chips for a glass of wine.");
                return;
            }

            _seats[seat].Requested = true;
            _pendingSeat = seat;
            _pendingUntil = Time.time + OrderTimeout;
            RPC_Order((byte)seat);
        }

        // ---------------------------------------------------------------- Authority

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Order(byte seat, RpcInfo info = default)
        {
            if (seat >= TerraceTable.MaxSeats || _table.OccupantOf(seat) != info.Source)
                return;
            // Still serving them (or their empty glass is on the counter): the HUD shouldn't have let them, but check anyway.
            if (PhaseAt(seat, out _) != WinePhase.Idle)
                return;

            OrderIds.Set(seat, (byte)(OrderIds[seat] % 255 + 1));
            OrderTicks.Set(seat, Runner.Tick.Raw);
            OrderOwners.Set(seat, info.Source);
        }

        // ---------------------------------------------------------------- Lifecycle

        public override void Spawned()
        {
            _seats = new SeatView[TerraceTable.MaxSeats];
            for (var seat = 0; seat < _seats.Length; seat++)
            {
                var glass = Instantiate(_glassPrefab, transform).transform;
                glass.name = $"WineGlass_{seat + 1}";
                glass.gameObject.SetActive(false);
                _seats[seat] = new SeatView { Glass = glass, Liquid = glass.Find("Wine") };
            }

            if (_bartenderVisual != null)
                _bartenderRest = _bartenderVisual.transform.rotation;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            foreach (var view in _seats)
            {
                SetDrinking(view, null);
                if (view.Glass != null)
                    Destroy(view.Glass.gameObject);
            }

            _seats = System.Array.Empty<SeatView>();
        }

        public override void Render()
        {
            var serving = false;
            var servingSeat = -1;
            for (var seat = 0; seat < _seats.Length && seat < _table.SeatCount; seat++)
            {
                var view = _seats[seat];
                var id = OrderIds[seat];
                if (id != view.ShownId)
                {
                    // A new order (or the first look at one): start its sequence from whatever phase it's in now.
                    view.ShownId = id;
                    view.ShownPhase = WinePhase.Idle;
                    view.PawClinked = false;
                    if (_pendingSeat == seat)
                        _pendingSeat = -1;
                }

                var phase = PhaseAt(seat, out var elapsed);
                if (id != 0 && OrderOwners[seat] == Runner.LocalPlayer)
                    UpdateOwnOrder(seat, view, id);
                if (phase != view.ShownPhase)
                    OnPhaseChanged(view, phase);
                if (phase == WinePhase.Paw && !view.PawClinked && elapsed >= PourEnd + PawHitOffset)
                {
                    view.PawClinked = true;
                    Sfx.PlayAt(SoundId.PropClink, view.Glass.position, 0.6f);
                }

                UpdateGlass(seat, view, phase, elapsed);
                SetDrinking(view, phase == WinePhase.Drink ? PlayerCat.Find(OrderOwners[seat]) : null);

                if (phase is WinePhase.Pour or WinePhase.Paw or WinePhase.Slide)
                {
                    serving = true;
                    servingSeat = seat;
                }
            }

            UpdateBartender(serving, servingSeat);
        }

        /// <summary>
        /// The local player's own order: pays the first time it shows up accepted, then announces it once served and
        /// counts the glass once drunk. Keyed on the order id rather than on phase changes, so a tab that resumes past
        /// a phase still gets each step; nothing more happens once the cat has left the stool.
        /// </summary>
        void UpdateOwnOrder(int seat, SeatView view, byte id)
        {
            var cat = PlayerCat.Local;
            if (cat == null || _table.OccupantOf(seat) != Runner.LocalPlayer)
                return;
            var elapsed = (Runner.Tick.Raw - OrderTicks[seat]) * Runner.DeltaTime;
            if (elapsed < 0f)
                return;

            if (view.PaidId != id)
            {
                // Only an order this client asked for: a player number can be reused after someone leaves.
                if (!view.Requested)
                    return;
                view.Requested = false;
                view.PaidId = id;
                ProfileStore.SetBalance(Mathf.Max(0, ProfileStore.Current.Balance - WinePrice));
            }

            if (elapsed >= SlideEnd && view.AnnouncedId != id)
            {
                view.AnnouncedId = id;
                cat.Announce($"{cat.DisplayName} ordered a glass of wine.");
            }

            if (elapsed >= DrinkEnd && view.DrunkId != id)
            {
                view.DrunkId = id;
                cat.AddDrink();
            }
        }

        /// <summary>One-off effects when a seat's glass enters a phase: the paw and the arrival clink.</summary>
        void OnPhaseChanged(SeatView view, WinePhase phase)
        {
            var previous = view.ShownPhase;
            view.ShownPhase = phase;

            switch (phase)
            {
                case WinePhase.Paw when previous == WinePhase.Pour:
                    _bartenderVisual?.Reactions?.PlaySwipe();
                    break;
                case WinePhase.Drink when previous == WinePhase.Slide:
                    Sfx.PlayAt(SoundId.PropClink, view.Glass.position, 0.5f);
                    break;
            }
        }

        /// <summary>Places the glass and sets the wine level for the phase; the glass is hidden while the seat is idle.</summary>
        void UpdateGlass(int seat, SeatView view, WinePhase phase, float elapsed)
        {
            var glass = view.Glass;
            if (phase == WinePhase.Idle)
            {
                if (glass.gameObject.activeSelf)
                    glass.gameObject.SetActive(false);
                return;
            }

            if (!glass.gameObject.activeSelf)
                glass.gameObject.SetActive(true);

            var anchor = _table.GetSeatAnchor(seat);
            var counterSpot = new Vector3(anchor.position.x, _slideLane.position.y, _slideLane.position.z);
            var flatForward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized;
            var drinkSpot = anchor.position + flatForward * DrinkForward;
            drinkSpot.y = _slideLane.position.y;
            var level = 1f;
            var position = counterSpot;
            var rotation = Quaternion.identity;

            switch (phase)
            {
                case WinePhase.Pour:
                    position = _pourPoint.position;
                    level = Mathf.Clamp01(elapsed / PourEnd);
                    break;
                case WinePhase.Paw:
                    position = _pourPoint.position;
                    break;
                case WinePhase.Slide:
                {
                    var u = Mathf.Clamp01((elapsed - PawEnd) / (SlideEnd - PawEnd));
                    var t = 1f - (1f - u) * (1f - u) * (1f - u);
                    position = Vector3.Lerp(_pourPoint.position, counterSpot, t);
                    rotation = Quaternion.Euler(0f, SlideSpin * t, 0f);
                    break;
                }
                case WinePhase.Drink:
                {
                    // The glass stands on the counter at the edge and the cat laps at it; it's drawn in from the lane first.
                    var since = elapsed - SlideEnd;
                    position = Vector3.Lerp(counterSpot, drinkSpot, Mathf.SmoothStep(0f, 1f, since / NudgeSeconds));
                    level = 1f - Mathf.Clamp01(since / (DrinkEnd - SlideEnd));
                    break;
                }
                case WinePhase.Empty:
                    position = drinkSpot;
                    level = 0f;
                    break;
            }

            glass.SetPositionAndRotation(position, rotation);
            SetLevel(view, level);
        }

        static void SetLevel(SeatView view, float level)
        {
            if (view.Liquid == null)
                return;
            var show = level > 0.01f;
            if (view.Liquid.gameObject.activeSelf != show)
                view.Liquid.gameObject.SetActive(show);
            if (!show)
                return;
            var half = LiquidHalfHeight * level;
            view.Liquid.localScale = new Vector3(view.Liquid.localScale.x, half, view.Liquid.localScale.z);
            view.Liquid.localPosition = new Vector3(0f, LiquidBottom + half, 0f);
        }

        /// <summary>The seated cat laps at the glass during Drink; the flag is cleared when the phase or the cat changes.</summary>
        static void SetDrinking(SeatView view, PlayerCat drinker)
        {
            if (view.Drinker == drinker)
                return;
            if (view.Drinker != null && view.Drinker.AnimatorDriver != null)
                view.Drinker.AnimatorDriver.Drinking = false;
            view.Drinker = drinker;
            if (drinker != null && drinker.AnimatorDriver != null)
                drinker.AnimatorDriver.Drinking = true;
        }

        /// <summary>The bartender stands and turns to the stool it's serving, then turns back to the room.</summary>
        void UpdateBartender(bool serving, int seat)
        {
            if (_bartenderVisual == null)
                return;

            if (serving)
            {
                _bartender?.HoldStanding(BartenderHoldAfterSlide);
                _bartenderVisual.FaceTowards(_table.GetSeatAnchor(seat).position, BartenderTurnSpeed);
                return;
            }

            var body = _bartenderVisual.transform;
            if (body.rotation != _bartenderRest)
                body.rotation = Quaternion.RotateTowards(body.rotation, _bartenderRest, BartenderTurnSpeed * Time.deltaTime);
        }
    }
}
