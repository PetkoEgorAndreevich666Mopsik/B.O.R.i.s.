using UnityEngine;

namespace BORis
{
    [CreateAssetMenu(menuName = "BORis/Order")]
    public sealed class OrderDefinition : ScriptableObject
    {
        [SerializeField] string orderId;
        [SerializeField] string clientName;
        [SerializeField, TextArea] string symptom;
        [SerializeField, Min(0)] int reward;
        [SerializeField] RobotRepair robotPrefab;
        [SerializeField] bool requiresCleaning;
        [SerializeField] bool requiresSolderingStation;

        public string OrderId => orderId;
        public string ClientName => clientName;
        public string Symptom => symptom;
        public int Reward => reward;
        public RobotRepair RobotPrefab => robotPrefab;
        public bool RequiresCleaning => requiresCleaning;
        public bool RequiresSolderingStation => requiresSolderingStation;

        public bool IsValid => !string.IsNullOrWhiteSpace(orderId)
            && !string.IsNullOrWhiteSpace(clientName) && !string.IsNullOrWhiteSpace(symptom)
            && reward >= 0 && robotPrefab != null && robotPrefab.gameObject.activeSelf;
    }
}
