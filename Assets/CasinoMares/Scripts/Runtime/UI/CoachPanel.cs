using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Teaching mode shared by the poker and baccarat HUDs: a coach card with live advice (while the coach is on)
    /// and a rules overlay (always one click away). The coach setting is personal, saved on this machine only, and
    /// never shown to the other players at the table.
    /// </summary>
    public class CoachPanel
    {
        readonly string _prefsKey;
        readonly VisualElement _card;
        readonly Label _title;
        readonly Label _body;
        readonly VisualElement _meter;
        readonly VisualElement _meterFill;
        readonly Label _meterLabel;
        readonly Label _tip;
        readonly VisualElement _rules;
        bool _seated;

        public CoachPanel(VisualElement root, string prefsKey, string rulesTitle, IReadOnlyList<(string heading, string body)> rules)
        {
            _prefsKey = prefsKey;

            _card = new VisualElement { pickingMode = PickingMode.Ignore };
            _card.AddToClassList("panel");
            _card.AddToClassList("coach");
            _title = AddLabel(_card, "coach-title");
            _body = AddLabel(_card, "coach-body");
            _meter = new VisualElement { pickingMode = PickingMode.Ignore };
            _meter.AddToClassList("coach-meter");
            _meterFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _meterFill.AddToClassList("coach-meter-fill");
            _meter.Add(_meterFill);
            _meterLabel = AddLabel(_meter, "coach-meter-label");
            _card.Add(_meter);
            _tip = AddLabel(_card, "coach-tip");
            root.Add(_card);

            _rules = BuildRules(rulesTitle, rules);
            root.Add(_rules);
            Refresh();
        }

        /// <summary>Live advice on or off (off by default).</summary>
        public bool Enabled
        {
            get => PlayerPrefs.GetInt(_prefsKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(_prefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Refresh();
            }
        }

        public bool RulesOpen => !_rules.ClassListContains("hidden");

        /// <summary>Call when the local player sits down or gets up.</summary>
        public void SetSeated(bool seated)
        {
            _seated = seated;
            if (!seated)
                ShowRules(false);
            Refresh();
        }

        public void ShowRules(bool show) => _rules.EnableInClassList("hidden", !show);

        /// <summary>
        /// Update the card. <paramref name="meter"/> is a 0..1 bar (a win chance), or negative to hide it.
        /// Text may use rich text tags.
        /// </summary>
        public void Set(string title, string body, string tip, float meter = -1f, string meterText = null)
        {
            _title.text = title;
            _body.text = body;
            _body.EnableInClassList("hidden", string.IsNullOrEmpty(body));
            _tip.text = tip;
            _tip.EnableInClassList("hidden", string.IsNullOrEmpty(tip));
            _meter.EnableInClassList("hidden", meter < 0f);
            if (meter >= 0f)
            {
                _meterFill.style.width = Length.Percent(Mathf.Clamp01(meter) * 100f);
                _meterLabel.text = meterText;
            }
        }

        void Refresh() => _card.EnableInClassList("hidden", !_seated || !Enabled);

        VisualElement BuildRules(string title, IReadOnlyList<(string heading, string body)> sections)
        {
            var backdrop = new VisualElement();
            backdrop.AddToClassList("screen");
            backdrop.AddToClassList("rules-backdrop");
            backdrop.AddToClassList("hidden");

            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("rules-panel");
            backdrop.Add(panel);

            var header = new VisualElement();
            header.AddToClassList("rules-header");
            AddLabel(header, "rules-title").text = title;
            var close = new Button(() => ShowRules(false)) { text = "Close" };
            close.AddToClassList("bj-button");
            close.AddToClassList("bj-button--secondary");
            header.Add(close);
            panel.Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("rules-scroll");
            foreach (var (heading, body) in sections)
            {
                AddLabel(scroll, "rules-heading").text = heading;
                AddLabel(scroll, "rules-body").text = body;
            }

            panel.Add(scroll);
            return backdrop;
        }

        static Label AddLabel(VisualElement parent, string className)
        {
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }
    }
}
