Shader "BORis/Workshop Blue Hour"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(float3 position : POSITION)
            {
                Varyings output;
                output.position = TransformObjectToHClip(position);
                output.direction = position;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                half skyHeight = smoothstep(0.0, 0.58, direction.y);
                half sunset = pow(saturate(dot(direction, normalize(float3(0.92, 0.03, -0.35)))), 6.0);
                half horizon = sunset * exp(-abs(direction.y - 0.025) * 8.0);
                half3 color = lerp(half3(0.32, 0.40, 0.52), half3(0.064, 0.125, 0.26), skyHeight);
                color += half3(0.28, 0.105, 0.025) * horizon;
                half bands = sin(direction.y * 102.0 + sin(direction.z * 8.5)) +
                    0.45 * sin(direction.y * 173.0 + direction.z * 24.0);
                color += half3(0.10, 0.065, 0.065) * smoothstep(0.8, 1.35, bands) * horizon;
                return half4(SRGBToLinear(color), 1);
            }
            ENDHLSL
        }
    }
}
