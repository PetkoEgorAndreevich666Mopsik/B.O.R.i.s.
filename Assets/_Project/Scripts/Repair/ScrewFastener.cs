using UnityEngine;

namespace BORis
{
    public sealed class ScrewFastener : MonoBehaviour
    {
        [SerializeField, Min(.1f)] float turns = 2f;
        [SerializeField, Min(0f)] float travel = .06f;
        [SerializeField, Range(0f, 1f)] float tightness = 1f;
        [SerializeField] GameObject looseIndicator;
        [SerializeField] GameObject tightIndicator;
        [SerializeField] Material ghostMaterial;
        Vector3 seatedPosition;
        Quaternion seatedRotation;
        MeshRenderer[] renderers;
        Material[][] metalMaterials;
        MaterialPropertyBlock ghostProperties;
        bool hovered;

        public float Tightness => tightness;
        public bool IsRemoved { get; private set; }
        public bool IsLoose => IsRemoved;
        public bool IsTight => !IsRemoved && tightness == 1f;

        void Awake()
        {
            seatedPosition = transform.localPosition;
            seatedRotation = transform.localRotation;
            renderers = System.Array.FindAll(GetComponentsInChildren<MeshRenderer>(true), r =>
                (looseIndicator == null || !r.transform.IsChildOf(looseIndicator.transform))
                && (tightIndicator == null || !r.transform.IsChildOf(tightIndicator.transform)));
            metalMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) metalMaterials[i] = renderers[i].sharedMaterials;
            IsRemoved = tightness == 0f;
            UpdatePose();
        }

        internal float Turn(float clockwiseDegrees)
        {
            if (IsRemoved || !float.IsFinite(clockwiseDegrees)) return 0f;
            float previous = tightness;
            tightness = Mathf.Clamp01(tightness + clockwiseDegrees / (turns * 360f));
            if (clockwiseDegrees < 0f && tightness < .03f) tightness = 0f;
            if (clockwiseDegrees > 0f && tightness > .97f) tightness = 1f;
            IsRemoved = tightness == 0f;
            UpdatePose();
            return (tightness - previous) * turns * 360f;
        }

        internal void Insert()
        {
            IsRemoved = false;
            tightness = .08f;
            hovered = false;
            UpdatePose();
        }

        public void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            UpdateGhostColor();
        }

        public GameObject CreateLooseVisual(Transform parent)
        {
            var visual = new GameObject("LooseScrew");
            visual.transform.SetParent(parent, false);
            for (int i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i].GetComponent<MeshFilter>();
                if (source == null || source.sharedMesh == null) continue;
                var mesh = new GameObject(source.name);
                mesh.transform.SetParent(visual.transform, false);
                mesh.transform.localPosition = transform.InverseTransformPoint(source.transform.position);
                mesh.transform.localRotation = Quaternion.Inverse(transform.rotation) * source.transform.rotation;
                Vector3 scale = source.transform.lossyScale;
                mesh.transform.localScale = scale;
                mesh.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                mesh.AddComponent<MeshRenderer>().sharedMaterials = metalMaterials[i];
            }
            return visual;
        }

        void UpdatePose()
        {
            float loose = IsRemoved ? 0f : 1f - tightness;
            transform.localPosition = seatedPosition + seatedRotation * Vector3.up * (loose * travel);
            transform.localRotation = seatedRotation * Quaternion.AngleAxis(-loose * turns * 360f, Vector3.up);
            if (looseIndicator != null) looseIndicator.SetActive(false);
            if (tightIndicator != null) tightIndicator.SetActive(IsTight);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsRemoved && ghostMaterial != null)
                {
                    var materials = new Material[metalMaterials[i].Length];
                    for (int j = 0; j < materials.Length; j++) materials[j] = ghostMaterial;
                    renderers[i].sharedMaterials = materials;
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                else
                {
                    renderers[i].sharedMaterials = metalMaterials[i];
                    renderers[i].SetPropertyBlock(null);
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
            UpdateGhostColor();
        }

        void UpdateGhostColor()
        {
            if (!IsRemoved || ghostMaterial == null) return;
            ghostProperties ??= new MaterialPropertyBlock();
            ghostProperties.SetColor("_BaseColor", new Color(.65f, .86f, .96f, hovered ? .85f : .4f));
            foreach (MeshRenderer renderer in renderers) renderer.SetPropertyBlock(ghostProperties);
        }
    }
}
