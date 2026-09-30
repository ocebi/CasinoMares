using System.Collections.Generic;
using UnityEngine;

namespace CasinoMares.Blackjack
{
    /// <summary>A little stack of chips showing an amount, split into casino denominations (GDD §7.1).</summary>
    public class ChipStack : MonoBehaviour
    {
        public static readonly int[] Denominations = { 500, 100, 25, 10, 5 };

        static readonly Color[] DenominationColors =
        {
            new(0.52f, 0.28f, 0.72f), // 500 purple
            new(0.12f, 0.12f, 0.14f), // 100 black
            new(0.2f, 0.62f, 0.3f), // 25 green
            new(0.22f, 0.42f, 0.85f), // 10 blue
            new(0.93f, 0.92f, 0.88f), // 5 white
        };

        const int MaxChips = 14;
        const float ChipHeight = 0.012f;
        const float ChipDiameter = 0.075f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<Transform> _chips = new();
        Mesh _mesh;
        Material _material;
        MaterialPropertyBlock _block;
        int _amount = -1;
        Vector3 _targetPosition;
        bool _hasTarget;

        public int Amount => _amount;

        public static ChipStack Create(Transform parent, Mesh chipMesh, Material material)
        {
            var go = new GameObject("Chips");
            go.transform.SetParent(parent, false);
            var stack = go.AddComponent<ChipStack>();
            stack._mesh = chipMesh;
            stack._material = material;
            return stack;
        }

        public static Color ColorFor(int denomination)
        {
            var index = System.Array.IndexOf(Denominations, denomination);
            return index >= 0 ? DenominationColors[index] : Color.white;
        }

        /// <summary>Glide to a new spot (chips floating to the winner or the dealer).</summary>
        public void MoveTo(Vector3 position, bool instant)
        {
            _targetPosition = position;
            _hasTarget = true;
            if (instant)
                transform.position = position;
        }

        void Update()
        {
            if (_hasTarget)
                transform.position = Vector3.Lerp(transform.position, _targetPosition, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        public void SetAmount(int amount)
        {
            if (amount == _amount)
                return;
            _amount = amount;
            _block ??= new MaterialPropertyBlock();

            var chipIndex = 0;
            var remaining = Mathf.Max(0, amount);
            foreach (var denomination in Denominations)
            {
                while (remaining >= denomination && chipIndex < MaxChips)
                {
                    var chip = GetChip(chipIndex);
                    chip.gameObject.SetActive(true);
                    chip.localPosition = new Vector3(Random.Range(-0.003f, 0.003f), ChipHeight * (chipIndex + 0.5f), Random.Range(-0.003f, 0.003f));
                    _block.SetColor(BaseColorId, ColorFor(denomination));
                    chip.GetComponent<MeshRenderer>().SetPropertyBlock(_block);
                    remaining -= denomination;
                    chipIndex++;
                }
            }

            for (var i = chipIndex; i < _chips.Count; i++)
                _chips[i].gameObject.SetActive(false);
        }

        Transform GetChip(int index)
        {
            while (_chips.Count <= index)
            {
                var chip = new GameObject($"Chip_{_chips.Count}");
                chip.transform.SetParent(transform, false);
                // Unity's built-in cylinder is 2 units tall and 1 unit wide.
                chip.transform.localScale = new Vector3(ChipDiameter, ChipHeight / 2f, ChipDiameter);
                chip.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = chip.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _chips.Add(chip.transform);
            }

            return _chips[index];
        }
    }
}
