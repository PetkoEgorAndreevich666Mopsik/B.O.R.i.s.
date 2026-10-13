using UnityEngine;

namespace BORis
{
    public sealed class RepairConnector : MonoBehaviour
    {
        [SerializeField] Transform plug;
        [SerializeField] Vector3 disconnectOffset = new(.12f, 0f, 0f);
        [SerializeField] bool connected = true;
        Vector3 connectedPosition;

        public bool IsConnected => connected;

        void Awake()
        {
            connectedPosition = plug.localPosition;
            UpdatePose();
        }

        internal void SetConnected(bool value)
        {
            connected = value;
            UpdatePose();
        }

        void UpdatePose() => plug.localPosition = connectedPosition + (connected ? Vector3.zero : disconnectOffset);
    }
}
