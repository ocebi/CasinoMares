using System;
using System.Collections.Generic;
using System.Text;
using CasinoMares.Core;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Social
{
    /// <summary>
    /// A café table on the seaside terrace: somewhere to sit, watch the sea and chat, with no game. It seats players
    /// through the same <see cref="IGameTable"/> path as the games (seat camera, Stand up, the seat anchor), and
    /// patrons sit on the free seats (<see cref="Npc.PatronSpot"/>).
    /// </summary>
    public class TerraceTable : NetworkBehaviour, IGameTable, IPlayerLeft
    {
        public const int MaxSeats = 3;

        public static readonly List<TerraceTable> All = new();

        [SerializeField] string _displayName = "Terrace table";
        [SerializeField] Transform[] _seatAnchors = Array.Empty<Transform>();
        [SerializeField] CinemachineCamera[] _seatCameras = Array.Empty<CinemachineCamera>();

        [Networked, Capacity(MaxSeats)] NetworkArray<PlayerRef> Occupants => default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public string DisplayName => _displayName;
        public int SeatCount => _seatAnchors.Length;

        public PlayerRef OccupantOf(int seat) =>
            Object != null && Object.IsValid && seat >= 0 && seat < SeatCount ? Occupants[seat] : PlayerRef.None;

        public bool IsTaken(int seat) => OccupantOf(seat) != PlayerRef.None;

        public override void Spawned()
        {
            All.Add(this);
            GameTables.All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            GameTables.All.Remove(this);
        }

        // ---------------------------------------------------------------- IGameTable

        public bool TryGetSeatOf(PlayerRef player, out int seat)
        {
            for (seat = 0; seat < SeatCount; seat++)
            {
                if (player != PlayerRef.None && OccupantOf(seat) == player)
                    return true;
            }

            seat = -1;
            return false;
        }

        public Transform GetSeatAnchor(int seat) => _seatAnchors[seat];
        public CinemachineCamera GetSeatCamera(int seat) => _seatCameras[seat];
        public void RequestLeave(int seat) => RPC_Leave((byte)seat);

        /// <summary>Nothing to report about someone who's just enjoying the view.</summary>
        public bool TryDescribePlayer(PlayerRef player, StringBuilder text) => false;

        // ---------------------------------------------------------------- Authority

        public void RequestSeat(int seat) => RPC_Sit((byte)seat);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Sit(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && Occupants[seat] == PlayerRef.None && !GameTables.TryFindSeat(info.Source, out _, out _))
                Occupants.Set(seat, info.Source);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_Leave(byte seat, RpcInfo info = default)
        {
            if (seat < SeatCount && Occupants[seat] == info.Source)
                Occupants.Set(seat, PlayerRef.None);
        }

        void IPlayerLeft.PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;
            for (var seat = 0; seat < SeatCount; seat++)
            {
                if (Occupants[seat] == player)
                    Occupants.Set(seat, PlayerRef.None);
            }
        }
    }
}
