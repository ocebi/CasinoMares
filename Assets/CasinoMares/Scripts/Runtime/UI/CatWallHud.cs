using CasinoMares.Props;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>The cat wall's clock, top middle: "Ready" on the start rug, then the time while climbing and at the top.</summary>
    public class CatWallHud : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _title;
        Label _time;
        Label _best;
        CatWall.Phase _phase = (CatWall.Phase)(-1);
        int _shownTenths = -1;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _root = ui.Q("climb-root");
            _title = ui.Q<Label>("climb-title");
            _time = ui.Q<Label>("climb-time");
            _best = ui.Q<Label>("climb-best");
        }

        void Update()
        {
            var wall = CatWall.Instance;
            var phase = wall != null ? wall.State : CatWall.Phase.Idle;
            if (phase != _phase)
            {
                _phase = phase;
                _shownTenths = -1;
                _root.EnableInClassList("hidden", phase == CatWall.Phase.Idle);
                _root.EnableInClassList("climb--done", phase == CatWall.Phase.Finished);
                _title.text = phase switch
                {
                    CatWall.Phase.Ready => "Cat wall: jump on to start",
                    CatWall.Phase.Finished => wall.NewBest ? "New best!" : "Top!",
                    _ => "Cat wall",
                };
                var best = CatWall.Best;
                _best.text = best > 0f ? $"Best {CatWall.Format(best)}" : "";
                _best.EnableInClassList("hidden", best <= 0f);
            }

            var showTime = phase is CatWall.Phase.Climbing or CatWall.Phase.Finished;
            _time.EnableInClassList("hidden", !showTime);
            if (!showTime)
                return;
            var tenths = Mathf.FloorToInt(wall.Elapsed * 10f);
            if (tenths == _shownTenths)
                return;
            _shownTenths = tenths;
            _time.text = CatWall.Format(wall.Elapsed);
        }
    }
}
