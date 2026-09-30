using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// The cat pack's clips move the whole rig (its GeneralController root) forward: walk cycles drift ahead and
    /// sitting starts 0.3 units in front. CatMovement moves the cat, so after the Animator poses the rig each
    /// frame, pin the rig root back over the cat's origin, keeping its height and rotation.
    /// Unity's Generic root motion could do this too, but its baked root rotation didn't survive reimports of
    /// this rig (cats ended up pitched 90°), so this stays explicit.
    /// </summary>
    public class CatRigAnchor : MonoBehaviour
    {
        [SerializeField] Transform _rigRoot;

        void LateUpdate()
        {
            if (_rigRoot == null)
                return;
            var position = _rigRoot.localPosition;
            if (position.x != 0f || position.z != 0f)
                _rigRoot.localPosition = new Vector3(0f, position.y, 0f);
        }
    }
}
