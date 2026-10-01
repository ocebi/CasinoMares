using System.Linq;
using CasinoMares.Player;
using CasinoMares.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.Social
{
    /// <summary>
    /// The in-world leaderboard on the wall (GDD §7.4). A UIDocument rendered into a RenderTexture that the board
    /// mesh displays; refreshed once a second from the networked balances.
    /// </summary>
    public class LeaderboardBoard : MonoBehaviour
    {
        [SerializeField] UIDocument _document;
        [SerializeField] float _refreshSeconds = 1f;

        VisualElement _rows;
        Label _empty;
        float _nextRefresh;

        void OnEnable()
        {
            // The panel only draws into the board's texture. Without this, UI Toolkit would still map screen clicks and
            // touches onto it as if it covered the top-left of the screen, swallowing them over the title and rows.
            _document.panelSettings.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));

            var ui = _document.rootVisualElement;
            _rows = ui.Q("board-rows");
            _empty = ui.Q<Label>("board-empty");
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh || _rows == null)
                return;
            _nextRefresh = Time.unscaledTime + _refreshSeconds;

            _rows.Clear();
            var rank = 1;
            foreach (var cat in PlayerCat.All.Where(c => c != null && c.Object != null && c.Object.IsValid).OrderByDescending(c => c.Balance).Take(10))
                _rows.Add(HudController.LeaderboardRow(rank++, cat));
            _empty.EnableInClassList("hidden", rank > 1);
        }
    }
}
