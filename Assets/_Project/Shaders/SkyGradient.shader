// Cartoon sky for a skybox material: a smooth gradient (top -> horizon -> bottom) with a soft glow around the sun.
// Used by the main menu (seen through the forge window). No texture, no clouds (clouds are 3D models).
Shader "OverSmithed/SkyGradient"
{
    Properties
    {
        _TopColor ("Top color", Color) = (0.55, 0.72, 0.92, 1)
        _HorizonColor ("Horizon color", Color) = (1, 0.84, 0.64, 1)
        _BottomColor ("Bottom color", Color) = (0.95, 0.78, 0.6, 1)
        _Exponent ("Gradient sharpness", Range(0.1, 4)) = 0.8
        _SunDirection ("Sun direction (toward the sun)", Vector) = (0.24, 0.3, 0.92, 0)
        _SunColor ("Sun glow color", Color) = (1, 0.8, 0.5, 1)
        _SunStrength ("Sun glow strength", Range(0, 2)) = 0.6
        _SunSize ("Sun glow sharpness", Range(1, 64)) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _Exponent;
                float4 _SunDirection;
                half4 _SunColor;
                half _SunStrength;
                half _SunSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float up = direction.y;
                half3 color = up >= 0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(up), _Exponent))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-up * 4));
                float sun = saturate(dot(direction, normalize(_SunDirection.xyz)));
                color += _SunColor.rgb * pow(sun, _SunSize) * _SunStrength;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
