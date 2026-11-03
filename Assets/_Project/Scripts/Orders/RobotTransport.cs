using System.Collections.Generic;
using UnityEngine;

namespace BORis
{
    public sealed class RobotTransport : MonoBehaviour
    {
        [SerializeField] Workbench workbench;
        [SerializeField] Transform reception;
        [SerializeField] Collider deliveryZone;
        [SerializeField] GameObject tablePlacementHint;
        [SerializeField] GameObject windowPlacementHint;
        readonly List<Collider> heldColliders = new();
        Transform previousPlace;
        Vector3 carryVelocity;
        public bool IsCarrying { get; private set; }
        public bool IsAtDelivery { get; private set; }
        public Transform Reception => reception;
        public Collider DeliveryZone => deliveryZone;
        public RobotRepair Robot => workbench != null ? workbench.Robot : null;

        public void ReceiveRobot()
        {
            CancelCarry();
            IsAtDelivery = false;
            previousPlace = reception;
            workbench.PositionRobot(reception);
        }

        public bool TryPickUp(out string reason)
        {
            reason = "";
            var session = GetComponent<WorkshopSession>();
            if (session != null && session.Window != null && session.Window.Phase == ServiceWindowPhase.Departing)
            {
                reason = "Дождитесь окончания отправки";
                return false;
            }
            if (!isActiveAndEnabled || Robot == null || !Robot.isActiveAndEnabled || workbench.IsOccupied || IsCarrying)
                reason = "Робот недоступен для переноса";
            else if (!Robot.CanRunTest(out reason)) return false;
            if (reason.Length > 0) return false;
            previousPlace = workbench.RobotOnTable ? null : IsAtDelivery ? deliveryZone.transform : reception;
            workbench.PositionRobot(previousPlace != null ? previousPlace : reception);
            IsCarrying = true;
            carryVelocity = Vector3.zero;
            IsAtDelivery = false;
            heldColliders.Clear();
            foreach (Collider collider in Robot.GetComponentsInChildren<Collider>())
                if (collider.enabled) { heldColliders.Add(collider); collider.enabled = false; }
            return true;
        }

        public void Follow(Camera view)
        {
            if (!IsCarrying || Robot == null) return;
            Quaternion facing = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f);
            Robot.transform.rotation = Quaternion.Slerp(Robot.transform.rotation, facing, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            workbench.SynchronizeRobotPhysics();
            Bounds bounds = Robot.InspectionPivot.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer renderer in Robot.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            Vector3 target = view.transform.position + facing * new Vector3(.28f, -.55f, 1.2f);
            Vector3 direction = target - view.transform.position;
            var player = view.GetComponentInParent<PlayerController>();
            int mask = player != null ? ~(1 << player.gameObject.layer) : Physics.DefaultRaycastLayers;
            if (Physics.SphereCast(view.transform.position, .28f, direction.normalized, out RaycastHit obstacle,
                direction.magnitude, mask, QueryTriggerInteraction.Ignore))
                target = view.transform.position + direction.normalized * Mathf.Max(.35f, obstacle.distance - .05f);
            Vector3 position = Robot.transform.position + target - bounds.center;
            Robot.transform.position = Vector3.SmoothDamp(Robot.transform.position, position, ref carryVelocity, .09f,
                Mathf.Infinity, Time.unscaledDeltaTime);
            workbench.SynchronizeRobotPhysics();
        }

        public Transform PreviewDrop(Ray ray, float reach)
        {
            Transform destination = FindDestination(ray, reach, out bool onTable);
            if (tablePlacementHint != null) tablePlacementHint.SetActive(IsCarrying && destination != null && onTable);
            if (windowPlacementHint != null) windowPlacementHint.SetActive(IsCarrying && destination != null && !onTable);
            return destination;
        }

        Transform FindDestination(Ray ray, float reach, out bool onTable)
        {
            bool hit = Physics.Raycast(ray, out RaycastHit target, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            onTable = hit && target.collider.GetComponentInParent<Workbench>() == workbench;
            bool onDelivery = deliveryZone != null && deliveryZone.Raycast(ray, out RaycastHit zoneHit, reach)
                && (!hit || target.collider == deliveryZone || zoneHit.distance <= target.distance + .05f);
            return onTable ? workbench.transform : onDelivery ? deliveryZone.transform : null;
        }

        public bool TryDrop(Ray ray, float reach)
        {
            if (!IsCarrying || Robot == null) { CancelCarry(); return false; }
            Transform destination = FindDestination(ray, reach, out bool onTable);
            RestoreColliders();
            IsCarrying = false;
            ClearPlacementHints();
            if (destination != null)
            {
                IsAtDelivery = !onTable;
                previousPlace = onTable ? null : deliveryZone.transform;
                workbench.PositionRobot(previousPlace);
                return true;
            }
            workbench.PositionRobot(previousPlace);
            IsAtDelivery = deliveryZone != null && previousPlace == deliveryZone.transform;
            return false;
        }

        void RestoreColliders()
        {
            foreach (Collider collider in heldColliders) if (collider != null) collider.enabled = true;
            heldColliders.Clear();
        }

        public void CancelCarry()
        {
            ClearPlacementHints();
            if (!IsCarrying) return;
            RestoreColliders();
            IsCarrying = false;
            if (workbench != null) workbench.PositionRobot(previousPlace);
            IsAtDelivery = deliveryZone != null && previousPlace == deliveryZone.transform;
        }

        void ClearPlacementHints()
        {
            if (tablePlacementHint != null) tablePlacementHint.SetActive(false);
            if (windowPlacementHint != null) windowPlacementHint.SetActive(false);
        }

        void OnDisable() => CancelCarry();
    }
}
