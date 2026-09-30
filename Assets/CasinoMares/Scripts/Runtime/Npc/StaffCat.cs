using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>
    /// Local ambience for staff cats (bartender, cashier; GDD §4.3): mostly sitting behind the counter, getting up
    /// now and then. Not networked; staff only need to look alive.
    /// </summary>
    public class StaffCat : MonoBehaviour
    {
        [SerializeField] NpcCatVisual _visual;
        [SerializeField] int _fur;
        [SerializeField] Vector2 _sitSeconds = new(8f, 20f);
        [SerializeField] Vector2 _standSeconds = new(3f, 7f);

        float _nextChange;

        void Start()
        {
            _visual.SetFur(_fur);
            _visual.Sitting = true;
            _nextChange = Time.time + Random.Range(_sitSeconds.x, _sitSeconds.y);
        }

        void Update()
        {
            if (Time.time < _nextChange)
                return;

            _visual.Sitting = !_visual.Sitting;
            var range = _visual.Sitting ? _sitSeconds : _standSeconds;
            _nextChange = Time.time + Random.Range(range.x, range.y);
        }
    }
}
