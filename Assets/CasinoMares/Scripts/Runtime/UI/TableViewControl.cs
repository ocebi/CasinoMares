using CasinoMares.CameraRig;
using CasinoMares.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// The View button, its caption and the V key that a card table's HUD shares (UX §1.4, GDD §3.5): cycles
    /// <see cref="PlayerCameraRig.CycleTableView"/> and names the view beside the button for a moment. The HUD's
    /// document holds a <c>view-button</c> and a <c>view-caption</c> in its corner row; the HUD calls
    /// <see cref="OnSeated"/> from its seat-change handler and <see cref="Update"/> each frame. Every card table's HUD
    /// has one, so only the one whose table the player sits at answers the key or the button. On touch the button is an
    /// icon showing the view a tap switches to (UX §1.9).
    /// </summary>
    public sealed class TableViewControl
    {
        /// <summary>Seconds the view's name stays beside the button after a change (then fades, UX §1.4).</summary>
        const float CaptionSeconds = 1.5f;
        const string CaptionShowClass = "bj-view-caption--show";

        readonly Label _caption;
        readonly VisualElement _icon;
        string _iconClass;
        float _captionUntil;
        bool _seated;

        public TableViewControl(VisualElement ui)
        {
            _caption = ui.Q<Label>("view-caption");
            var button = ui.Q<Button>("view-button");
            _icon = button.Q(className: "btn-icon");
            button.clicked += Cycle;
        }

        /// <summary>Sitting down restores the last view for this game; say which, unless it's the plain seat view.</summary>
        public void OnSeated(bool seated)
        {
            _seated = seated;
            var rig = PlayerCameraRig.Instance;
            if (seated && rig != null && rig.CurrentTableView != TableView.Seat)
                ShowCaption(rig.CurrentTableView);
            else
                HideCaption();
            ShowNextIcon(rig != null ? rig.CurrentTableView : TableView.Seat);
        }

        /// <summary>Reads the V key (any phase, not while a text field has the keyboard) and fades the caption out.</summary>
        public void Update()
        {
            if (_seated && GameInput.KeyPressed(Key.V))
                Cycle();
            if (_captionUntil > 0f && Time.time >= _captionUntil)
                HideCaption();
        }

        void Cycle()
        {
            var rig = PlayerCameraRig.Instance;
            if (!_seated || rig == null || !rig.HasTableViews)
                return;
            var view = rig.CycleTableView();
            ShowCaption(view);
            ShowNextIcon(view);
        }

        /// <summary>The icon for the view after <paramref name="current"/>: Seat and Overhead take turns.</summary>
        void ShowNextIcon(TableView current)
        {
            if (_icon == null)
                return;
            var next = current == TableView.Seat ? "icon--view-overhead" : "icon--view-seat";
            if (next == _iconClass)
                return;
            _icon.RemoveFromClassList("icon--view-overhead");
            _icon.RemoveFromClassList("icon--view-seat");
            _icon.AddToClassList(next);
            _iconClass = next;
        }

        void ShowCaption(TableView view)
        {
            _caption.text = view switch
            {
                TableView.Overhead => "Overhead view",
                _ => "Seat view",
            };
            _caption.AddToClassList(CaptionShowClass);
            _captionUntil = Time.time + CaptionSeconds;
        }

        void HideCaption()
        {
            _caption.RemoveFromClassList(CaptionShowClass);
            _captionUntil = 0f;
        }
    }
}
