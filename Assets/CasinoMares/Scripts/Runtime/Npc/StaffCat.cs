using UnityEngine;

namespace CasinoMares.Npc
{
    /// <summary>
    /// Local ambience for staff cats (bartender, cashier; GDD §4.3): mostly sitting behind the counter, getting up
    /// now and then. Not networked; staff only need to look alive. Something with work for the cat (the wine bar)
    /// can hold it standing for a while.
    /// </summary>
    public class StaffCat : MonoBehaviour
    {
        [SerializeField] NpcCatVisual _visual;
        [SerializeField] int _fur;
        [SerializeField] Vector2 _sitSeconds = new(8f, 20f);
        [SerializeField] Vector2 _standSeconds = new(3f, 7f);

        float _nextChange;
        float _holdUntil;
        bool _held;

        void Start()
        {
            _visual.SetFur(_fur);
            _visual.Sitting = true;
            _nextChange = Time.time + Random.Range(_sitSeconds.x, _sitSeconds.y);
        }

        /// <summary>Stand up (if sitting) and stay up for at least <paramref name="seconds"/> from now.</summary>
        public void HoldStanding(float seconds)
        {
            _holdUntil = Mathf.Max(_holdUntil, Time.time + seconds);
            _visual.Sitting = false;
        }

        void Update()
        {
            if (Time.time < _holdUntil)
            {
                _held = true;
                return;
            }

            if (_held)
            {
                // Released: a normal stand before the next sit, not an instant flop onto the stool.
                _held = false;
                _nextChange = Time.time + Random.Range(_standSeconds.x, _standSeconds.y);
                return;
            }

            if (Time.time < _nextChange)
                return;

            _visual.Sitting = !_visual.Sitting;
            var range = _visual.Sitting ? _sitSeconds : _standSeconds;
            _nextChange = Time.time + Random.Range(range.x, range.y);
        }
    }
}
