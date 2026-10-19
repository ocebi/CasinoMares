using CasinoMares.Npc;
using Fusion;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The NPC budget (plan 2026-10-19 §13, GDD §4.5): one master-client object that scales the patrons and the tables'
    /// NPC players to the number of players in the hall. Both casinos get it.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        static void BuildNpcBudget()
        {
            var budget = new GameObject("NpcBudget");
            MakeMasterClientObject(budget.AddComponent<NetworkObject>());
            budget.AddComponent<NpcBudget>();
        }
    }
}
