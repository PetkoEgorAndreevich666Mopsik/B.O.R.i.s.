Shader "Custom/HardOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0, 0, 1)
        _Thickness ("Outline Thickness", Range(1, 10)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert 
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_OutlineRenderTexture);
            SAMPLER(sampler_OutlineRenderTexture);

            float4 _OutlineColor;
            float _Thickness;

            half4 Frag(Varyings input) : SV_Target {
                float2 uv = input.texcoord;

                half4 center = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv);

                float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                float referenceHeight = 1080.0;
                float scaleFactor = _ScreenParams.y / referenceHeight;
                float scaledThickness = _Thickness * scaleFactor;
                float2 offset = texelSize * scaledThickness;

                half4 up = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(0, offset.y));
                half4 down = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(0, -offset.y));
                half4 left = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(-offset.x, 0));
                half4 right = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(offset.x, 0));

                half4 topLeft = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(-offset.x, offset.y));
                half4 topRight = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(offset.x, offset.y));
                half4 bottomLeft = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(-offset.x, -offset.y));
                half4 bottomRight = SAMPLE_TEXTURE2D_X(_OutlineRenderTexture, sampler_OutlineRenderTexture, uv + float2(offset.x, -offset.y));

                half centerOccupied = step(0.5h, center.a);
                half centerId = dot(center.rgb, half3(1.0h, 256.0h, 65536.0h));
                half4 neighborOccupied = half4(step(0.5h, up.a), step(0.5h, down.a), step(0.5h, left.a), step(0.5h, right.a));
                half4 neighborDifferent = half4(
                    step(0.001h, distance(center.rgb, up.rgb)),
                    step(0.001h, distance(center.rgb, down.rgb)),
                    step(0.001h, distance(center.rgb, left.rgb)),
                    step(0.001h, distance(center.rgb, right.rgb)));

                half4 diagonalOccupied = half4(step(0.5h, topLeft.a), step(0.5h, topRight.a), step(0.5h, bottomLeft.a), step(0.5h, bottomRight.a));
                half4 diagonalDifferent = half4(
                    step(0.001h, distance(center.rgb, topLeft.rgb)),
                    step(0.001h, distance(center.rgb, topRight.rgb)),
                    step(0.001h, distance(center.rgb, bottomLeft.rgb)),
                    step(0.001h, distance(center.rgb, bottomRight.rgb)));

                half4 neighborIds = half4(
                    dot(up.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(down.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(left.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(right.rgb, half3(1.0h, 256.0h, 65536.0h)));
                half4 diagonalIds = half4(
                    dot(topLeft.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(topRight.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(bottomLeft.rgb, half3(1.0h, 256.0h, 65536.0h)),
                    dot(bottomRight.rgb, half3(1.0h, 256.0h, 65536.0h)));

                // Draw the outside edge on background pixels. At a boundary between
                // two targets only the lower id owns the line, preventing two thick
                // outlines from being drawn over each other.
                half4 cardinalOwner = step(centerId + 0.001h, neighborIds);
                half4 diagonalOwner = step(centerId + 0.001h, diagonalIds);
                half4 cardinalEdges = neighborOccupied * ((1.0h - centerOccupied)
                    + centerOccupied * neighborDifferent * cardinalOwner);
                half4 diagonalEdges = diagonalOccupied * ((1.0h - centerOccupied)
                    + centerOccupied * diagonalDifferent * diagonalOwner);
                half edge = saturate(max(max(cardinalEdges.x, cardinalEdges.y), max(cardinalEdges.z, cardinalEdges.w))
                    + max(max(diagonalEdges.x, diagonalEdges.y), max(diagonalEdges.z, diagonalEdges.w)));

                return half4(_OutlineColor.rgb, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
