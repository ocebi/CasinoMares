using CasinoMares.Audio;
using UnityEngine;

namespace CasinoMares.Blackjack
{
    /// <summary>
    /// One floating card (GDD §5.5): glides along an arc from where it is to its slot and flips in mid-air.
    /// Purely local; <see cref="BlackjackTableView"/> tells it where to go.
    /// </summary>
    public class CardVisual : MonoBehaviour
    {
        const float FlightDuration = 0.38f;
        const float ArcHeight = 0.28f;

        MeshFilter _face;
        MeshFilter _back;
        Vector3 _fromPosition;
        Quaternion _fromRotation;
        Vector3 _toPosition;
        Quaternion _toRotation;
        float _t = 1f;
        bool _releaseWhenLanded;

        public byte Card { get; private set; } = 255;
        public bool FaceUp { get; private set; }
        public bool IsFlying => _t < 1f;

        public static CardVisual Create(Transform parent, Material material)
        {
            var root = new GameObject("Card");
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<CardVisual>();
            visual._face = CreateSide(root.transform, "Face", material);
            visual._back = CreateSide(root.transform, "Back", material);
            visual._back.sharedMesh = CardFaces.BackMesh;
            return visual;
        }

        static MeshFilter CreateSide(Transform parent, string name, Material material)
        {
            var side = new GameObject(name);
            side.transform.SetParent(parent, false);
            var renderer = side.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return side.AddComponent<MeshFilter>();
        }

        public void SetCard(byte card)
        {
            if (Card == card)
                return;
            Card = card;
            _face.sharedMesh = CardFaces.FaceMesh(card);
        }

        /// <summary>Fly to a slot. Face-down cards are flipped over around their long axis.</summary>
        public void FlyTo(Vector3 position, Quaternion slotRotation, bool faceUp, bool instant = false)
        {
            FaceUp = faceUp;
            _releaseWhenLanded = false;
            _fromPosition = transform.position;
            _fromRotation = transform.rotation;
            _toPosition = position;
            _toRotation = faceUp ? slotRotation : slotRotation * Quaternion.Euler(0f, 0f, 180f);
            _t = instant ? 1f : 0f;
            if (instant)
                transform.SetPositionAndRotation(_toPosition, _toRotation);
            else
                Sfx.PlayAt(SoundId.CardSlide, _fromPosition, 0.55f);
        }

        /// <summary>Update the slot without restarting the flight (layout shifts when cards are added).</summary>
        public void Retarget(Vector3 position, Quaternion slotRotation, bool faceUp)
        {
            var rotation = faceUp ? slotRotation : slotRotation * Quaternion.Euler(0f, 0f, 180f);
            if ((position - _toPosition).sqrMagnitude < 0.00001f && faceUp == FaceUp && Quaternion.Angle(rotation, _toRotation) < 0.5f)
                return;

            if (IsFlying)
            {
                _toPosition = position;
                _toRotation = rotation;
                FaceUp = faceUp;
                return;
            }

            FlyTo(position, slotRotation, faceUp);
        }

        /// <summary>Fly away (to the discard tray) and destroy on landing.</summary>
        public void FlyAwayAndRelease(Vector3 position, Quaternion rotation)
        {
            FlyTo(position, rotation, false);
            _releaseWhenLanded = true;
        }

        void Update()
        {
            if (_t >= 1f)
                return;

            _t = Mathf.Min(1f, _t + Time.deltaTime / FlightDuration);
            var eased = 1f - (1f - _t) * (1f - _t);
            var position = Vector3.Lerp(_fromPosition, _toPosition, eased) + Vector3.up * (Mathf.Sin(_t * Mathf.PI) * ArcHeight);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(_fromRotation, _toRotation, eased));

            if (_t < 1f)
                return;
            if (_releaseWhenLanded)
                Destroy(gameObject);
            else
                Sfx.PlayAt(SoundId.CardPlace, _toPosition, 0.35f);
        }
    }
}
