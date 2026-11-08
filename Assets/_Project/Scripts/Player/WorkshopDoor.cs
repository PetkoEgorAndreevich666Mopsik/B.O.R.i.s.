using UnityEngine;

namespace BORis
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkshopDoor : MonoBehaviour
    {
        [SerializeField] Vector3 openOffset;
        [SerializeField] float openAngle;
        const float TravelTime = .48f;

        Vector3 closedPosition;
        Quaternion closedRotation;
        Bounds travelBounds;
        CharacterController operatorBody;
        bool targetOpen;
        float travel;

        public float OpenFraction => Mathf.SmoothStep(0f, 1f, travel);
        public string Hint => targetOpen ? "E — закрыть дверь" : "E — открыть дверь";

        void Awake()
        {
            closedPosition = transform.localPosition;
            closedRotation = transform.localRotation;
            var panel = GetComponent<BoxCollider>();
            travelBounds = panel.bounds;
            if (openAngle == 0f)
            {
                Vector3 worldOffset = transform.parent != null ? transform.parent.TransformVector(openOffset) : openOffset;
                travelBounds.Encapsulate(new Bounds(travelBounds.center + worldOffset, travelBounds.size));
                return;
            }

            // Include the middle of the swing, not just the end positions.
            Vector3 min = panel.center - panel.size * .5f;
            Vector3 max = panel.center + panel.size * .5f;
            for (int step = 1; step <= 8; step++)
            {
                var rotation = Quaternion.Euler(0f, openAngle * step / 8f, 0f);
                foreach (float x in new[] { min.x, max.x })
                foreach (float y in new[] { min.y, max.y })
                foreach (float z in new[] { min.z, max.z })
                    travelBounds.Encapsulate(transform.TransformPoint(rotation * new Vector3(x, y, z)));
            }
            travelBounds.Expand(.02f);
        }

        public bool TryToggle(CharacterController body)
        {
            if ((targetOpen || openAngle != 0f) && travelBounds.Intersects(body.bounds)) return false;

            operatorBody = body;
            targetOpen = !targetOpen;
            return true;
        }

        void Update()
        {
            if (targetOpen ? travel >= 1f : travel <= 0f) return;
            if (operatorBody != null && travelBounds.Intersects(operatorBody.bounds))
            {
                if (openAngle != 0f) return;
                if (!targetOpen) targetOpen = true;
            }

            travel = Mathf.MoveTowards(travel, targetOpen ? 1f : 0f, Time.deltaTime / TravelTime);
            float fraction = OpenFraction;
            transform.SetLocalPositionAndRotation(closedPosition + openOffset * fraction,
                closedRotation * Quaternion.Euler(0f, openAngle * fraction, 0f));
        }
    }
}
