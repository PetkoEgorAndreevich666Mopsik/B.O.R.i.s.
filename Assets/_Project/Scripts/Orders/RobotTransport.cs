using System.Collections.Generic;
using UnityEngine;

namespace BORis
{
    public sealed class RobotTransport : MonoBehaviour
    {
        [SerializeField] Workbench workbench;
        [SerializeField] Transform reception;
        [SerializeField] Collider deliveryZone;
        readonly List<Collider> heldColliders = new();
        Transform previousPlace;
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
            if (!isActiveAndEnabled || Robot == null || !Robot.isActiveAndEnabled || workbench.IsOccupied || IsCarrying)
                reason = "Робот недоступен для переноса";
            else if (!Robot.CanRunTest(out reason)) return false;
            if (reason.Length > 0) return false;
            previousPlace = workbench.RobotOnTable ? null : IsAtDelivery ? deliveryZone.transform : reception;
            workbench.PositionRobot(previousPlace != null ? previousPlace : reception);
            IsCarrying = true;
            IsAtDelivery = false;
            heldColliders.Clear();
            foreach (Collider collider in Robot.GetComponentsInChildren<Collider>())
                if (collider.enabled) { heldColliders.Add(collider); collider.enabled = false; }
            return true;
        }

        public void Follow(Camera view)
        {
            if (!IsCarrying || Robot == null) return;
            Robot.transform.rotation = Quaternion.Euler(0f, view.transform.eulerAngles.y + 180f, 0f);
            Bounds bounds = Robot.InspectionPivot.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer renderer in Robot.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            Robot.transform.position += view.transform.position + view.transform.forward * 1.25f - view.transform.up * .35f - bounds.center;
        }

        public bool TryDrop(Ray ray, float reach)
        {
            if (!IsCarrying || Robot == null) { CancelCarry(); return false; }
            bool hit = Physics.Raycast(ray, out RaycastHit target, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool onTable = hit && target.collider.GetComponentInParent<Workbench>() == workbench;
            bool onDelivery = deliveryZone != null && deliveryZone.Raycast(ray, out RaycastHit zoneHit, reach)
                && (!hit || target.collider == deliveryZone || zoneHit.distance <= target.distance + .05f);
            RestoreColliders();
            IsCarrying = false;
            if (onTable || onDelivery)
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
            if (!IsCarrying) return;
            RestoreColliders();
            IsCarrying = false;
            if (workbench != null) workbench.PositionRobot(previousPlace);
            IsAtDelivery = deliveryZone != null && previousPlace == deliveryZone.transform;
        }

        void OnDisable() => CancelCarry();
    }
}
