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
        [SerializeField] AudioClip releaseSound;
        Collider[] colliders;
        ScrewFastener[] screws;

        public RepairPartKind Kind => kind;
        public string DisplayName => displayName;
        public bool Faulty => faulty;
        public bool IsInstalled => installed;
        public Transform InstallationAnchor => installationAnchor;
        public Transform TrayAnchor => trayAnchor;
        public ScrewFastener[] Screws => screws;
        public bool IsReleased { get; private set; }

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
            IsReleased = false;
            RestorePose();
        }

        internal void RefreshRelease()
        {
            if (kind != RepairPartKind.Cover || !installed) return;
            bool released = screws.Length > 0;
            foreach (ScrewFastener screw in screws) released &= screw.IsLoose;
            if (released == IsReleased) return;
            IsReleased = released;
            RestorePose();
            if (released && releaseSound != null)
            {
                var audio = GetComponent<AudioSource>();
                if (audio != null) audio.PlayOneShot(releaseSound, .7f);
            }
        }

        public void RestorePose()
        {
            Transform anchor = installed ? installationAnchor : trayAnchor;
            if (anchor != null) transform.SetPositionAndRotation(
                anchor.position + (installed && IsReleased ? anchor.rotation * Vector3.back * .035f : Vector3.zero), anchor.rotation);
            SetDragging(false);
        }
    }
}
