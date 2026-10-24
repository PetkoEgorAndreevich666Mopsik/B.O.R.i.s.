using UnityEngine;

namespace BORis
{
    public abstract class RepairTool : MonoBehaviour
    {
        Vector3 homePosition;
        Quaternion homeRotation;
        Collider[] colliders;
        Camera selectedView;
        Workbench workbench;

        protected float Reach => 3f + (workbench != null ? workbench.CameraOffset : 0f);

        public bool IsSelected => selectedView != null;
        public abstract bool IsActive { get; }
        public abstract void Drag(Vector2 pointer);
        public abstract void End();

        protected virtual void Awake()
        {
            workbench = GetComponentInParent<Workbench>();
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            colliders = GetComponentsInChildren<Collider>();
        }

        public void Select(Camera camera)
        {
            End();
            selectedView = camera;
            RestorePose();
        }

        public void FollowPointer(Vector2 pointer)
        {
            if (selectedView == null || IsActive) return;
            Vector3 position = selectedView.ScreenToWorldPoint(new Vector3(pointer.x + 12f, pointer.y - 12f, .65f));
            transform.SetPositionAndRotation(position, selectedView.transform.rotation * Quaternion.Euler(0f, 0f, -35f));
        }

        protected void SetColliders(bool enabled)
        {
            if (colliders == null) return;
            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = enabled;
        }

        protected void RestorePose()
        {
            SetColliders(!IsSelected);
            if (!IsSelected)
            {
                transform.localPosition = homePosition;
                transform.localRotation = homeRotation;
            }
        }
    }
}
