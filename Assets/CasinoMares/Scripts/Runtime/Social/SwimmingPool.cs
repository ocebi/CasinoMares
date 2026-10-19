using System.Collections.Generic;
using UnityEngine;

namespace CasinoMares.Social
{
    /// <summary>
    /// The terrace pool's water (plan §11.2): the box a cat swims in, over the deep part of the pool and the lower
    /// steps. <see cref="Player.CatMovement"/> asks it every tick on the owner: stepping in sets
    /// <see cref="Player.PlayerCat.Swimming"/>, stepping out (up the steps) clears it, and in between the cat is held
    /// at <see cref="Surface"/>. A box test, not a trigger collider: the pointer and camera raycasts hit triggers.
    /// </summary>
    public class SwimmingPool : MonoBehaviour
    {
        public static readonly List<SwimmingPool> All = new();

        [Tooltip("The swimmable box round this object's position, world-aligned (the object isn't rotated or scaled).")]
        [SerializeField] Vector3 _size = new(5f, 1.6f, 4f);
        [Tooltip("The water surface, measured up from this object's position.")]
        [SerializeField] float _surface;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public float Surface => transform.position.y + _surface;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 position)
        {
            var offset = position - transform.position;
            return Mathf.Abs(offset.x) <= _size.x / 2f && Mathf.Abs(offset.y) <= _size.y / 2f && Mathf.Abs(offset.z) <= _size.z / 2f;
        }

        /// <summary>Whether <paramref name="position"/> (a cat's feet) is in any pool's water, and that water's surface.</summary>
        public static bool WaterAt(Vector3 position, out float surface)
        {
            foreach (var pool in All)
            {
                if (pool.Contains(position))
                {
                    surface = pool.Surface;
                    return true;
                }
            }

            surface = 0f;
            return false;
        }
    }
}
