using System.Collections.Generic;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Social;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// The Tab / Top cats overlay (GDD §7.4, UX §1.10): Hud.uxml's podium for the top three, rows for 4th to 10th
    /// (4th to 6th on a phone) and, when the local cat is further down, the "···" gap and its own pinned row. Ranked
    /// by <see cref="SessionRanking"/>. The podium, gap, pinned holder and empty note come from the UXML; the rows are
    /// made here once and reused. Every text and class is written only when its value changes, so an open board makes
    /// no garbage per frame.
    /// </summary>
    public class LeaderboardPanel
    {
        const int DesktopRows = 10;
        const int TouchRows = 6;
        const int PodiumPlaces = 3;
        const string Hidden = "hidden";
        const string YouSuffix = " (you)";

        /// <summary>A podium card or a list row, remembering what it shows so it only writes changes.</summary>
        public class Slot
        {
            public VisualElement Root;
            public Label Rank;
            public VisualElement Move;
            public VisualElement Swatch;
            public Label Name;
            public Label Net;
            int _rank = -1;
            int _move = int.MinValue;
            int _net = int.MinValue;
            string _name;
            bool _you;
            Color _swatch;
            bool _empty;

            /// <summary>
            /// Shows <paramref name="entry"/>; <paramref name="you"/> adds " (you)" and, on a row,
            /// <c>lb-row--you</c>.
            /// </summary>
            public void Show(SessionRanking.Entry entry, bool you)
            {
                SetEmpty(false);
                if (Rank != null && entry.Rank != _rank)
                {
                    _rank = entry.Rank;
                    Rank.text = RankText(_rank);
                }

                var move = entry.MoveNow;
                if (Move != null && move != _move)
                {
                    _move = move;
                    Move.EnableInClassList("lb-move--up", move > 0);
                    Move.EnableInClassList("lb-move--down", move < 0);
                }

                if (!ReferenceEquals(entry.Name, _name) || you != _you)
                {
                    _name = entry.Name;
                    Name.text = you ? _name + YouSuffix : _name;
                    if (Rank != null)
                        Root.EnableInClassList("lb-row--you", you);
                    _you = you;
                }

                var swatch = entry.Cat.Swatch;
                if (swatch != _swatch)
                {
                    _swatch = swatch;
                    Swatch.style.backgroundColor = swatch;
                }

                if (entry.Net != _net)
                {
                    _net = entry.Net;
                    Net.text = NetText(_net);
                    Net.EnableInClassList("lb-net--pos", _net > 0);
                    Net.EnableInClassList("lb-net--neg", _net < 0);
                }
            }

            /// <summary>A podium place with nobody on it: dimmed and blank.</summary>
            public void SetEmpty(bool empty)
            {
                if (empty == _empty)
                    return;
                _empty = empty;
                Root.EnableInClassList("lb-podium--empty", empty);
                if (!empty)
                    return;
                _name = null;
                _net = int.MinValue;
                _swatch = default;
                Name.text = "";
                Net.text = "";
                Net.EnableInClassList("lb-net--pos", false);
                Net.EnableInClassList("lb-net--neg", false);
                Swatch.style.backgroundColor = StyleKeyword.Null;
            }
        }

        static readonly string[] RankTexts = new string[64];
        static readonly Dictionary<int, string> NetTexts = new();

        readonly VisualElement _podium;
        readonly Slot[] _places = new Slot[PodiumPlaces];
        readonly VisualElement _rowsHost;
        readonly List<Slot> _rows = new();
        readonly VisualElement _gap;
        readonly VisualElement _pinned;
        readonly Slot _you;
        readonly VisualElement _empty;
        int _rowsShown = -1;
        int _pinShown = -1;
        int _emptyShown = -1;

        public LeaderboardPanel(VisualElement leaderboard)
        {
            _podium = leaderboard.Q("lb-podium");
            for (var place = 0; place < PodiumPlaces; place++)
            {
                var card = leaderboard.Q($"lb-podium-{place + 1}");
                _places[place] = new Slot
                {
                    Root = card,
                    Swatch = card.Q(className: "lb-podium-swatch"),
                    Name = card.Q<Label>(className: "lb-podium-name"),
                    Net = card.Q<Label>(className: "lb-podium-net"),
                };
            }

            _rowsHost = leaderboard.Q("leaderboard-rows");
            _gap = leaderboard.Q("lb-gap");
            _pinned = leaderboard.Q("lb-you-pinned");
            _empty = leaderboard.Q("lb-empty");
            _you = MakeRow();
            _pinned.Add(_you.Root);
        }

        public void Update()
        {
            var ranked = SessionRanking.Ranked;
            var local = PlayerCat.Local;
            var anyNet = false;
            SessionRanking.Entry mine = null;
            for (var i = 0; i < ranked.Count; i++)
            {
                anyNet |= ranked[i].Net != 0;
                if (ranked[i].Cat == local)
                    mine = ranked[i];
            }

            // Nobody has won or lost anything yet: just the "Earn your first chips" note.
            var empty = anyNet ? 0 : 1;
            if (empty != _emptyShown)
            {
                _emptyShown = empty;
                _empty.EnableInClassList(Hidden, anyNet);
                _podium.EnableInClassList(Hidden, !anyNet);
                _rowsHost.EnableInClassList(Hidden, !anyNet);
            }

            var lastRank = Platform.IsTouch ? TouchRows : DesktopRows;
            var pin = anyNet && mine != null && mine.Rank > lastRank ? 1 : 0;
            if (pin != _pinShown)
            {
                _pinShown = pin;
                _gap.EnableInClassList(Hidden, pin == 0);
                _pinned.EnableInClassList(Hidden, pin == 0);
            }

            if (!anyNet)
                return;

            for (var place = 0; place < PodiumPlaces; place++)
            {
                if (place < ranked.Count)
                    _places[place].Show(ranked[place], ranked[place] == mine);
                else
                    _places[place].SetEmpty(true);
            }

            var rows = Mathf.Max(0, Mathf.Min(ranked.Count, lastRank) - PodiumPlaces);
            while (_rows.Count < rows)
            {
                var row = MakeRow();
                _rowsHost.Add(row.Root);
                _rows.Add(row);
            }

            for (var i = 0; i < rows; i++)
                _rows[i].Show(ranked[PodiumPlaces + i], ranked[PodiumPlaces + i] == mine);
            if (rows != _rowsShown)
            {
                _rowsShown = rows;
                for (var i = 0; i < _rows.Count; i++)
                    _rows[i].Root.EnableInClassList(Hidden, i >= rows);
            }

            if (pin == 1)
                _you.Show(mine, true);
        }

        /// <summary>A list row: rank, move arrow, swatch, name, net, in that order (UX §1.10). The wall board uses it too.</summary>
        public static Slot MakeRow()
        {
            var row = new Slot { Root = Make<VisualElement>("lb-row") };
            row.Rank = Add(row.Root, Make<Label>("lb-rank"));
            row.Move = Add(row.Root, Make<VisualElement>("lb-move"));
            row.Swatch = Add(row.Root, Make<VisualElement>("lb-swatch"));
            row.Name = Add(row.Root, Make<Label>("lb-name"));
            row.Net = Add(row.Root, Make<Label>("lb-net"));
            return row;
        }

        static T Make<T>(string className) where T : VisualElement, new()
        {
            var element = new T { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        static string RankText(int rank)
        {
            if (rank < 0 || rank >= RankTexts.Length)
                return rank.ToString();
            return RankTexts[rank] ??= rank.ToString();
        }

        /// <summary>"+1,240", "-80" or "0" (a plain hyphen: the UI font has no minus sign), made once per value.</summary>
        public static string NetText(int net)
        {
            if (NetTexts.TryGetValue(net, out var text))
                return text;
            if (NetTexts.Count > 512)
                NetTexts.Clear();
            text = net > 0 ? $"+{net:N0}" : net.ToString("N0");
            NetTexts.Add(net, text);
            return text;
        }
    }
}
