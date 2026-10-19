using System.Collections.Generic;
using CasinoMares.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.Social
{
    /// <summary>
    /// The in-world leaderboard on the wall (GDD §7.4). A UIDocument rendered into a RenderTexture that the board
    /// mesh displays; refreshed once a second with the same session ranking and the same <c>lb-row</c> rows as the Tab
    /// overlay (<see cref="SessionRanking"/>, <see cref="LeaderboardPanel.MakeRow"/>), minus the "you" highlight: it's a
    /// shared wall. Rows are made once and only rewritten when what they show changes.
    /// </summary>
    public class LeaderboardBoard : MonoBehaviour
    {
        const int MaxRows = 10;

        [SerializeField] UIDocument _document;
        [SerializeField] float _refreshSeconds = 1f;

        VisualElement _rows;
        Label _empty;
        float _nextRefresh;
        readonly List<LeaderboardPanel.Slot> _rowViews = new();
        int _shown = -1;

        void OnEnable()
        {
            // The panel only draws into the board's texture. Without this, UI Toolkit would still map screen clicks and
            // touches onto it as if it covered the top-left of the screen, swallowing them over the title and rows.
            _document.panelSettings.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));

            var ui = _document.rootVisualElement;
            _rows = ui.Q("board-rows");
            _empty = ui.Q<Label>("board-empty");
            _rows?.Clear();
            _rowViews.Clear();
            _shown = -1;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh || _rows == null)
                return;
            _nextRefresh = Time.unscaledTime + _refreshSeconds;

            var ranked = SessionRanking.Ranked;
            var anyNet = false;
            for (var i = 0; i < ranked.Count; i++)
                anyNet |= ranked[i].Net != 0;

            // "Earn your first chips" until somebody has won or lost something.
            var count = anyNet ? Mathf.Min(ranked.Count, MaxRows) : 0;
            while (_rowViews.Count < count)
            {
                var row = LeaderboardPanel.MakeRow();
                _rows.Add(row.Root);
                _rowViews.Add(row);
            }

            for (var i = 0; i < count; i++)
                _rowViews[i].Show(ranked[i], false);

            if (count != _shown)
            {
                _shown = count;
                for (var i = 0; i < _rowViews.Count; i++)
                    _rowViews[i].Root.EnableInClassList("hidden", i >= count);
                _empty.EnableInClassList("hidden", count > 0);
            }
        }
    }
}
