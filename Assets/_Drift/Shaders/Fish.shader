Shader "Drift/Fish"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _WaterTint ("Underwater Tint", Color) = (0.35,0.6,0.7,1)
        _SubmergedAlpha ("Submerged Alpha", Range(0,1)) = 0.72
        _SubmergeDepth ("Submerge Fade Depth", Float) = 0.12
        _Ambient ("Ambient", Range(0,1)) = 0.55
    }
    SubShader
    {
        // Drawn after the water (Transparent+5) with ZWrite off: the island terrain's depth still
        // occludes fish under the beach, while the water plane (ZWrite Off) never hides them.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Fish"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            // Everything is lit per vertex: the light, the cloud shadow, the underwater tint and the haze all vary over
            // many fish lengths, and a fish is a handful of vertices a fraction of a unit apart, so the fragment only
            // outputs the interpolated colour.
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color        : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint, _WaterTint;
                float _SubmergedAlpha, _SubmergeDepth, _Ambient;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(posWS);
                Light l = GetMainLight();
                float lit = _Ambient + (1.0 - _Ambient) * saturate(l.direction.y);
                float3 col = IN.color.rgb * _Tint.rgb * lit * l.color * CloudShadow(posWS.xz);
                float below = saturate(-posWS.y / max(_SubmergeDepth, 1e-3));
                col = lerp(col, col * _WaterTint.rgb, below * 0.7);
                float alpha = lerp(1.0, _SubmergedAlpha, below) * IN.color.a;
                OUT.color = half4(DriftFog(col, posWS), alpha);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }
}
