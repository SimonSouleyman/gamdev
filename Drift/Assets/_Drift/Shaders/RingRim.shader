Shader "Drift/RingRim"
{
    // The foam line at the two rims of the adventure ring (Drift.Visuals.RingRims builds the mesh). The rim itself
    // is invisible - this is the only thing that says the world ends here: a band of foam lying flat on the water,
    // a bright crest right at the lip, spray flecks drifting along it. Procedural, no textures.
    // UV0 = (along the rim in units, distance inward from the lip in units), UV1 = (side: +1 / -1, 0..1 inward).
    // _RimGlow is pushed by Drift.Visuals.CurvedWorld from RingWorld.EdgeWarning: the closer the player, the
    // stronger the foam answers.
    Properties
    {
        _Foam ("Foam", Color) = (0.96, 0.99, 1, 1)
        _Spray ("Spray", Color) = (0.82, 0.93, 1, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.55
        _CrestWidth ("Crest Width", Float) = 0.9
    }
    SubShader
    {
        // After the water (Water.shader is Queue 3000, ZWrite Off): the foam lies ON the sea, so it has to be
        // drawn over it - within one queue the sort is by bounds centre and the sea would win.
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }

        Pass
        {
            Name "Rim"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftCurve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float2 uv0         : TEXCOORD1;
                float2 uv1         : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Foam;
                float4 _Spray;
                float _Opacity;
                float _CrestWidth;
            CBUFFER_END

            float _RimGlow;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Smooth 1D value noise: the foam varies along the rim without repeating lanes.
            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash(float2(i, 3.0)), Hash(float2(i + 1.0, 3.0)), f);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.uv0 = IN.uv0;
                OUT.uv1 = IN.uv1;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                // World z, not the mesh's own uv: RingRims re-centres the mesh in 64 u steps, and a pattern tied to
                // the mesh jumped with every step.
                float along = IN.positionWS.z;
                float inward = IN.uv0.y;
                float glow = saturate(_RimGlow);

                // Along the track the rim is seen at a grazing angle, so one pixel covers metres of it: every detail
                // finer than that fades to its mean. Its old 2 u foam lanes with gaps between them aliased into the
                // "wall of shimmering horizontal bands" along the rims.
                float px = max(fwidth(along), 1e-4);
                float detail = 1.0 - smoothstep(0.25, 1.0, px);
                float fine = 1.0 - smoothstep(0.1, 0.4, px);

                // The crest: a bright line right on the lip, breathing a little along the rim.
                float breathe = 0.75 + 0.25 * sin(along * 0.21 + t * 0.7);
                breathe = lerp(0.85, breathe, detail);
                float crest = exp(-inward / max(0.15, _CrestWidth * breathe));

                // Foam washing inward: one continuous tongue whose reach wanders with a slow noise and swells in and
                // out over time - no lanes, no gaps.
                float n = Noise1(along * 0.23 + 11.0) * 0.65 + lerp(0.5, Noise1(along * 0.61 - t * 0.35), fine) * 0.35;
                float swell = 0.65 + 0.35 * sin(t * 0.8 + along * 0.13 + 6.2832 * Noise1(along * 0.07));
                float reach = lerp(2.4, (1.2 + 2.8 * n) * swell, detail);
                float tongue = saturate(1.0 - inward / max(0.3, reach));
                tongue *= tongue;

                // A few spray flecks drifting along the line, only close to the lip and only where they can be seen.
                float fu = along * 1.7 + t * 0.6;
                float cell = floor(fu);
                float fleck = step(0.92, Hash(float2(cell, 17.0))) * exp(-inward * 1.6)
                            * smoothstep(0.0, 0.3, frac(fu)) * smoothstep(1.0, 0.7, frac(fu))
                            * (1.0 - smoothstep(0.04, 0.15, px));

                float a = saturate(crest * (0.55 + 0.45 * glow) + tongue * 0.3 + fleck * 0.5 * (0.4 + 0.6 * glow));
                float3 col = lerp(_Spray.rgb, _Foam.rgb, saturate(crest + fleck));
                a *= _Opacity * (0.7 + 0.5 * glow);
                return float4(DriftFog(col, IN.positionWS), saturate(a));
            }
            ENDHLSL
        }
    }
}
