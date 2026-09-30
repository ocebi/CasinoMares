using System.Collections.Generic;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Screen-space nameplates that track each remote cat's head and fade out with distance (GDD §8).
    /// </summary>
    public class NameplateLayer : MonoBehaviour
    {
        [SerializeField] UIDocument _document;
        [SerializeField] float _fullOpacityDistance = 6f;
        [SerializeField] float _maxDistance = 12f;

        VisualElement _layer;
        readonly Dictionary<PlayerCat, Label> _plates = new();
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
                    plate = new Label { pickingMode = PickingMode.Ignore };
                    plate.AddToClassList("nameplate");
                    _layer.Add(plate);
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
                _plates[cat].RemoveFromHierarchy();
                _plates.Remove(cat);
            }
        }

        void UpdatePlate(PlayerCat cat, Label plate, Camera cam)
        {
            var worldPosition = cat.NameplateAnchor.position;
            var toCat = worldPosition - cam.transform.position;
            var distance = toCat.magnitude;
            var inFront = Vector3.Dot(toCat, cam.transform.forward) > 0f;

            if (!inFront || distance > _maxDistance)
            {
                plate.style.display = DisplayStyle.None;
                return;
            }

            var panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, worldPosition, cam);
            plate.style.display = DisplayStyle.Flex;
            plate.style.left = panelPosition.x;
            plate.style.top = panelPosition.y;
            plate.style.opacity = 1f - Mathf.InverseLerp(_fullOpacityDistance, _maxDistance, distance);
            plate.style.borderBottomColor = cat.Swatch;
            plate.text = cat.DisplayName.ToString();
        }
    }
}
