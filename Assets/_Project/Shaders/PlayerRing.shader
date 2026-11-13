// Colored ring drawn on the floor under a player (GP-18). Put it on a flat quad (UVs 0..1).
// The ring is computed from the distance to the quad's center: no texture, sharp at any size.
// Unlit and transparent: it keeps the same color in light and in shadow.
Shader "OverSmithed/PlayerRing"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.2, 0.55, 1, 1)
        _Radius ("Outer radius (0.5 = quad edge)", Range(0, 0.5)) = 0.46
        _Thickness ("Thickness", Range(0, 0.5)) = 0.1
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _OutlineColor ("Outline color", Color) = (0.08, 0.06, 0.05, 0.6)
        _OutlineWidth ("Outline width (0 = none)", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // Pulled a bit toward the camera so it never flickers with the floor under it.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Radius;
                float _Thickness;
                float _Opacity;
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // 1 between inner and outer, 0 elsewhere, with about one pixel of soft edge at any distance.
            float Band(float d, float inner, float outer, float edge)
            {
                return smoothstep(inner - edge, inner, d) * (1.0 - smoothstep(outer - edge, outer, d));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = distance(input.uv, float2(0.5, 0.5));
                float edge = max(fwidth(d), 0.0001);
                float inner = _Radius - _Thickness;
                float ring = Band(d, inner, _Radius, edge);
                // Thin dark line on both sides of the ring: readable on any floor color.
                float outline = _OutlineWidth > 0 ? Band(d, inner - _OutlineWidth, _Radius + _OutlineWidth, edge) : 0;

                half3 color = lerp(_OutlineColor.rgb, _BaseColor.rgb, ring);
                half alpha = max(ring * _BaseColor.a, outline * _OutlineColor.a);
                return half4(color, alpha * _Opacity);
            }
            ENDHLSL
        }
    }
}
