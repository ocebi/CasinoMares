using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>
    /// A local-only NPC cat body (dealer, seat filler, staff, patron visual). Same model, animator and
    /// reactions as the player cat, without any networking.
    /// </summary>
    public class NpcCatVisual : MonoBehaviour
    {
        [SerializeField] CatFurPalette _palette;
        [SerializeField] Renderer[] _furRenderers;
        [SerializeField] CatAnimatorDriver _driver;
        [SerializeField] CatReactions _reactions;

        // Serialized: the scene builder picks the dealers' furs, and the recolored ones only load at runtime (Start).
        [SerializeField, HideInInspector] int _fur = -1;
        System.Action<Material> _onFurLoaded;

        public CatReactions Reactions => _reactions;
        public CatAnimatorDriver Driver => _driver;

        public bool Sitting
        {
            get => _driver != null && _driver.Sitting;
            set
            {
                if (_driver != null)
                    _driver.Sitting = value;
            }
        }

        void Start()
        {
            if (_fur >= 0 && _palette != null)
                ApplyFur();
        }

        public void SetFur(int index)
        {
            if (index == _fur || _palette == null)
                return;
            _fur = index;
            ApplyFur();
        }

        /// <summary>The fur shows the black fallback until its own has loaded (<see cref="CatFurPalette.Resolve"/>).</summary>
        void ApplyFur()
        {
            _onFurLoaded ??= _ => { if (this != null) ApplyFur(); };
            var material = _palette.Resolve(_fur, _onFurLoaded);
            foreach (var furRenderer in _furRenderers)
                PlayerCat.SetFur(furRenderer, material);
        }

        /// <summary>Turn to face a point (yaw only), smoothly.</summary>
        public void FaceTowards(Vector3 point, float degreesPerSecond = 240f)
        {
            var direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            var target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * Time.deltaTime);
        }
    }
}
