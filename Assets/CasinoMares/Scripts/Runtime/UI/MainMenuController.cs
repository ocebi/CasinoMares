using System.Collections.Generic;
using CasinoMares.Core;
using CasinoMares.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>Name + fur color + Play (GDD §8). Shown over the casino until the session is joined.</summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] UIDocument _document;
        [SerializeField] CatFurPalette _palette;

        VisualElement _root;
        TextField _nameField;
        Label _furName;
        Label _status;
        Button _playButton;
        readonly List<Button> _swatches = new();
        int _furIndex;
        bool _blockingInput;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("menu-root");
            _nameField = ui.Q<TextField>("name-field");
            _furName = ui.Q<Label>("fur-name");
            _status = ui.Q<Label>("status-label");
            _playButton = ui.Q<Button>("play-button");

            var profile = ProfileStore.Current;
            _nameField.value = profile.DisplayName;
            _nameField.maxLength = ProfileStore.MaxNameLength;
            WebTextInput.Attach("name", _nameField);
            BuildSwatches(ui.Q("fur-swatches"));
            SelectFur(_palette.Clamp(profile.FurIndex));

            _playButton.clicked += OnPlayClicked;
            var quit = ui.Q<Button>("quit-button");
            quit.clicked += Application.Quit;
            quit.EnableInClassList("hidden", !Platform.CanQuit);

            ConnectionManager.Instance.StateChanged += OnConnectionStateChanged;
            OnConnectionStateChanged(ConnectionManager.Instance.State);
        }

        void OnDisable()
        {
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.StateChanged -= OnConnectionStateChanged;
            SetInputBlocked(false);
        }

        void BuildSwatches(VisualElement container)
        {
            container.Clear();
            _swatches.Clear();
            for (var i = 0; i < _palette.Count; i++)
            {
                var index = i;
                var option = _palette.Get(i);
                var swatch = new Button(() => SelectFur(index)) { tooltip = option.Name };
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = option.Swatch;
                container.Add(swatch);
                _swatches.Add(swatch);
            }
        }

        void SelectFur(int index)
        {
            _furIndex = index;
            for (var i = 0; i < _swatches.Count; i++)
                _swatches[i].EnableInClassList("swatch--selected", i == index);
            _furName.text = _palette.Get(index).Name;
        }

        void OnPlayClicked()
        {
            WebTextInput.Pull(_nameField);
            ProfileStore.SetIdentity(_nameField.value, _furIndex);
            _nameField.value = ProfileStore.Current.DisplayName;
            ConnectionManager.Instance.Connect();
        }

        void OnConnectionStateChanged(ConnectionState state)
        {
            var inMenu = state != ConnectionState.Connected;
            _root.EnableInClassList("hidden", !inMenu);
            SetInputBlocked(inMenu);

            _playButton.SetEnabled(state is ConnectionState.Offline or ConnectionState.Failed);
            _status.RemoveFromClassList("status--error");
            switch (state)
            {
                case ConnectionState.Connecting:
                    _status.text = "Connecting to the casino...";
                    break;
                case ConnectionState.Offline:
                case ConnectionState.Failed:
                    _status.text = ConnectionManager.LastError;
                    _status.EnableInClassList("status--error", !string.IsNullOrEmpty(ConnectionManager.LastError));
                    break;
                default:
                    _status.text = "";
                    break;
            }
        }

        void SetInputBlocked(bool blocked)
        {
            if (blocked == _blockingInput)
                return;

            _blockingInput = blocked;
            if (blocked)
                GameInput.PushUIBlock();
            else
                GameInput.PopUIBlock();
        }
    }
}
