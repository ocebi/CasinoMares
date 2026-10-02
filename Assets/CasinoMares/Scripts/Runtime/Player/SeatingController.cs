using System;
using CasinoMares.CameraRig;
using CasinoMares.Core;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Local player only. Watches the game tables for a seat assigned to this player and sits the cat down (or
    /// stands it up) to match: teleports to the seat, locks movement, switches to the seat camera and frees the
    /// cursor. Tables own seat assignment; this just follows it, so it also copes with being kicked or migrating.
    /// </summary>
    public class SeatingController : NetworkBehaviour
    {
        [SerializeField] float _standUpDistance = 0.9f;

        public static event Action<IGameTable, int> LocalSeatChanged;

        public static IGameTable LocalTable { get; private set; }
        public static int LocalSeat { get; private set; } = -1;

        PlayerCat _cat;
        CatMovement _movement;
        bool _cursorFreed;
        float _seatedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LocalSeatChanged = null;
            LocalTable = null;
            LocalSeat = -1;
        }

        void Awake()
        {
            _cat = GetComponent<PlayerCat>();
            _movement = GetComponent<CatMovement>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (HasStateAuthority && LocalTable != null)
                ApplySeat(null, -1);
        }

        void Update()
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority)
                return;

            GameTables.TryFindSeat(Runner.LocalPlayer, out var table, out var seat);
            if (table != LocalTable || seat != LocalSeat)
                ApplySeat(table, seat);

            // The [E] press that asked for the seat can still be "pressed" when the seat arrives; ignore it.
            if (LocalTable != null && Time.time - _seatedAt > 0.3f && GameInput.InteractPressed())
                LocalTable.RequestLeave(LocalSeat);
        }

        void ApplySeat(IGameTable table, int seat)
        {
            var previousAnchor = LocalTable != null && LocalSeat >= 0 ? LocalTable.GetSeatAnchor(LocalSeat) : null;

            LocalTable = table;
            LocalSeat = seat;

            if (table != null)
            {
                _seatedAt = Time.time;
                var anchor = table.GetSeatAnchor(seat);
                _movement.MovementLocked = true;
                _movement.RequestTeleport(anchor.position, anchor.rotation);
                _cat.IsSeated = true;
                PlayerCameraRig.Instance?.SetOverride(table.GetSeatCamera(seat), anchor);
                if (!_cursorFreed)
                {
                    GameInput.PushCursorFree();
                    _cursorFreed = true;
                }
            }
            else
            {
                if (previousAnchor != null)
                {
                    var standPosition = previousAnchor.position - previousAnchor.forward * _standUpDistance;
                    standPosition.y = Mathf.Max(0.05f, standPosition.y - 0.4f);
                    _movement.RequestTeleport(standPosition, previousAnchor.rotation);
                }

                _movement.MovementLocked = false;
                _cat.IsSeated = false;
                PlayerCameraRig.Instance?.ClearOverride();
                if (_cursorFreed)
                {
                    GameInput.PopCursorFree();
                    _cursorFreed = false;
                }
            }

            LocalSeatChanged?.Invoke(table, seat);
        }
    }
}
