Shader "BORis/Workshop City"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float3 position : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                half3 color : COLOR;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.position);
                output.position = TransformWorldToHClip(output.positionWS);
                output.color = input.color.rgb;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 offset = input.positionWS - _WorldSpaceCameraPos;
                half fog = smoothstep(35.0, 445.0, length(offset));
                float3 direction = normalize(offset);
                half skyHeight = smoothstep(0.0, 0.58, direction.y);
                half sunset = pow(saturate(dot(direction, normalize(float3(0.92, 0.03, -0.35)))), 6.0);
                half horizon = sunset * exp(-abs(direction.y - 0.025) * 8.0);
                half3 haze = lerp(half3(0.32, 0.40, 0.52), half3(0.064, 0.125, 0.26), skyHeight);
                haze += half3(0.28, 0.105, 0.025) * horizon;
                half brightness = max(input.color.r, max(input.color.g, input.color.b));
                half3 facade = input.color * lerp(0.59, 0.95, smoothstep(0.49, 0.87, brightness));
                return half4(SRGBToLinear(lerp(facade, haze, fog)), 1);
            }
            ENDHLSL
        }
    }
}
