using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>
    /// The casino entrance. NPC players walk in through it to take a seat and walk out through it when they leave
    /// (<see cref="NpcSeating"/>). Sits in the dark vestibule behind the doorway, where cats appear and vanish.
    /// </summary>
    public class CasinoDoor : MonoBehaviour
    {
        static CasinoDoor _instance;

        public static bool Exists => _instance != null;
        public static Vector3 Inside => _instance != null ? _instance.transform.position : Vector3.zero;

        void OnEnable() => _instance = this;

        void OnDisable()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
