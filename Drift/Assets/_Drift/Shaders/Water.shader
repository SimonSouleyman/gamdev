Shader "Drift/Water"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.05,0.24,0.45,1)
        _ShallowColor ("Shallow Color", Color) = (0.22,0.72,0.78,1)
        _FoamColor ("Foam Color", Color) = (0.95,0.98,1,1)
        _SkyColor ("Sky Reflection", Color) = (0.6,0.8,1,1)
        _StormTint ("Storm Tint", Color) = (0.5,0.56,0.66,1)
        _DepthRange ("Depth Range", Float) = 2.6
        _ShallowWidth ("Shallow Band Width", Float) = 1.8
        _FoamWidth ("Foam Width", Float) = 0.55
        _WaveScale ("Wave Scale", Float) = 0.45
        _WaveSpeed ("Wave Speed", Float) = 0.7
        _WaveNormalScale ("Wave Normal Strength", Range(0,2)) = 0.55
        _DetailScale ("Detail Ripple Scale", Float) = 5.0
        _DetailStrength ("Detail Ripple Strength", Range(0,0.5)) = 0.08
        _StormWaveBoost ("Storm Wave Boost", Range(0,3)) = 1.1
        _Specular ("Specular", Range(0,2)) = 0.9
        _SpecPower ("Specular Power", Range(2,200)) = 32
        _MoonReflectSize ("Moon Reflection Size", Range(0.5,4)) = 1.8
        _MoonGlitter ("Moon Glitter", Range(0,4)) = 0.9
        _Fresnel ("Fresnel Sky Tint", Range(0,1)) = 0.35
        _Sparkle ("Shallow Sparkle", Range(0,1)) = 0.22
        _Caustic ("Shallow Caustics", Range(0,1)) = 0.22
        _MinAlpha ("Shallow Alpha", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Marks every sea pixel for Drift/CurvedSky, which is drawn right after the water and skips them (it used
            // to fill the whole screen behind the sea). Where nothing opaque lies behind, the water puts the sky's
            // below-the-limb colour (_CurveFogColor) under itself, see the end of frag.
            Stencil { Ref 8 WriteMask 8 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                half4  current     : TEXCOORD2;   // _CurrentField at the vertex: 5 u texels, bilinear, carried well by the 3 u grid
                float3 bentWS      : TEXCOORD3;   // where the pixel really is (its view ray, for the sky under the sea)
                // StormField at the vertex: its rim noise has a 20 u lattice and eases over a third of the storm's radius,
                // which the sea grid (3 u around the view) carries without a visible difference.
                half   storm       : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _FoamColor, _SkyColor, _StormTint;
                float _DepthRange, _ShallowWidth, _FoamWidth, _WaveScale, _WaveSpeed, _WaveNormalScale;
                float _DetailScale, _DetailStrength, _StormWaveBoost;
                float _Specular, _SpecPower, _Fresnel, _Sparkle, _Caustic, _MinAlpha;
                float _MoonReflectSize, _MoonGlitter;
            CBUFFER_END

            #include "DriftWater.hlsl"

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.bentWS = DriftCurveWS(posWS);
                OUT.positionHCS = TransformWorldToHClip(OUT.bentWS);
                OUT.positionWS = posWS;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                float2 fuv = (posWS.xz - _CurrentFieldParams.xy) * _CurrentFieldParams.z;
                OUT.current = SAMPLE_TEXTURE2D_LOD(_CurrentField, sampler_CurrentField, fuv, 0);
                OUT.storm = (half)StormField(posWS.xz);
                return OUT;
            }

            // Sum of sines with sharpened (gerstner-like) crests: h = a * (2e^2 - 1), e = 0.5 + 0.5 sin.
            // Returns the height gradient (for the normal) and the normalized height (-1..1).
            // The phases (world position x frequency + time) are large numbers and stay 32-bit; everything after the
            // sincos is a few units at most and runs in half precision (twice the rate on mobile GPUs).
            void Waves(float2 p, float t, half storm, float2 wind, out half2 grad, out half height)
            {
                const float2 dirs[5] = { float2(0.914, 0.406), float2(-0.6, 0.8), float2(0.301, -0.954), float2(-0.862, -0.507), float2(0.707, 0.707) };
                const float freq[5] = { 0.7, 1.1, 1.6, 2.4, 3.6 };
                const half amp[5]  = { 0.5, 0.35, 0.22, 0.14, 0.08 };
                const float spd[5]  = { 0.9, 1.15, 1.5, 1.9, 2.4 };
                float scale = _WaveScale * 2.0 * (1.0 + 0.3 * storm);
                half ampMul = 1.0h + (half)_StormWaveBoost * storm;
                grad = 0;
                height = 0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float2 d = dirs[i];
                    float k = freq[i] * scale;
                    half a = amp[i] * ampMul;
                    float s, c;
                    sincos(dot(d, p) * k + t * spd[i], s, c);
                    half e = (half)s * 0.5h + 0.5h;
                    height += a * (2.0h * e * e - 1.0h);
                    grad += (half2)d * ((half)k * a * 2.0h * e * (half)c);
                }
                // Storm chop travelling with the wind so gusts and waves agree on a direction (amplitude 0 in calm water).
                [branch] if (storm > 0.0)
                {
                    float k = 2.2 * scale;
                    half a = 0.45h * storm;
                    float s, c;
                    sincos(dot(wind, p) * k + t * 2.6, s, c);
                    half e = (half)s * 0.5h + 0.5h;
                    height += a * (2.0h * e * e - 1.0h);
                    grad += (half2)wind * ((half)k * a * 2.0h * e * (half)c);
                }
                height /= 1.29h + 0.45h * storm;
            }

            // Fine ripples: three short sines, one aligned with the wind, only affecting the normal.
            half2 Detail(float2 p, float t, half storm, float2 wind)
            {
                float2 d0 = wind;
                float2 d1 = float2(-0.5, 0.866);
                float2 d2 = float2(0.3, -0.954);
                float k = _DetailScale;
                half a = (half)_DetailStrength * (1.0h + 1.5h * storm);
                half2 g = (half2)d0 * (a * (half)cos(dot(d0, p) * k + t * 3.0));
                g += (half2)d1 * (a * 0.7h * (half)cos(dot(d1, p) * k * 1.3 + t * 3.7));
                g += (half2)d2 * (a * 0.5h * (half)cos(dot(d2, p) * k * 1.7 - t * 4.3));
                return g;
            }

            // Stretched noise without its lattice: a value noise sampled on a squashed grid shows its cells as a
            // plaid of rectangles, so the two octaves sit on grids ROTATED against the stretch and are warped by
            // a slow isotropic noise. Used wherever foam is drawn out along a direction.
            float StretchedNoise(float2 sp, float2 wp)
            {
                float warp = DriftNoiseStable(wp * 0.11 + 7.1) - 0.5;
                float2 r1 = float2(sp.x * 0.819 - sp.y * 0.574, sp.x * 0.574 + sp.y * 0.819) + warp * 0.9;
                float2 r2 = float2(sp.x * 0.545 + sp.y * 0.839, -sp.x * 0.839 + sp.y * 0.545) * 1.7 + 0.5 - warp * 1.3;
                return DriftNoiseStable(r1) * 0.55 + DriftNoiseStable(r2) * 0.45;
            }

            // Wake along the island's real coastline. `sd` is the distance to the shore and `nrm` the outward
            // normal of the shore point nearest to the pixel, both from the coast distance field, so every term
            // follows the outline into its notches: bow foam is a band on the shore that faces the way the island
            // travels, the churn is the water behind a shore that faces away (reaching furthest straight astern
            // and dying out towards the flanks) and a V line leaves each of the two widest points. What used to be
            // here built the churn from the bounding box across the velocity, which drew a white rectangle.
            void PlayerWake(float2 wp, float sd, float2 nrm, float coastFade, float churn, float n1,
                            out float wakeFoam, out float bowFoam, out float churnTint)
            {
                wakeFoam = 0; bowFoam = 0; churnTint = 0;
                float2 rel = wp - _PlayerPos.xy;
                float d2 = dot(rel, rel);
                if (d2 >= _PlayerWake.z * _PlayerWake.z || sd <= 0.0) return;

                float speed = length(_PlayerVel.xy);
                float2 dir = _PlayerVel.xy / max(speed, 1e-4);
                float speedF = saturate(speed / 3.0);
                float along = dot(rel, dir);
                float across = dot(rel, float2(-dir.y, dir.x));
                float facing = dot(nrm, dir);

                float lead = saturate(facing);
                float band = _PlayerWake.w;
                float shell = 1.0 - saturate(abs(sd - (0.15 + 0.3 * speedF) * band) / ((0.3 + 0.6 * speedF) * band));
                float bow = shell * lead * sqrt(lead) * speedF * coastFade;

                float aL = _PlayerWakeEdge.x, cL = _PlayerWakeEdge.y, aR = _PlayerWakeEdge.z, cR = _PlayerWakeEdge.w;
                float astern = saturate(-facing);
                astern *= astern;
                // The trail reaches further the faster the island goes; _PlayerWake.x already keeps it inside
                // the coast field, whose border fade would otherwise cut the foam along a straight line.
                float tailN = saturate(sd / max(_PlayerWake.x * astern * (0.55 + 0.45 * speedF), 0.35));
                // The lattice of a stretched value noise would show as rectangles, so it is sampled on a grid
                // rotated against the flow - the same reason the wind streaks rotate theirs.
                float2 sp = float2(along * 0.3 + _Time.y * 0.5 + (churn - 0.5) * 0.6, across * 1.9);
                float streak = DriftNoiseStable(float2(sp.x * 0.819 - sp.y * 0.574, sp.x * 0.574 + sp.y * 0.819));
                // Behind a very wide island the churn is toned down (WaterFeedback.WakeDamping): at full strength over the
                // whole stern it read as a white field instead of a wake. 0 for every island up to ~16 u across.
                float wake = astern * (1.0 - tailN) * (1.0 - tailN) * speedF * coastFade * (1.0 - _PlayerWakeDamp);

                // The lines wander with the churn noise and widen as they fade, so they read as foam, not as rulers.
                float sL = aL - along, sR = aR - along;
                float wob = (churn - 0.5) * band;
                float fadeL = saturate(1.0 - sL / _PlayerWake.y), fadeR = saturate(1.0 - sR / _PlayerWake.y);
                float vL = (1.0 - saturate(abs(across - cL - 0.3 * sL + wob * (0.5 + 0.08 * sL)) / (band * (0.3 + 0.07 * sL)))) * saturate(sL * 1.5) * fadeL * sqrt(fadeL);
                float vR = (1.0 - saturate(abs(across - cR + 0.3 * sR + wob * (0.5 + 0.08 * sR)) / (band * (0.3 + 0.07 * sR)))) * saturate(sR * 1.5) * fadeR * sqrt(fadeR);
                float vee = max(vL, vR) * speedF;

                wakeFoam = smoothstep(0.3, 0.85, wake * (0.2 + 0.65 * churn + 0.6 * streak))
                         + smoothstep(0.2, 0.75, vee * (0.35 + 1.0 * n1)) * 0.8;
                bowFoam = smoothstep(0.12, 0.6, bow * (0.5 + 0.8 * n1));
                churnTint = wake * 0.3;
            }

            float ImpactRing(float2 wp, float2 c, float age, float strength, float churn)
            {
                // Single exits under [branch]: FXC warns about (and some mobile compilers mishandle) early returns there.
                float res = 0.0;
                [branch] if (strength > 0.0)
                {
                    float d = length(wp - c);
                    float r = age * 6.0;
                    float w = 0.3 + 0.25 * age;
                    float band = exp(-(d - r) * (d - r) / (w * w));
                    float inner = saturate(1.0 - d / max(r, 0.01)) * saturate(1.0 - age / 0.6) * 0.5;
                    float life = saturate(1.0 - age / 2.5);
                    res = (band + inner) * life * strength * (0.25 + 0.5 * churn) * step(0.0, age);
                }
                return res;
            }

            // Thin foam streaks sliding past the island, in a band that follows its SHORE (not a circle around the
            // bounding radius, which on a branched island runs across open water). They exist only while the island
            // is really under way and scroll with its own speed, so speed is readable in a still frame.
            float SpeedLines(float2 wp, float sd, float coastFade, float aa)
            {
                float gain = _SpeedFeelGains.x;
                float drive = _SpeedFeel.x;
                if (gain <= 0.0 || drive <= 0.05 || coastFade <= 0.0 || sd <= 0.0) return 0.0;
                const float inner = 0.8, outer = 14.0;
                if (sd >= outer) return 0.0;
                float band = saturate((sd - inner) / 2.5) * (1.0 - smoothstep(outer - 7.0, outer, sd));
                if (band <= 0.0) return 0.0;
                float speed = max(_SpeedFeel.w, 0.5);
                float2 rel = wp - _PlayerPos.xy;
                float2 v = _PlayerVel.xy;
                float2 dir = v / max(length(v), 1e-4);
                float a = dot(rel, dir) + _Time.y * (speed * 1.7);
                float b = dot(rel, float2(-dir.y, dir.x));
                float streak = smoothstep(0.6, 0.92, StretchedNoise(float2(a * 0.09, b * 1.6), wp));
                // Fade out once a pixel covers more than a streak: from far away they would only shimmer.
                return streak * band * coastFade * saturate(drive) * gain * (0.55 + 0.75 * _SpeedFeel.y) * saturate(1.0 - aa * 4.0);
            }

            // The wide ring of spray a merge throws: fast, ragged and gone within _SprayPos life. Both the rim
            // wobble and the speckle are sampled in world space - a noise keyed on the radius draws contour
            // rings instead of foam.
            float SprayRing(float2 wp, float churn)
            {
                float st = _SprayPos.w;
                if (st <= 0.0) return 0.0;
                float age = _SprayPos.z;   // 0..1 over the ring's life
                float life = saturate(1.0 - age);
                if (life <= 0.0) return 0.0;
                float2 rel = wp - _SprayPos.xy;
                float d = length(rel) + (DriftNoiseStable(rel * 0.55 + 5.3) - 0.5) * (1.5 + 3.0 * age);
                float r = age * (12.0 + 16.0 * st);
                float w = 0.6 + 2.6 * age;
                float ring = exp(-(d - r) * (d - r) / (w * w));
                float speck = smoothstep(0.3, 0.92, DriftNoiseStable(wp * 2.1 + 17.0) * 0.6 + churn * 0.4);
                return ring * life * life * st * (0.35 + 1.0 * speck);
            }

            // Tiny ring left by a jumping fish: 0.15 u -> ~1.1 u over 0.8 s.
            float SplashRing(float2 wp)
            {
                float res = 0.0;
                [branch] if (_SplashPos.w > 0.0)
                {
                    float age = _SplashPos.z;
                    float d = length(wp - _SplashPos.xy);
                    float r = 0.15 + age * 1.2;
                    float w = 0.08 + 0.12 * age;
                    float band = exp(-(d - r) * (d - r) / (w * w));
                    float life = saturate(1.0 - age / 0.8);
                    res = band * life * life * _SplashPos.w;
                }
                return res;
            }

            // Soft, wide, low-contrast foam streaks stretched along the wind. Elongation comes from
            // sampling the noise with a very short scale along the wind and a long one across it.
            float WindStreaks(float2 wp, float2 wind, float storm, float mask)
            {
                float u = dot(wp, wind);
                float v = dot(wp, float2(-wind.y, wind.x));
                float scroll = 0.35 + 0.25 * _CurrentSpeed + 1.2 * storm;
                float2 sp = float2((u - _Time.y * scroll) * 0.075, v * 0.45);
                float s1 = StretchedNoise(sp, wp);
                float s2 = DriftNoiseStable(wp * 0.3 + wind * (_Time.y * 0.25) + 13.7);
                float streak = smoothstep(0.42, 0.95, s1 * 0.6 + s2 * 0.4);
                // Storm-only now: the plate current has its own drifting flecks (Drift/WaterFlecks), and wind-aligned
                // streaks that also grew with the current pointed the wrong way whenever wind and current disagreed.
                float strength = 0.63 * storm;
                return streak * mask * saturate(strength);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // Derivatives first, while every pixel of the quad is still running (the early outs below diverge).
                float2 wp = IN.positionWS.xz;
                float fw = max(fwidth(wp.x), fwidth(wp.y));
                // The depth maths stays 32-bit from the interpolated screen position to `diff` (a raw reversed-Z depth of a
                // far shore is a tiny number; in half the shore foam, which hangs off `diff`, would break into steps).
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfDepth = IN.screenPos.w;
                float diff = max(0.0, sceneDepth - surfDepth);

                // The sea dissolves into the sky colour at the limb, so the horizon is closed whatever the mesh does there.
                // Where the haze is complete nothing below would show: a zoomed-out view is mostly such pixels.
                float haze = DriftFogAmount(IN.positionWS);
                [branch] if (haze >= 0.998) return float4(_CurveFogColor.rgb, 1.0);

                float2 coastN;
                float coastFade;
                float coastD = CoastDistance(wp, coastN, coastFade);
                float3 toCam = GetCameraPositionWS() - IN.positionWS;
                float camDist = length(toCam);
                // Rough dark water under every storm, not just the one the player is in, so a storm is seen coming.
                // Near the camera the sea grid is 3 u and carries StormField from the vertices; further out its triangles
                // double in size every ring and the ragged rim of a distant storm needs the per-pixel field.
                float stormField = IN.storm;
                [branch] if (camDist >= 50.0) stormField = StormField(wp);
                half storm = (half)saturate(max(_Storm, stormField));
                float t = _Time.y * _WaveSpeed;
                float2 wind = normalize(_WindDir.xy + float2(1e-4, 0));
                // Past 120 u farLod below has flattened the waves to under 2 % brightness and a barely tilted normal:
                // they fade out completely by 170 u and the far sea (most of a zoomed-out or title view) skips the sines.
                half waveFar = (half)saturate((170.0 - camDist) / 50.0);
                half2 g = 0;
                half h = 0;
                [branch] if (waveFar > 0.0)
                {
                    Waves(wp, t, storm, wind, g, h);
                    g *= waveFar;
                    h *= waveFar;
                }
                // Fine sines alias into a moire lattice when the camera is far: fade the detail and
                // flatten the main waves with distance instead of letting them shimmer.
                half lod = (half)saturate(1.0 - (camDist - 12.0) / 30.0);
                // The curved world shows the sea from 100 u and more, where even the flattened waves and their glints
                // line up into a grid: a second, slower fade takes them down to a calm sheen.
                half farLod = (half)(1.0 - 0.85 * saturate((camDist - 40.0) / 80.0));
                g = g * ((half)_WaveNormalScale * lerp(0.3h, 1.0h, lod) * farLod);
                [branch] if (lod > 0.0) g += Detail(wp, t, storm, wind) * lod;

                half3 light = _WaterLight.w > 0.0 ? (half3)_WaterLight.rgb : half3(1, 1, 1);
                half3 shallowCol = (half3)_ShallowColor.rgb * light;
                half3 foamCol = (half3)_FoamColor.rgb * light;

                // Tint and shore foam come from the camera depth texture, which is the real beach outline of
                // EVERY island. No analytic island distance is used for them: a circle on the bounding radius
                // once produced a turquoise disc three times the size of the island.
                half depthFade = (half)saturate(diff / _DepthRange);
                half shallowMask = 1.0h - smoothstep(0.15h, 0.9h, depthFade);
                half3 col = lerp(shallowCol, (half3)_DeepColor.rgb, pow(depthFade, 0.7h));
                col *= 1.0h + h * (0.05h + 0.05h * storm) * farLod;

                float3 V = toCam / max(camDist, 1e-6);
                Light l = GetMainLight();
                // The glint stays 32-bit: pow(nh, 32) turns the half-precision steps of nh near 1 into visible bands.
                float3 H = normalize(l.direction + V);
                // Glints from a calmed normal too: the broad lobe on the full wave normal lit every crest facing the sun
                // and drew a lattice of white bands across the whole sea.
                float3 nSpec = normalize(float3(-g.x * 0.35, 1.0, -g.y * 0.35));
                float nh = saturate(dot(nSpec, H));
                float nh2 = nh * nh;
                float nh4 = nh2 * nh2;
                float spec = pow(nh, _SpecPower) * _Specular * 0.55 + nh4 * nh4 * nh4 * 0.03 * _Specular;
                spec *= saturate(l.direction.y * 4.0) * (1.0 - 0.5 * storm) * farLod * farLod;
                // At night the main light is the moonlight key, lit from high up for the island: its glints would
                // cover the sea in daylight-white blotches. The moon's own glitter path below takes over.
                spec *= 1.0 - 0.9 * saturate(_SkyStars.x * 1.5);
                // Sky reflection from a calmed normal: with the full wave normal every crest facing away from the camera
                // turned into a long white streak across the sea, which read as stripy clouds.
                half3 nSky = normalize(half3(-g.x * 0.25h, 1.0h, -g.y * 0.25h));
                half fres1 = 1.0h - saturate(dot(nSky, (half3)V));
                half fres2 = fres1 * fres1;
                col = lerp(col, (half3)_SkyColor.rgb, fres2 * fres2 * (half)_Fresnel);
                col += (half3)(l.color * spec);

                // The moon in the water, night only (the chase camera looks down past the limb, so this is where the
                // player sees it): its mirror image on a half-calmed normal (the waves break it into a wobbling disc),
                // a faint sheen round it, and a glitter path from it towards the camera - a narrow band on the moon's
                // bearing, pushed sideways by the waves' slope and broken into sparkles by two noise lookups. (A pow()
                // glint on the wave normal instead drew the waves' sine lattice over half the sea.) Nothing while the
                // sky is light or the moon is down, and not on the adventure ring: this is a flat-sea mirror (positionWS is
                // unbent), which the ring's band, curving up ahead, turned into a grey cross.
                float moonVis = _SkyStars.x * _SkyMoonColor.a * saturate(_SkyMoonDir.y * 10.0) * (1.0 - 0.8 * storm)
                              * (_CurveRing.x > 0.0 ? 0.0 : 1.0);
                [branch] if (moonVis > 0.01)
                {
                    float2 rh = normalize(-V.xz + 1e-5);
                    float2 mh = normalize(_SkyMoonDir.xz + 1e-5);
                    float2 side = float2(mh.y, -mh.x);
                    float across = dot(rh, side) + dot(g, side) * 0.12;
                    float nearer = V.y - _SkyMoonDir.y;
                    float width = 0.035 + 0.05 * saturate(nearer * 3.0);
                    // Everything below (disc, sheen and glitter) is scaled by nearPath, which is 0 more than two path
                    // widths off the moon's bearing: most of the night sea stops here.
                    float nearPath = saturate(1.0 - (across * across) / (width * width * 4.0));
                    [branch] if (nearPath > 0.0)
                    {
                        float3 nMoon = normalize(float3(-g.x * 0.2, 1.0, -g.y * 0.2));
                        float3 R = reflect(-V, nMoon);
                        float3 dm = R - _SkyMoonDir.xyz;
                        float mr2 = 1.0 / (_SkyMoonDir.w * _SkyMoonDir.w);
                        float md2 = dot(dm, dm) / (mr2 * _MoonReflectSize * _MoonReflectSize);
                        float mdisc = saturate(1.6 - md2 * 1.6);
                        float mglow = 1.0 / (1.0 + md2 * 0.35);

                        float band = saturate(1.0 - (across * across) / (width * width)) * step(0.0, dot(rh, mh));
                        band *= saturate(nearer * 12.0 + 0.6) * saturate(1.0 - nearer * 2.2);
                        float glit = 0.0;
                        [branch] if (band > 0.001)
                        {
                            float sp = DriftNoiseStable(wp * 6.2 + float2(0.0, t * 1.7)) * 0.55 + DriftNoiseStable(wp * 13.1 - t * 1.3) * 0.45;
                            glit = band * (0.08 + smoothstep(0.58, 0.8, sp) * 1.2) * _MoonGlitter;
                        }
                        float lit = 0.35 + 0.65 * saturate(_SkyStarParams.y);
                        // The mirror disc only near the path: with the waves it would scatter as white blobs over the sea.
                        col += (half3)(_SkyMoonColor.rgb * (moonVis * lit * ((mdisc * mdisc * 0.85 + mglow * 0.05) * nearPath + glit)));
                    }
                }

                // The foam flecks carried by the current are drawn by Drift/WaterFlecks right after the sea (one small
                // quad per living fleck instead of four cell lookups in every sea pixel); only the plate-boundary
                // closeness they also carry is needed here, for the surf foam.
                half curEdge = 0.0;
                [branch] if (_CurrentFieldParams.w > 0.0)
                {
                    float2 fuv = (wp - _CurrentFieldParams.xy) * _CurrentFieldParams.z;
                    float2 inside = saturate(min(fuv, 1.0 - fuv) * 12.0);
                    curEdge = IN.current.b * (half)(inside.x * inside.y);
                }

                // The two churn noises feed only the shore foam, caustics, wake, impact/splash/spray rings, whitecaps and
                // the surf foam - each of them exactly 0 outside its own zone - so open water skips the eight hashes.
                // Their inputs scroll with time: DriftNoiseStable keeps them from degrading into square tiles once the
                // session (or the island's position) gets large.
                float2 relP = wp - _PlayerPos.xy;
                bool wakeZone = dot(relP, relP) < _PlayerWake.z * _PlayerWake.z && coastD > 0.0;
                bool needChurn = diff < max(_FoamWidth * 3.2, 0.9 * _DepthRange) || wakeZone || storm > 0.001
                               || max(_RingStrength.x, _RingStrength.y) > 0.0 || _SplashPos.w > 0.0 || _SprayPos.w > 0.0
                               || (curEdge > 0.0 && _SpeedFeel.z * _SpeedFeelGains.w > 0.0);
                half n1 = 0.0, n2 = 0.0;
                [branch] if (needChurn)
                {
                    n1 = (half)DriftNoiseStable(wp * 1.7 + t * 0.6);
                    n2 = (half)DriftNoiseStable(wp * 3.1 - t * 0.4);
                }
                half churn = n1 * 0.6h + n2 * 0.4h;

                // Shore foam that surges and breaks up over time instead of a fixed rim (depth only: the analytic circle
                // drew a white disc around every elongated island). Both terms are 0 past 3.2 foam widths of water.
                half foam = 0.0, ring = 0.0;
                [branch] if (diff < _FoamWidth * 3.2)
                {
                    half surge = 0.5h + 0.5h * (half)sin(t * 0.9 + diff * 5.0 + n2 * 3.0);
                    half foamBase = (half)(1.0 - saturate(diff / _FoamWidth));
                    foam = smoothstep(0.5h, 0.9h, foamBase * (0.3h + 0.5h * n1 + 0.35h * surge)) * (0.45h + 0.4h * n2);
                    ring = smoothstep(0.85h, 1.0h, (half)sin(diff * 7.0 - _Time.y * 1.6 + n2 * 2.0) * 0.5h + 0.5h)
                         * (half)(1.0 - saturate(diff / (_FoamWidth * 3.2))) * 0.35h;
                }

                // Shallow-water caustic web and sparkle, gated to where the sea floor is close.
                half caustic = 0.0, sparkle = 0.0;
                [branch] if (shallowMask > 0.0)
                {
                    half c1 = (half)sin(dot(wp, float2(0.83, 0.55)) * 4.0 + t * 1.3 + n1 * 3.5);
                    half c2 = (half)sin(dot(wp, float2(-0.6, 0.8)) * 4.6 - t * 1.1 + n2 * 3.5);
                    caustic = smoothstep(0.55h, 1.0h, c1 * c2) * (half)_Caustic * shallowMask * (1.0h - storm);
                    sparkle = smoothstep(0.9h, 1.0h, n2) * (half)_Sparkle * shallowMask * (half)saturate(nh * 2.0);
                }

                float wakeFoam, bowFoam, churnTint;
                PlayerWake(wp, coastD, coastN, coastFade, churn, n1, wakeFoam, bowFoam, churnTint);
                // Speed you can see: the bow throws more water and the wake churns harder the faster you go.
                float driveBoost = _SpeedFeelGains.y * _SpeedFeel.x;
                bowFoam *= 1.0 + driveBoost;
                wakeFoam *= 1.0 + 0.5 * driveBoost;
                churnTint *= 1.0 + 0.6 * driveBoost;
                float impact = ImpactRing(wp, _RingPos.xy, _RingAge.x, _RingStrength.x, churn)
                             + ImpactRing(wp, _RingPos.zw, _RingAge.y, _RingStrength.y, churn)
                             + SplashRing(wp) * (0.4 + 0.5 * churn)
                             + SprayRing(wp, churn);

                // Wind streaks and whitecaps both scale with storm: in calm weather (most pixels, most of the time) the
                // noise behind them is skipped instead of multiplied by zero. Inside a storm each mask noise only runs
                // where its term can show: the cap mask on a breaking crest (most storm pixels have none), the streaks
                // (their mask plus four more noises) inside their patches, once the storm is past 0.15 (below that they
                // changed the colour by under 2 %) and within 90 u of the camera (further out their thin lines only
                // shimmered).
                half streaks = 0.0, whitecap = 0.0;
                [branch] if (storm > 0.001)
                {
                    half capTex = n2 * (0.45h + 0.55h * n1);
                    half crest = smoothstep(0.62h, 0.95h, saturate(h * 0.5h + 0.5h) * (0.35h + 0.9h * capTex));
                    [branch] if (crest > 0.0)
                    {
                        half capMask = smoothstep(0.35h, 0.8h, (half)DriftNoiseStable(wp * 0.11 + wind * (_Time.y * 0.08)));
                        whitecap = crest * storm * 0.4h * capMask;
                    }
                    half streakFade = (half)(saturate((storm - 0.15) * 10.0) * saturate((90.0 - camDist) / 25.0));
                    [branch] if (streakFade > 0.0)
                    {
                        half streakMask = smoothstep(0.48h, 0.8h, (half)DriftNoiseStable(wp * 0.045 + wind * (_Time.y * 0.02)));
                        [branch] if (streakMask > 0.0)
                            streaks = (half)WindStreaks(wp, wind, storm, streakMask) * streakFade;
                    }
                }
                // Emphasis along the shore, not in a circle: _CurrentEmphasis.x is a distance from the coastline.
                half nearPlayer = (half)((1.0 - smoothstep(0.35, 1.0, coastD / max(_CurrentEmphasis.x, 1e-3))) * coastFade);
                // Surfing a plate boundary lights the seam up, so gaining and losing it is unmistakable.
                half surfFoam = smoothstep(0.22h, 0.8h, curEdge * curEdge * (half)_SpeedFeel.z * (0.3h + 1.2h * churn))
                              * (half)_SpeedFeelGains.w * (0.2h + 0.8h * nearPlayer) * 0.7h;
                half speedLines = (half)saturate(SpeedLines(wp, coastD, coastFade, fw));

                col *= lerp(half3(1, 1, 1), (half3)_StormTint.rgb, storm);
                col = lerp(col, shallowCol, (half)saturate(churnTint));
                col = lerp(col, foamCol * 0.92h, streaks * 0.16h);
                col = lerp(col, foamCol * 0.98h, speedLines * 0.45h);
                half foamMask = saturate(foam + ring + (half)(wakeFoam + bowFoam + impact) + whitecap + surfFoam);
                col = lerp(col, foamCol, foamMask);
                col += (sparkle + caustic * 0.35h) * foamCol;
                // Almost fully hazed, the cloud shadow changes the result by well under 1/255: skipped there. The sea
                // reads it from the cloud shadow texture (Drift.Visuals.CloudShadowTexture) instead of hashing clumps.
                [branch] if (haze < 0.985) col *= (half)CloudShadowTex(wp);

                half alpha = lerp((half)_MinAlpha, 0.97h, pow(depthFade, 0.6h));
                alpha = max(alpha, max(foamMask, max(streaks * 0.3h, speedLines * 0.45h)));
                col = lerp(col, (half3)_CurveFogColor.rgb, (half)haze);
                alpha = max(alpha, (half)haze);
                // Nothing opaque behind the sea: the sky dome is drawn after the water now and skips every sea pixel, so
                // the colour it showed there (below the limb it is exactly _CurveFogColor) is blended in here.
            #if UNITY_REVERSED_Z
                bool open = rawDepth <= 0.0;
            #else
                bool open = rawDepth >= 1.0;
            #endif
                if (open && _CurveSkyDraw > 0.5)
                {
                    // DriftSky's base gradient for this view ray: exactly _CurveFogColor below the limb (the whole
                    // globe), the gradient where the adventure ring's band climbs into the sky (no sun glow, clouds or
                    // stars: they would change the 3 % that shows through by next to nothing).
                    float3 ray = normalize(IN.bentWS - GetCameraPositionWS());
                    float up = _CurveSkyCenter.w - dot(ray, _CurveSkyCenter.xyz);
                    float q = 1.0 / (1.0 + _SkyStarParams.w * max(up, 0.0));
                    half3 behind = (half3)lerp(_CurveFogColor.rgb, _CurveSkyZenith.rgb, 1.0 - q * q);
                    col = lerp(behind, col, alpha);
                    alpha = 1.0;
                }
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
