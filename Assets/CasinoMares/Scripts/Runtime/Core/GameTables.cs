using System.Collections.Generic;
using System.Text;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// A casino game you sit at (blackjack now, roulette/poker later). Seating, the seat camera and spectating are
    /// shared across games through this interface (GDD §9.6).
    /// </summary>
    public interface IGameTable
    {
        string DisplayName { get; }
        Transform transform { get; }

        /// <summary>The seat this player occupies at the table, if any.</summary>
        bool TryGetSeatOf(PlayerRef player, out int seat);

        Transform GetSeatAnchor(int seat);
        CinemachineCamera GetSeatCamera(int seat);

        /// <summary>A fixed camera above the felt for the overhead table view (GDD §3.5); null where there is none.</summary>
        CinemachineCamera GetOverheadCamera() => null;

        /// <summary>Ask the table's authority to free this seat for the local player.</summary>
        void RequestLeave(int seat);

        /// <summary>Spectator panel text for a seated player (GDD §6). Hidden info must not be included.</summary>
        bool TryDescribePlayer(PlayerRef player, StringBuilder text);
    }

    public static class GameTables
    {
        /// <summary>
        /// Seconds between the last card (or the ball dropping) and the result: time to take it in before the
        /// labels, payouts and the cats' reactions give it away.
        /// </summary>
        public const float ResultPause = 1.5f;

        public static readonly List<IGameTable> All = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public static bool TryFindSeat(PlayerRef player, out IGameTable table, out int seat)
        {
            foreach (var candidate in All)
            {
                if (candidate.TryGetSeatOf(player, out seat))
                {
                    table = candidate;
                    return true;
                }
            }

            table = null;
            seat = -1;
            return false;
        }
    }
}
