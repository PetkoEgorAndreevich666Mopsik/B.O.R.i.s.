using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class OutlineFeature : ScriptableRendererFeature
{
    [Serializable]
    public class OutlineSettings
    {
        public Material SilhouetteMaterial;
        public Material OutlineMaterial;
    }

    public OutlineSettings settings = new OutlineSettings();

    class RenderSilhouettePass : ScriptableRenderPass
    {
        private readonly Material silhouetteMaterial;
        private static readonly int SilhouetteTextureID = Shader.PropertyToID("_OutlineRenderTexture");
        private static readonly int OutlineObjectID = Shader.PropertyToID("_OutlineObjectId");

        public RenderSilhouettePass(Material mat)
        {
            silhouetteMaterial = mat;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private class PassData
        {
            public Material material;
            public OutlineTarget[] targets;
            public int cameraCullingMask;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (silhouetteMaterial == null) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            TextureDesc texDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.clear,
                name = "_OutlineRenderTexture"
            };
            TextureHandle silhouetteTexture = renderGraph.CreateTexture(texDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Render Silhouette", out var passData))
            {
                passData.material = silhouetteMaterial;
                passData.targets = OutlineTarget.ActiveTargets;
                passData.cameraCullingMask = cameraData.camera.cullingMask;

                builder.SetRenderAttachment(silhouetteTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetGlobalTextureAfterPass(silhouetteTexture, SilhouetteTextureID);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    for (int targetIndex = 0; targetIndex < data.targets.Length; targetIndex++)
                    {
                        OutlineTarget target = data.targets[targetIndex];
                        if (target == null || !target.isActiveAndEnabled)
                            continue;

                        context.cmd.SetGlobalColor(OutlineObjectID, EncodeObjectId(targetIndex + 1));

                        Renderer[] renderers = target.Renderers;
                        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                        {
                            Renderer targetRenderer = renderers[rendererIndex];
                            if (!target.Owns(targetRenderer) || !CanRender(targetRenderer, data.cameraCullingMask))
                                continue;

                            int subMeshCount = GetSubMeshCount(targetRenderer);
                            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                                context.cmd.DrawRenderer(targetRenderer, data.material, subMesh, 0);
                        }
                    }
                });
            }
        }

        private static bool CanRender(Renderer targetRenderer, int cameraCullingMask)
        {
            if (targetRenderer == null || !targetRenderer.enabled || !targetRenderer.gameObject.activeInHierarchy)
                return false;

            return (cameraCullingMask & (1 << targetRenderer.gameObject.layer)) != 0;
        }

        private static int GetSubMeshCount(Renderer targetRenderer)
        {
            if (targetRenderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
                return Mathf.Max(1, skinnedMeshRenderer.sharedMesh.subMeshCount);

            if (targetRenderer.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
                return Mathf.Max(1, meshFilter.sharedMesh.subMeshCount);

            return Mathf.Max(1, targetRenderer.sharedMaterials.Length);
        }

        private static Color EncodeObjectId(int id)
        {
            // Alpha marks occupied pixels. RGB distinguishes touching targets.
            return new Color(
                (id & 0xFF) / 255f,
                ((id >> 8) & 0xFF) / 255f,
                ((id >> 16) & 0xFF) / 255f,
                1f);
        }
    }

    class DrawOutlinePass : ScriptableRenderPass
    {
        private Material outlineMaterial;

        public DrawOutlinePass(Material mat)
        {
            outlineMaterial = mat;
            requiresIntermediateTexture = true; 
        }

        private class PassData
        {
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (outlineMaterial == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle activeColorTexture = resourceData.activeColorTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Outline", out var passData))
            {
                passData.material = outlineMaterial;

                builder.UseGlobalTexture(
                    Shader.PropertyToID("_OutlineRenderTexture"),
                    AccessFlags.Read
                );

                builder.SetRenderAttachment(activeColorTexture, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, new Vector2(1, 1), data.material, 0);
                });
            }
        }
    }

    private RenderSilhouettePass silhouettePass;
    private DrawOutlinePass outlinePass;

    public override void Create()
    {
        silhouettePass = new RenderSilhouettePass(settings.SilhouetteMaterial)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques
        };

        outlinePass = new DrawOutlinePass(settings.OutlineMaterial)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.SilhouetteMaterial != null && settings.OutlineMaterial != null)
        {
            renderer.EnqueuePass(silhouettePass);
            renderer.EnqueuePass(outlinePass);
        }
    }
}
