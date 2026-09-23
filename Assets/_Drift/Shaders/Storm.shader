Shader "Drift/Storm"
{
    // Storm clouds, the rain shafts under them and lightning bolts, all in one world-space dynamic mesh built by
    // Drift.Visuals.StormVisuals. uv0 = (storm centre x, z, spin rad/s, kind):
    //   kind 0 = cloud puff: all four corners at the puff centre, uv1 = (corner x, corner y, radius, seed); the
    //            puffs of one clump share a spin and orbit the storm centre, so the cluster churns without a rebuild.
    //   kind 1 = rain shaft (orbits with its clump), uv1 = (shaft radius at the sea, streak lane, height, cloud base).
    //   kind 2 = bolt.
    // The same shader is the additive bolt material (_SrcBlend One, _DstBlend One).
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        _Ambient ("Ambient", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Storm"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"
            #include "DriftCloudPuff.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 color       : COLOR;
                float4 uv0         : TEXCOORD1;
                float4 uv1         : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Ambient, _SrcBlend, _DstBlend;
            CBUFFER_END

            // Pushed by StormVisuals: xy = the player island, z/w = where the clear eye above it ends / is fully closed.
            float4 _StormEye;
            float _LightningFlash;
            // Pushed by StormVisuals. _DriftStormNight: 0 by day .. 1 at night (storms get a moonlit rim and a cold
            // flicker inside, so they still read against a dark sea). _DriftRainNear: x..y = rain fades in with the
            // distance to the camera, z = share of streak lanes left right in front of the camera, w = distance at
            // which all lanes are back (cozy keeps the rain out of the camera's face, adventure only the last metres).
            float _DriftStormNight;
            float4 _DriftRainNear;

            float EyeMask(float2 wp)
            {
                float d = length(wp - _StormEye.xy);
                return smoothstep(_StormEye.z, _StormEye.w, d);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float kind = IN.uv0.w;
                if (kind < 1.5)
                {
                    float a = _Time.y * IN.uv0.z;
                    float s = sin(a), c = cos(a);
                    float2 d = posWS.xz - IN.uv0.xy;
                    posWS.xz = IN.uv0.xy + float2(d.x * c - d.y * s, d.x * s + d.y * c);
                }
                OUT.color = IN.color;
                OUT.uv0 = IN.uv0;
                OUT.uv1 = IN.uv1;
                // The adventure ring ends at its rims: everything past the lip is folded onto it, so cloud and rain
                // outside the band would pile up there as a curtain hanging over the edge. Each one fades as soon as
                // it would reach past the rim itself, so the storm ends inside the band.
                if (kind < 1.5 && _CurveRing.x > 0.0)
                {
                    // Puff: its billboard radius. Shaft: its radius, twice, so the far side of the cylinder has faded
                    // before the near side reaches the lip (a shaft cut there folded into a grey sheet on the rim).
                    float reach = kind < 0.5 ? IN.uv1.z : IN.uv1.x * 1.2;
                    float over = abs(posWS.x - _CurveRing.y) + reach * 2.0 + 2.0 - _CurveRing.z;
                    OUT.color.a *= 1.0 - smoothstep(-2.0, 2.0, over);
                }
                if (kind < 0.5)
                {
                    float radius = IN.uv1.z;
                    float vis = EyeMask(posWS.xz) * DriftPuffClearView(posWS, radius);
                    OUT.color.a *= vis;
                    radius *= (0.6 + 0.4 * vis) * step(0.004, OUT.color.a);
                    OUT.positionHCS = TransformWorldToHClip(DriftPuffCorner(posWS, IN.uv1.xy, radius));
                }
                else
                {
                    OUT.positionHCS = DriftCurveHClip(posWS);
                }
                OUT.positionWS = posWS;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float kind = IN.uv0.w;
                float2 wp = IN.positionWS.xz;
                if (kind > 1.5)
                {
                    // Bolt: additive, colour * alpha carries the flicker. The wide glow ribbon around it (uv1.y = 1)
                    // falls off softly across (uv1.x = -1..1).
                    float across = 1.0 - saturate(abs(IN.uv1.x));
                    float soft = IN.uv1.y > 0.5 ? across * across : 1.0;
                    return float4(IN.color.rgb * (IN.color.a * soft), 1.0);
                }

                float flash = saturate(_LightningFlash);
                // Day / night brightness of the palette's cloud colour: storms darken with the sky.
                float3 pl = DriftPuffLitColor();
                float bright = saturate(dot(pl, float3(0.3, 0.5, 0.2)));

                if (kind < 0.5)
                {
                    // Storm puff: dark, heavy bellies, grey tops catching the light; the flash lights it up from inside.
                    float3 lit = lerp(IN.color.rgb * 1.35, DriftPuffDarkColor(), 0.2) * (0.35 + 0.65 * bright);
                    float3 dark = IN.color.rgb * 0.35 * (0.3 + 0.7 * bright);
                    float4 p = DriftPuffShade(IN.uv1.xy, IN.uv1.w, _Time.y * 0.12, lit, dark);
                    float3 col = p.rgb + flash * float3(0.75, 0.8, 0.95) * (0.3 + 0.3 * p.a);
                    UNITY_BRANCH
                    if (_DriftStormNight > 0.01)
                    {
                        float night = saturate(_DriftStormNight);
                        float r = length(IN.uv1.xy);
                        // Moonlit rim along the outline and a lighter body: a dark mass on a dark sea has no edge.
                        float rim = smoothstep(0.35, 0.85, r) * saturate(p.a * 1.5);
                        col = max(col, float3(0.1, 0.11, 0.16) * night) + float3(0.42, 0.48, 0.68) * (rim * 0.35 * night);
                        // Heat lightning: now and then a patch of the storm glows cold from inside for a moment - one
                        // spot per storm (uv0.xy = its centre), fading with the distance, so it lights a whole
                        // region of the mass instead of speckling single puffs.
                        float2 sc = IN.uv0.xy;
                        float beat = _Time.y * 0.55 + frac(dot(sc, float2(0.0131, 0.0173)));
                        float slot = floor(beat);
                        float h1 = frac(sin(slot * 12.9898 + sc.x * 0.37) * 43758.5453);
                        float h2 = frac(sin(slot * 78.233 + sc.y * 0.53) * 43758.5453);
                        float pulse = step(0.6, h1) * pow(saturate(sin(frac(beat) * 3.14159)), 4.0);
                        float2 spot = sc + (float2(h1, h2) - 0.5) * 14.0;
                        float2 dw = IN.positionWS.xz - spot;
                        float inside = exp(-dot(dw, dw) / 50.0);
                        col += float3(0.4, 0.45, 0.8) * (pulse * inside * 0.4 * night);
                    }
                    return float4(DriftFog(col, IN.positionWS), p.a * IN.color.a);
                }

                // Rain shaft: thin falling streaks in lanes over a faint grey veil, fading out towards the sea.
                Light l = GetMainLight();
                float litL = _Ambient + (1.0 - _Ambient) * saturate(l.direction.y + 0.2);
                float3 light = litL * lerp(float3(0.55, 0.6, 0.72), l.color, 0.6);
                float lane = floor(IN.uv1.y);
                float h = DriftHash(float2(lane, 7.13));
                float fu = frac(IN.uv1.y);
                // Shafts right in front of the camera would be a few fat bars: they fade out close up, and there
                // only a few thin lanes are left (in cozy over a long stretch, so a storm never greys out the view).
                float4 rn = _DriftRainNear.y > 0.0 ? _DriftRainNear : float4(6.0, 18.0, 0.35, 18.0);
                float camD = length(DriftCurveWS(IN.positionWS) - _WorldSpaceCameraPos);
                float nearK = smoothstep(rn.x, rn.y, camD);
                float farK = smoothstep(rn.x, max(rn.w, rn.x + 1.0), camD);
                float width = lerp(0.55, 1.0, farK);
                float lineMask = 1.0 - smoothstep(0.03 * width, 0.14 * width, abs(fu - 0.5));
                float dash = step(frac(IN.uv1.z * 0.6 + _Time.y * (2.2 + h) + h * 9.0), 0.35);
                float streak = lineMask * dash * step(lerp(rn.z, 0.35, farK), h);
                float v = IN.uv1.z / max(IN.uv1.w, 1.0);
                float veil = 0.1 * saturate(0.3 + 0.7 * v) * farK;
                float ends = smoothstep(0.0, 0.06, v) * (1.0 - smoothstep(0.6, 1.0, v));
                float alpha = IN.color.a * saturate(streak * 0.65 + veil) * ends * EyeMask(wp) * nearK;
                float3 col = IN.color.rgb * max(light, 0.35 * saturate(_DriftStormNight)) + flash * 0.5;
                return float4(DriftFog(col, IN.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
