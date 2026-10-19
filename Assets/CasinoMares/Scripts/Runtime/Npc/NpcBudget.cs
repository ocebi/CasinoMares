using CasinoMares.Player;
using Fusion;
using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>
    /// How many NPC cats the hall has for the number of players in it (GDD §4.5): the fewer players, the more NPCs, so the
    /// casino feels about as busy alone as it does full. Networked scene object simulated by the master client. It
    /// settles a few seconds after a player joins or leaves, then sets how many of the wandering patrons are in
    /// (<see cref="PatronWanted"/>; the others walk out of the entrance and wait there) and how much the tables' "random"
    /// NPC players are scaled (<see cref="SeatScale"/>, applied when a table next picks how many cats it wants).
    /// </summary>
    public class NpcBudget : NetworkBehaviour
    {
        /// <summary>Wandering patrons in each scene (CasinoSceneBuilder builds ten).</summary>
        public const int FullPatrons = 10;
        /// <summary>Seat cats with every table on "random": five tables times the 1.72 average target, rounded.</summary>
        public const int FullSeatCats = 9;
        /// <summary>The NPCs a solo player has, today's numbers before the budget.</summary>
        public const int FullTotal = FullPatrons + FullSeatCats;
        /// <summary>Cats in the hall, players and NPCs, that the budget aims for.</summary>
        public const int TargetCats = FullTotal + 1;
        /// <summary>The fewest NPCs, about a quarter of the full set, so a full room of ten still has a few around.</summary>
        public const int MinimumNpcs = 5;
        /// <summary>Seconds after a join or leave before the budget changes, so a quick rejoin doesn't make cats come and go.</summary>
        const float SettleSeconds = 10f;

        static NpcBudget _instance;

        [Networked] byte Patrons { get; set; }
        [Networked] byte SeatPercent { get; set; }
        [Networked] byte Players { get; set; }
        [Networked] TickTimer Settle { get; set; }
        [Networked] NetworkBool Initialized { get; set; }

        /// <summary>Whether the patron at <paramref name="index"/> (its place under Patrons) is in the hall; all are without a budget.</summary>
        public static bool PatronWanted(int index) => !Ready || index < _instance.Patrons;

        /// <summary>What the tables' "random" NPC targets are multiplied by; 1 without a budget.</summary>
        public static float SeatScale => Ready ? _instance.SeatPercent / 100f : 1f;

        static bool Ready => _instance != null && _instance.Object != null && _instance.Object.IsValid && _instance.Initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        /// <summary>NPCs for <paramref name="players"/> players: the target less the players, between the minimum and the full set.</summary>
        public static int Budget(int players) => Mathf.Clamp(TargetCats - players, MinimumNpcs, FullTotal);

        /// <summary>The budget split between patrons and seat cats in the full set's proportion.</summary>
        public static void Split(int players, out int patrons, out int seatCats)
        {
            var budget = Budget(players);
            patrons = Mathf.RoundToInt(budget * (float)FullPatrons / FullTotal);
            seatCats = budget - patrons;
        }

        public override void Spawned() => _instance = this;

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_instance == this)
                _instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var players = PlayerCat.All.Count;
            if (!Initialized)
            {
                // The first master sets the budget at once: there's nothing to settle yet.
                Apply(players);
                Initialized = true;
                return;
            }

            if (players != Players)
            {
                Players = (byte)players;
                Settle = TickTimer.CreateFromSeconds(Runner, SettleSeconds);
            }

            if (Settle.Expired(Runner))
            {
                Settle = TickTimer.None;
                Apply(players);
            }
        }

        void Apply(int players)
        {
            Players = (byte)players;
            Split(players, out var patrons, out var seatCats);
            Patrons = (byte)patrons;
            SeatPercent = (byte)Mathf.RoundToInt(100f * seatCats / FullSeatCats);
        }
    }
}
