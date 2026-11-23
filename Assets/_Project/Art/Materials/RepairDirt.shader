Shader "BORis/RepairDirt"
{
    Properties
    {
        _DirtMask ("Dirt Mask", 2D) = "white" {}
        [HideInInspector] _DirtBoundsMin ("Bounds Min", Vector) = (0,0,0,0)
        [HideInInspector] _DirtBoundsSize ("Bounds Size", Vector) = (1,1,1,0)
        [HideInInspector] _DirtProjection ("Projection", Float) = 0
        [HideInInspector] _DirtSurfaceDirectionWS ("Surface Direction WS", Vector) = (0,0,-1,0)
        [HideInInspector] _DirtNormalThreshold ("Normal Threshold", Float) = 0.72
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };
            TEXTURE2D(_DirtMask);
            SAMPLER(sampler_DirtMask);
            float4 _DirtBoundsMin;
            float4 _DirtBoundsSize;
            float _DirtProjection;
            float4x4 _DirtWorldToLocal;
            float4 _DirtSurfaceDirectionWS;
            float _DirtNormalThreshold;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Depth bias is handled by the pass-level Offset. Physically moving
                // split vertices along their normals opens cracks at mesh seams.
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = normalWS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half facing = dot(normalize(input.normalWS), normalize(_DirtSurfaceDirectionWS.xyz));
                clip(facing - _DirtNormalThreshold);

                float3 positionDirt = mul(_DirtWorldToLocal, float4(input.positionWS, 1)).xyz;
                float3 relative = (positionDirt - _DirtBoundsMin.xyz) / max(_DirtBoundsSize.xyz, .00001);
                float2 uv = _DirtProjection < .5
                    ? relative.xy
                    : (_DirtProjection < 1.5 ? relative.xz : relative.zy);
                clip(uv);
                clip(1 - uv);
                half4 dirt = SAMPLE_TEXTURE2D(_DirtMask, sampler_DirtMask, uv);
                dirt.a *= smoothstep(_DirtNormalThreshold, min(1.0, _DirtNormalThreshold + .23), facing);
                return dirt;
            }
            ENDHLSL
        }
    }
}
