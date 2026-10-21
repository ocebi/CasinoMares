using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>Gift chips to another cat (GDD §7.3). Chips already in a bet can't be gifted.</summary>
    public class GiftDialog : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        static GiftDialog _instance;

        public static bool IsOpen => _instance != null && _instance._target != null;

        VisualElement _root;
        Label _title;
        IntegerField _amount;
        Label _status;
        PlayerCat _target;

        public static void Open(PlayerCat target)
        {
            if (_instance != null)
                _instance.Show(target);
        }

        void OnEnable()
        {
            _instance = this;
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("gift-root");
            _title = ui.Q<Label>("gift-title");
            _amount = ui.Q<IntegerField>("gift-amount");
            _status = ui.Q<Label>("gift-status");
            WebTextInput.Attach("gift", _amount, cancelled: Hide);

            foreach (var preset in new[] { 50, 100, 250, 500 })
                ui.Q<Button>($"gift-{preset}").clicked += () => _amount.value = preset;
            ui.Q<Button>("gift-send").clicked += Send;
            ui.Q<Button>("gift-cancel").clicked += Hide;
        }

        void OnDisable()
        {
            if (_target != null)
                Hide();
            if (_instance == this)
                _instance = null;
        }

        void Update()
        {
            if (_target == null)
                return;
            if (GameInput.PausePressed() || _target.Object == null || !_target.Object.IsValid)
                Hide();
        }

        void Show(PlayerCat target)
        {
            if (_target != null)
                return;
            _target = target;
            _title.text = $"Gift chips to {target.DisplayName}";
            _status.text = $"You have {ProfileStore.Current.Balance:N0} chips.";
            _status.RemoveFromClassList("status--error");
            _amount.value = Mathf.Min(100, ProfileStore.Current.Balance);
            _root.RemoveFromClassList("hidden");
            GameInput.PushUIBlock();
        }

        void Hide()
        {
            if (_target == null)
                return;
            _target = null;
            _root.AddToClassList("hidden");
            GameInput.PopUIBlock();
        }

        void Send()
        {
            WebTextInput.Pull(_amount);
            var amount = _amount.value;
            var balance = ProfileStore.Current.Balance;
            string error = null;
            if (amount <= 0)
                error = "Pick an amount above zero.";
            else if (amount > balance)
                error = "You don't have that many chips."; // Chips in play are already out of the balance.

            if (error != null)
            {
                _status.text = error;
                _status.AddToClassList("status--error");
                return;
            }

            var target = _target;
            var me = PlayerCat.Local;
            ProfileStore.SetBalance(balance - amount);
            target.Social.SendGift(amount, me != null ? me.DisplayName.ToString() : "A cat");
            me?.Social.Announce($"{me.DisplayName} gifted {amount:N0} chips to {target.DisplayName}.");
            Toasts.Show($"You gifted {amount:N0} chips to {target.DisplayName}.");
            Hide();
        }
    }
}
