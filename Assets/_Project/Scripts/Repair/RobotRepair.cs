using UnityEngine;

namespace BORis
{
    public sealed class RobotRepair : MonoBehaviour
    {
        public float Turn(ScrewFastener screw, float clockwiseDegrees)
        {
            if (screw == null || !screw.isActiveAndEnabled || screw.GetComponentInParent<RobotRepair>() != this)
                return 0f;
            return screw.Turn(clockwiseDegrees);
        }
    }
}
