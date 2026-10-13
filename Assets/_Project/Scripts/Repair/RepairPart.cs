using UnityEngine;

namespace BORis
{
    public enum RepairPartKind { Cover, Drive }

    public sealed class RepairPart : MonoBehaviour
    {
        [SerializeField] RepairPartKind kind;
        [SerializeField] string displayName;
        [SerializeField] bool faulty;
        [SerializeField] bool installed = true;
        [SerializeField] Transform installationAnchor;
        [SerializeField] Transform trayAnchor;
        Collider[] colliders;
        ScrewFastener[] screws;

        public RepairPartKind Kind => kind;
        public string DisplayName => displayName;
        public bool Faulty => faulty;
        public bool IsInstalled => installed;
        public Transform InstallationAnchor => installationAnchor;
        public Transform TrayAnchor => trayAnchor;
        public ScrewFastener[] Screws => screws;

        void Awake()
        {
            colliders = GetComponentsInChildren<Collider>();
            screws = GetComponentsInChildren<ScrewFastener>();
        }

        public void SetDragging(bool dragging)
        {
            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = !dragging;
        }

        internal void Place(bool inRobot)
        {
            installed = inRobot;
            RestorePose();
        }

        public void RestorePose()
        {
            Transform anchor = installed ? installationAnchor : trayAnchor;
            if (anchor != null) transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            SetDragging(false);
        }
    }
}
