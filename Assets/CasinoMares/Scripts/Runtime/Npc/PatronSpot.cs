using System.Collections.Generic;
using CasinoMares.Social;
using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>A point of interest ambient patrons wander to (bar, lounge cushion, near a table...).</summary>
    public class PatronSpot : MonoBehaviour
    {
        public static readonly List<PatronSpot> All = new();

        [Tooltip("Patrons sit down here instead of standing around.")]
        [SerializeField] bool _sit;
        [SerializeField] Vector2 _stayRange = new(6f, 16f);
        [Tooltip("A seat players can take too (terrace chairs): patrons leave it to them.")]
        [SerializeField] TerraceTable _playerTable;
        [SerializeField] int _playerSeat = -1;

        public bool Sit => _sit;
        public Vector2 StayRange => _stayRange;

        /// <summary>A player is sitting here, so no patron should be.</summary>
        public bool TakenByPlayer => _playerTable != null && _playerTable.IsTaken(_playerSeat);

        /// <summary>Set by the patron that claimed the spot so two cats don't stack on one cushion.</summary>
        public PatronBrain ClaimedBy { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void OnEnable() => All.Add(this);

        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = _sit ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.3f, 0.8f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.4f);
        }
    }
}
