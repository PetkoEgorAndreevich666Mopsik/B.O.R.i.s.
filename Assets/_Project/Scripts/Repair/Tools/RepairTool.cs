using UnityEngine;

namespace BORis
{
    public abstract class RepairTool : MonoBehaviour
    {
        public abstract bool IsActive { get; }
        public abstract void Drag(Vector2 pointer);
        public abstract void End();
    }
}
