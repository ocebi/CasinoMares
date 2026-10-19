using System.Collections.Generic;
using CasinoMares.Player;
using Fusion;
using UnityEngine;

namespace CasinoMares.Social
{
    /// <summary>
    /// Who's on top this session (GDD §7.4): every cat in the room by <see cref="PlayerCat.SessionNet"/>, highest first,
    /// ties by name. Shared by the Tab overlay, the wall board and the leader's nameplate crown, so they always agree.
    /// Ranked at most once a frame, without allocating: entries are kept per cat, names are re-made only when they
    /// change, and the sort uses a cached comparer. Remembers each cat's last rank so a view can show it moving.
    /// </summary>
    public static class SessionRanking
    {
        /// <summary>How long a rank change shows its up/down arrow.</summary>
        public const float MoveShowSeconds = 3f;

        public class Entry
        {
            public PlayerCat Cat;
            /// <summary>1-based place in <see cref="Ranked"/>.</summary>
            public int Rank;
            /// <summary>+1 moved up, -1 moved down at <see cref="MovedAt"/>.</summary>
            public int Move;
            public float MovedAt = float.NegativeInfinity;
            public int Net;
            public string Name = "";
            internal NetworkString<_16> NameSource;
            internal bool Seen;

            /// <summary>The arrow to show now: +1, -1, or 0 once the change is older than <see cref="MoveShowSeconds"/>.</summary>
            public int MoveNow => Time.unscaledTime - MovedAt < MoveShowSeconds ? Move : 0;
        }

        class ByNet : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b)
            {
                var net = b.Net.CompareTo(a.Net);
                return net != 0 ? net : string.CompareOrdinal(a.Name, b.Name);
            }
        }

        static readonly List<Entry> _ranked = new();
        static readonly Dictionary<PlayerCat, Entry> _entries = new();
        static readonly List<PlayerCat> _gone = new();
        static readonly ByNet Comparer = new();
        static int _rankedFrame = -1;
        static int _lastCount;
        static Entry _leader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _ranked.Clear();
            _entries.Clear();
            _rankedFrame = -1;
            _lastCount = 0;
            _leader = null;
        }

        /// <summary>Every spawned cat, best first. Don't keep the list: it's refilled each frame it's asked for.</summary>
        public static List<Entry> Ranked
        {
            get
            {
                Refresh();
                return _ranked;
            }
        }

        /// <summary>The top cat while it's actually up this session (net above 0), else null.</summary>
        public static PlayerCat Leader
        {
            get
            {
                Refresh();
                return _leader?.Cat;
            }
        }

        /// <summary>The entry for <paramref name="cat"/> in this frame's ranking, or null if it isn't ranked.</summary>
        public static Entry Find(PlayerCat cat)
        {
            Refresh();
            return cat != null && _entries.TryGetValue(cat, out var entry) && entry.Seen ? entry : null;
        }

        static void Refresh()
        {
            if (_rankedFrame == Time.frameCount)
                return;
            _rankedFrame = Time.frameCount;

            _ranked.Clear();
            foreach (var entry in _entries.Values)
                entry.Seen = false;

            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.Object == null || !cat.Object.IsValid)
                    continue;
                if (!_entries.TryGetValue(cat, out var entry))
                {
                    entry = new Entry { Cat = cat };
                    _entries.Add(cat, entry);
                }

                var name = cat.DisplayName;
                if (!entry.NameSource.Equals(name) || entry.Name.Length == 0)
                {
                    entry.NameSource = name;
                    entry.Name = name.ToString();
                }

                entry.Net = cat.SessionNet;
                entry.Seen = true;
                _ranked.Add(entry);
            }

            _gone.Clear();
            foreach (var pair in _entries)
            {
                if (!pair.Value.Seen)
                    _gone.Add(pair.Key);
            }

            foreach (var cat in _gone)
                _entries.Remove(cat);

            _ranked.Sort(Comparer);

            // A cat joining or leaving shuffles places without anyone winning anything: no arrows then.
            var sameRoom = _ranked.Count == _lastCount;
            _lastCount = _ranked.Count;
            for (var i = 0; i < _ranked.Count; i++)
            {
                var entry = _ranked[i];
                var rank = i + 1;
                if (sameRoom && entry.Rank != 0 && entry.Rank != rank)
                {
                    entry.Move = rank < entry.Rank ? 1 : -1;
                    entry.MovedAt = Time.unscaledTime;
                }

                entry.Rank = rank;
            }

            _leader = _ranked.Count > 0 && _ranked[0].Net > 0 ? _ranked[0] : null;
        }
    }
}
