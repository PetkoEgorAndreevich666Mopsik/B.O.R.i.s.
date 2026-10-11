using UnityEngine;

namespace BORis
{
    public sealed class Workbench : MonoBehaviour
    {
        [SerializeField] Transform cameraAnchor;
        Transform view;
        Transform previousParent;
        Vector3 previousPosition;
        Quaternion previousRotation;

        public bool IsOccupied => view != null;

        public bool Enter(Camera camera)
        {
            if (IsOccupied || cameraAnchor == null || camera == null) return false;
            view = camera.transform;
            previousParent = view.parent;
            previousPosition = view.localPosition;
            previousRotation = view.localRotation;
            view.SetParent(cameraAnchor, false);
            view.localPosition = Vector3.zero;
            view.localRotation = Quaternion.identity;
            return true;
        }

        public void Exit()
        {
            if (view == null) return;
            view.SetParent(previousParent, false);
            view.localPosition = previousPosition;
            view.localRotation = previousRotation;
            view = null;
        }

        void OnDisable() => Exit();
    }
}
