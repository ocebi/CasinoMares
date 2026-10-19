using System.Collections.Generic;
using CasinoMares.Player;
using Fusion;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Screen-space nameplates that track each remote cat's head and fade out with distance (GDD §8). A cat's name
    /// is a networked string whose <c>ToString</c> builds a new string, so each plate keeps the string it last made
    /// and only makes another when the name changes.
    /// </summary>
    public class NameplateLayer : MonoBehaviour
    {
        class Plate
        {
            public Label Label;
            public NetworkString<_16> Name;
            public string Text;
        }

        [SerializeField] UIDocument _document;
        [SerializeField] float _fullOpacityDistance = 6f;
        [SerializeField] float _maxDistance = 12f;

        VisualElement _layer;
        readonly Dictionary<PlayerCat, Plate> _plates = new();
        readonly List<PlayerCat> _stale = new();

        void OnEnable()
        {
            _layer = _document.rootVisualElement.Q("nameplate-layer");
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _layer.panel == null)
                return;

            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.IsLocal)
                    continue;

                if (!_plates.TryGetValue(cat, out var plate))
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("nameplate");
                    _layer.Add(label);
                    plate = new Plate { Label = label };
                    _plates.Add(cat, plate);
                }

                UpdatePlate(cat, plate, cam);
            }

            _stale.Clear();
            foreach (var pair in _plates)
            {
                if (pair.Key == null || !PlayerCat.All.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }

            foreach (var cat in _stale)
            {
                _plates[cat].Label.RemoveFromHierarchy();
                _plates.Remove(cat);
            }
        }

        void UpdatePlate(PlayerCat cat, Plate plate, Camera cam)
        {
            var label = plate.Label;
            var worldPosition = cat.NameplateAnchor.position;
            var toCat = worldPosition - cam.transform.position;
            var distance = toCat.magnitude;
            var inFront = Vector3.Dot(toCat, cam.transform.forward) > 0f;

            if (!inFront || distance > _maxDistance)
            {
                label.style.display = DisplayStyle.None;
                return;
            }

            var panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, worldPosition, cam);
            label.style.display = DisplayStyle.Flex;
            label.style.left = panelPosition.x;
            label.style.top = panelPosition.y;
            label.style.opacity = 1f - Mathf.InverseLerp(_fullOpacityDistance, _maxDistance, distance);
            label.style.borderBottomColor = cat.Swatch;

            var name = cat.DisplayName;
            if (plate.Text == null || !plate.Name.Equals(name))
            {
                plate.Name = name;
                plate.Text = name.ToString();
                label.text = plate.Text;
            }
        }
    }
}
