Shader "Drift/Rainbow"
{
    // The rainbow of a world event (Drift.Visuals.RainbowArc owns the mesh: a band of the unit sphere around the
    // local +Z axis, which the component points at the spot opposite the sun). A rainbow has no place of its own:
    // it is light scattered back at a fixed angle from the antisolar direction, so the band is drawn around
    // whichever camera renders it, just inside the far plane, and the exact angle is taken per pixel. It therefore
    // looks right at every zoom and from a photo camera as well; islands in front of it hide it (depth test), and
    // everything below the horizon is faded out (_CurveSkyCenter, the same limb the sky dome uses), so the sea
    // never has a bow lying on it.
    //
    // Drawn after the water (Transparent+5) and before the storm clouds (Transparent+10), added to what is there.
    Properties
    {
        _ArcStrength ("Strength", Float) = 0
        _ArcPrimary ("Primary (outer deg, width deg)", Vector) = (42.4, 2.3, 0, 0)
        _ArcSecondary ("Secondary (inner deg, width deg, amount)", Vector) = (50.4, 3.4, 0.42, 0)
        _ArcSaturation ("Saturation", Range(0,1)) = 0.86
        _ArcInsideGlow ("Glow inside the bow", Range(0,0.5)) = 0.1
        _ArcFoot ("Brighter where it stands on the sea", Range(0,2)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Rainbow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Pushed by Drift.Visuals.CurvedWorld / CloudShadows; zero means "flat camera, no clouds".
            float4 _CurveSkyCenter;
            float  _CloudCover;

            CBUFFER_START(UnityPerMaterial)
                float4 _ArcPrimary;
                float4 _ArcSecondary;
                float  _ArcStrength;
                float  _ArcSaturation;
                float  _ArcInsideGlow;
                float  _ArcFoot;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirWS       : TEXCOORD0;
                float3 axisWS      : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 dir = normalize(mul((float3x3)unity_ObjectToWorld, IN.positionOS.xyz));
                OUT.positionHCS = TransformWorldToHClip(_WorldSpaceCameraPos + dir * (_ProjectionParams.z * 0.86));
                OUT.dirWS = dir;
                OUT.axisWS = mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1));
                return OUT;
            }

            // 0 = red, 1 = violet. Plain hue ramp; the pastel look comes from mixing it back towards white.
            float3 ArcHue(float u)
            {
                float h = u * 0.78;
                return saturate(abs(frac(h + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirWS);
                float3 axis = normalize(IN.axisWS);
                float theta = degrees(acos(clamp(dot(dir, axis), -1.0, 1.0)));

                // Primary bow: red on the outside, violet inside; soft edges, brightest in the middle of the band.
                float u = (_ArcPrimary.x - theta) / max(0.05, _ArcPrimary.y);
                // The hue starts a little inside the band, otherwise the soft outer edge fades the red away and
                // the bow reads as green only.
                float env = smoothstep(0.0, 0.10, u) * smoothstep(1.0, 0.82, u);
                float3 col = lerp(float3(1, 1, 1), ArcHue(saturate((u - 0.10) / 0.78)), _ArcSaturation) * env;

                // Secondary bow: the order turns round (red innermost) and it is much fainter.
                float u2 = (theta - _ArcSecondary.x) / max(0.05, _ArcSecondary.y);
                float env2 = smoothstep(0.0, 0.14, u2) * smoothstep(1.0, 0.76, u2);
                col += lerp(float3(1, 1, 1), ArcHue(saturate((u2 - 0.10) / 0.78)), _ArcSaturation * 0.8) * (env2 * _ArcSecondary.z);

                // Inside the primary bow the sky really is brighter (the dark band between the two bows is simply
                // where nothing is added).
                // The hump is zero at both ends, so the band's own inner edge (30 degrees) shows no seam.
                float inside = saturate((_ArcPrimary.x - _ArcPrimary.y - theta) / 9.5);
                col += _ArcInsideGlow * inside * (1.0 - inside) * 4.0;

                // Below the limb the sea has it; fade out there, all the way round the planet.
                float up = _CurveSkyCenter.w - dot(dir, _CurveSkyCenter.xyz);
                float horizon = saturate(up * 26.0);
                // A bow is brightest where it stands in the rain over the sea, and from the chase camera that
                // strip just above the limb is all of the sky there is - so the feet carry the whole event.
                horizon *= 1.0 + _ArcFoot * horizon * saturate(1.0 - up * 6.0);
                // A closed cloud cover puts the bow out.
                float cover = 1.0 - 0.55 * saturate(_CloudCover);

                // Authored in sRGB, added to a linear target: the cheap gamma-2 curve keeps the hues as authored.
                col *= col;
                return float4(col * (_ArcStrength * horizon * cover), 0);
            }
            ENDHLSL
        }
    }
}
