using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BORis
{
    sealed class TerminalProjection
    {
        const int PreviewLayer = 31;
        readonly Camera camera;
        readonly Transform stage;
        readonly Material material;
        readonly RenderTexture texture;
        readonly RawImage image;
        Transform model, source;

        public TerminalProjection(Transform parent, RawImage image)
        {
            this.image = image;
            stage = new GameObject("Terminal projection stage").transform;
            stage.SetParent(parent);
            stage.position = new Vector3(5000f, 1000f, 0f);
            texture = new RenderTexture(384, 384, 16) { name = "Terminal projection", antiAliasing = 2 };
            image.texture = texture;
            camera = new GameObject("Terminal projection camera").AddComponent<Camera>();
            camera.transform.SetParent(stage, false);
            camera.transform.localPosition = new Vector3(1.5f, 1.05f, -2.4f);
            camera.transform.LookAt(stage.position, Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.022f, .065f, .088f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.orthographicSize = .68f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 8f;
            camera.targetTexture = texture;
            camera.enabled = false;
            material = new Material(Resources.Load<Shader>("TerminalProjection"));
        }

        public void Show(Transform target)
        {
            camera.enabled = true;
            if (source == target) return;
            source = target;
            if (model != null)
            {
                model.gameObject.SetActive(false);
                Object.Destroy(model.gameObject);
            }
            if (target == null) return;

            model = new GameObject("Projection model").transform;
            model.SetParent(stage, false);
            Bounds bounds = default;
            bool hasMesh = false;
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled)
                    continue;
                Transform part = filter.transform;
                while (part != target && part.gameObject.activeSelf) part = part.parent;
                if (part != target) continue;

                var copy = new GameObject("Projection mesh") { layer = PreviewLayer };
                copy.transform.SetParent(model, false);
                Matrix4x4 pose = target.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                copy.transform.localPosition = pose.GetColumn(3);
                copy.transform.localRotation = pose.rotation;
                copy.transform.localScale = pose.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var surface = copy.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                System.Array.Fill(materials, material);
                surface.sharedMaterials = materials;
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;
                if (hasMesh) bounds.Encapsulate(surface.bounds);
                else bounds = surface.bounds;
                hasMesh = true;
            }

            if (hasMesh)
            {
                float scale = 1f / Mathf.Max(.001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
                model.localPosition = (stage.position - bounds.center) * scale;
                model.localScale = Vector3.one * scale;
                model.RotateAround(stage.position, Vector3.up, 140f);
            }
        }

        public void Hide()
        {
            if (camera != null) camera.enabled = false;
        }

        public void Dispose()
        {
            if (camera != null) camera.targetTexture = null;
            if (image != null) image.texture = null;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(material);
            if (stage != null) Object.Destroy(stage.gameObject);
        }
    }
}
