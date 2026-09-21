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
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _FoamColor, _SkyColor, _StormTint;
                float _DepthRange, _ShallowWidth, _FoamWidth, _WaveScale, _WaveSpeed, _WaveNormalScale;
                float _DetailScale, _DetailStrength, _StormWaveBoost;
                float _Specular, _SpecPower, _Fresnel, _Sparkle, _Caustic, _MinAlpha;
            CBUFFER_END

            // Per-frame feedback uniforms, pushed by Drift.Visuals.WaterFeedback through a
            // MaterialPropertyBlock. Deliberately not material Properties so the .mat asset never
            // serializes them and everything below degrades to nothing when they are left at zero.
            float4 _PlayerPos;      // xy = player island world xz
            float4 _PlayerVel;      // xy = player planar velocity
            float  _PlayerRadius;
            // Island outline for the wake, all pushed by WaterFeedback:
            //   _PlayerReach[k] = (land reach along body angle k * 360/32 [0 = body forward, clockwise], d reach / d angle, 0, 0)
            //   _PlayerBodyYaw  = heading yaw + body yaw in radians (body -> world)
            //   _PlayerWakeEdge = widest outline points across the velocity: (along, across) of the +perp side, then of the -perp side
            //   _PlayerWake     = (wake length, V line length, cutoff range from the island centre [0 = no wake], foam band scale)
            float4 _PlayerReach[32];
            float  _PlayerBodyYaw;
            float4 _PlayerWakeEdge;
            float4 _PlayerWake;
            float4 _RingPos;        // xy = ring 0 centre, zw = ring 1 centre
            float4 _RingAge;        // x, y = ring ages in seconds
            float4 _RingStrength;   // x, y = ring strengths (0 = inactive)
            float4 _CurrentDir;     // xy = normalized plate current direction
            float  _CurrentSpeed;
            float  _CurrentFoam;    // streak strength knob (WaterFeedback.currentStreakStrength)
            float  _Storm;          // 0..1 storm intensity at the player
            // Every visible storm (Drift.Visuals.StormVisuals): xy centre, z radius, w intensity. Unset = no storms.
            float4 _DriftStorms[4];
            float  _DriftStormCount;
            float4 _WindDir;        // xy = normalized wind (current + global wind), z = wind speed
            float4 _SplashPos;      // xy = fish splash centre, z = age, w = strength
            // Nearest islands (xy = centre, z = bounding radius, w = 1 when used). The camera depth
            // texture is not reliably available to transparents on every path, so shallow tint and
            // shore foam also come from this analytic distance and the two are combined.
            float4 _IslandData[8];

            float ShoreDistance(float2 wp)
            {
                float best = 1e4;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float4 d = _IslandData[i];
                    float sd = length(wp - d.xy) - d.z;
                    best = min(best, lerp(1e4, sd, saturate(d.w)));
                }
                return best;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            // Sum of sines with sharpened (gerstner-like) crests: h = a * (2e^2 - 1), e = 0.5 + 0.5 sin.
            // Returns the height gradient (for the normal) and the normalized height (-1..1).
            void Waves(float2 p, float t, float storm, float2 wind, out float2 grad, out float height)
            {
                const float2 dirs[5] = { float2(0.914, 0.406), float2(-0.6, 0.8), float2(0.301, -0.954), float2(-0.862, -0.507), float2(0.707, 0.707) };
                const float freq[5] = { 0.7, 1.1, 1.6, 2.4, 3.6 };
                const float amp[5]  = { 0.5, 0.35, 0.22, 0.14, 0.08 };
                const float spd[5]  = { 0.9, 1.15, 1.5, 1.9, 2.4 };
                float scale = _WaveScale * 2.0 * (1.0 + 0.3 * storm);
                float ampMul = 1.0 + _StormWaveBoost * storm;
                grad = 0;
                height = 0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float2 d = dirs[i];
                    float k = freq[i] * scale;
                    float a = amp[i] * ampMul;
                    float s, c;
                    sincos(dot(d, p) * k + t * spd[i], s, c);
                    float e = s * 0.5 + 0.5;
                    height += a * (2.0 * e * e - 1.0);
                    grad += d * (k * a * 2.0 * e * c);
                }
                // Storm chop travelling with the wind so gusts and waves agree on a direction.
                {
                    float k = 2.2 * scale;
                    float a = 0.45 * storm;
                    float s, c;
                    sincos(dot(wind, p) * k + t * 2.6, s, c);
                    float e = s * 0.5 + 0.5;
                    height += a * (2.0 * e * e - 1.0);
                    grad += wind * (k * a * 2.0 * e * c);
                }
                height /= 1.29 + 0.45 * storm;
            }

            // Fine ripples: three short sines, one aligned with the wind, only affecting the normal.
            float2 Detail(float2 p, float t, float storm, float2 wind)
            {
                float2 g = 0;
                float2 d0 = wind;
                float2 d1 = float2(-0.5, 0.866);
                float2 d2 = float2(0.3, -0.954);
                float k = _DetailScale;
                float a = _DetailStrength * (1.0 + 1.5 * storm);
                g += d0 * (a * cos(dot(d0, p) * k + t * 3.0));
                g += d1 * (a * 0.7 * cos(dot(d1, p) * k * 1.3 + t * 3.7));
                g += d2 * (a * 0.5 * cos(dot(d2, p) * k * 1.7 - t * 4.3));
                return g;
            }

            // atan2(y, x) within 1e-4 rad, about half the cost of the intrinsic on mobile.
            float DriftAtan2(float y, float x)
            {
                float ax = abs(x), ay = abs(y);
                float a = min(ax, ay) / max(max(ax, ay), 1e-5);
                float q = a * a;
                float r = ((-0.0464964749 * q + 0.15931422) * q - 0.327622764) * q * a + a;
                r = ay > ax ? 1.57079637 - r : r;
                r = x < 0.0 ? 3.14159274 - r : r;
                return y < 0.0 ? -r : r;
            }

            // Wake around the island's real outline. The outline is the 32-entry radial land profile: the pixel's
            // body angle picks two entries (reach and its angular derivative, both interpolated, so the outline
            // and its normal are continuous). Bow foam hugs the outline where its normal faces the velocity; the
            // turbulent wake fills the silhouette's shadow behind the line through the two widest points and fades
            // with the distance to the trailing outline (so it follows notches and the stern's shape); a V line
            // leaves each widest point. Everything scales with speed and is skipped outside _PlayerWake.z.
            void PlayerWake(float2 wp, float churn, float n1, out float wakeFoam, out float bowFoam, out float churnTint)
            {
                wakeFoam = 0; bowFoam = 0; churnTint = 0;
                float2 rel = wp - _PlayerPos.xy;
                float d2 = dot(rel, rel);
                if (d2 >= _PlayerWake.z * _PlayerWake.z) return;

                float speed = length(_PlayerVel.xy);
                float2 dir = _PlayerVel.xy / max(speed, 1e-4);
                float speedF = saturate(speed / 3.0);
                float along = dot(rel, dir);
                float across = dot(rel, float2(-dir.y, dir.x));
                float d = sqrt(d2);
                float2 rHat = rel / max(d, 1e-3);

                float u = frac((DriftAtan2(rel.x, rel.y) - _PlayerBodyYaw) * 0.159154943 + 1.0) * 32.0;
                float i0 = floor(u);
                float4 e = lerp(_PlayerReach[(int)i0 & 31], _PlayerReach[((int)i0 + 1) & 31], u - i0);
                float2 nrm = normalize(e.x * rHat - e.y * float2(rHat.y, -rHat.x));
                float sd = (d - e.x) * dot(nrm, rHat);
                float facing = dot(nrm, dir);

                float lead = saturate(facing);
                float band = _PlayerWake.w;
                float shell = 1.0 - saturate(abs(sd - (0.15 + 0.3 * speedF) * band) / ((0.3 + 0.6 * speedF) * band));
                float bow = shell * lead * sqrt(lead) * speedF;

                float aL = _PlayerWakeEdge.x, cL = _PlayerWakeEdge.y, aR = _PlayerWakeEdge.z, cR = _PlayerWakeEdge.w;
                float width = max(cL - cR, 0.5);
                float side = saturate((across - cR) / width);
                float back = lerp(aR, aL, side) - along;
                float tailN = saturate(sd / _PlayerWake.x);
                float jitter = (churn - 0.5) * (0.35 * width);
                float pinch = 0.2 * width * tailN;
                float inside = smoothstep(cR + pinch - 0.2 * width, cR + pinch + 0.1 * width, across + jitter)
                             * (1.0 - smoothstep(cL - pinch - 0.1 * width, cL - pinch + 0.2 * width, across + jitter));
                float behind = smoothstep(0.0, 0.25 * width + 0.5, back);
                float streak = DriftNoise(float2(along * 0.3 + _Time.y * 0.5, across * 1.9));
                float wake = behind * inside * (1.0 - tailN) * (1.0 - tailN) * step(0.0, sd) * speedF;

                // The lines wander with the churn noise and widen as they fade, so they read as foam, not as rulers.
                float sL = aL - along, sR = aR - along;
                float wob = (churn - 0.5) * band;
                float fadeL = saturate(1.0 - sL / _PlayerWake.y), fadeR = saturate(1.0 - sR / _PlayerWake.y);
                float vL = (1.0 - saturate(abs(across - cL - 0.3 * sL + wob * (0.5 + 0.08 * sL)) / (band * (0.3 + 0.07 * sL)))) * saturate(sL * 1.5) * fadeL * sqrt(fadeL);
                float vR = (1.0 - saturate(abs(across - cR + 0.3 * sR + wob * (0.5 + 0.08 * sR)) / (band * (0.3 + 0.07 * sR)))) * saturate(sR * 1.5) * fadeR * sqrt(fadeR);
                float vee = max(vL, vR) * speedF * step(0.0, sd);

                wakeFoam = smoothstep(0.3, 0.85, wake * (0.2 + 0.65 * churn + 0.6 * streak))
                         + smoothstep(0.2, 0.75, vee * (0.35 + 1.0 * n1)) * 0.8;
                bowFoam = smoothstep(0.12, 0.6, bow * (0.5 + 0.8 * n1));
                churnTint = wake * 0.3;
            }

            float ImpactRing(float2 wp, float2 c, float age, float strength, float churn)
            {
                float d = length(wp - c);
                float r = age * 6.0;
                float w = 0.3 + 0.25 * age;
                float band = exp(-(d - r) * (d - r) / (w * w));
                float inner = saturate(1.0 - d / max(r, 0.01)) * saturate(1.0 - age / 0.6) * 0.5;
                float life = saturate(1.0 - age / 2.5);
                return (band + inner) * life * strength * (0.25 + 0.5 * churn) * step(0.0, age);
            }

            // Tiny ring left by a jumping fish: 0.15 u -> ~1.1 u over 0.8 s.
            float SplashRing(float2 wp)
            {
                float age = _SplashPos.z;
                float d = length(wp - _SplashPos.xy);
                float r = 0.15 + age * 1.2;
                float w = 0.08 + 0.12 * age;
                float band = exp(-(d - r) * (d - r) / (w * w));
                float life = saturate(1.0 - age / 0.8);
                return band * life * life * _SplashPos.w;
            }

            // Soft, wide, low-contrast foam streaks stretched along the wind. Elongation comes from
            // sampling the noise with a very short scale along the wind and a long one across it.
            float WindStreaks(float2 wp, float2 wind, float storm, float mask)
            {
                float u = dot(wp, wind);
                float v = dot(wp, float2(-wind.y, wind.x));
                float scroll = 0.35 + 0.25 * _CurrentSpeed + 1.2 * storm;
                float2 sp = float2((u - _Time.y * scroll) * 0.075, v * 0.45);
                // A stretched value noise shows its lattice as a plaid of rectangles. The two octaves are
                // therefore sampled on lattices ROTATED against the stretch axis (different angles) and
                // warped by a slow isotropic noise, so no straight cell edges survive; the threshold is
                // wide so the streaks stay soft.
                float warp = DriftNoise(wp * 0.11 + 7.1) - 0.5;
                float2 r1 = float2(sp.x * 0.819 - sp.y * 0.574, sp.x * 0.574 + sp.y * 0.819) + warp * 0.9;
                float2 r2 = float2(sp.x * 0.545 + sp.y * 0.839, -sp.x * 0.839 + sp.y * 0.545) * 1.7 + 0.5 - warp * 1.3;
                float s1 = DriftNoise(r1) * 0.55 + DriftNoise(r2) * 0.45;
                float s2 = DriftNoise(wp * 0.3 + wind * (_Time.y * 0.25) + 13.7);
                float streak = smoothstep(0.42, 0.95, s1 * 0.6 + s2 * 0.4);
                float curF = saturate((_CurrentSpeed - 0.3) / 3.0);
                float strength = _CurrentFoam * (0.9 * curF + 1.8 * storm);
                return streak * mask * saturate(strength);
            }

            // The profile of StormSystem.IntensityAt (full strength over the inner 55 %, easing out to the rim) with
            // its rim pushed in and out by noise, so the rough sea is a ragged patch under the cloud cluster, not a disc.
            float StormField(float2 wp)
            {
                float storm = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    if (i >= (int)_DriftStormCount) break;
                    float4 s = _DriftStorms[i];
                    float d = length(wp - s.xy) / max(s.z, 1e-3);
                    d *= 0.82 + 0.36 * DriftNoise(wp * 0.05 + s.xy * 0.013);
                    float k = 1.0 - smoothstep(0.55, 1.0, d);
                    storm = max(storm, s.w * k);
                }
                return storm;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfDepth = IN.screenPos.w;
                float diff = max(0, sceneDepth - surfDepth);
                float shore = ShoreDistance(IN.positionWS.xz);

                float2 wp = IN.positionWS.xz;
                // Rough dark water under every storm, not just the one the player is in, so a storm is seen coming.
                float storm = saturate(max(_Storm, StormField(wp)));
                float t = _Time.y * _WaveSpeed;
                float2 wind = normalize(_WindDir.xy + float2(1e-4, 0));

                float2 g;
                float h;
                Waves(wp, t, storm, wind, g, h);
                // Fine sines alias into a moiré lattice when the camera is far: fade the detail and
                // flatten the main waves with distance instead of letting them shimmer.
                float camDist = length(GetCameraPositionWS() - IN.positionWS);
                float lod = saturate(1.0 - (camDist - 12.0) / 30.0);
                // The curved world shows the sea from 100 u and more, where even the flattened waves and their glints
                // line up into a grid: a second, slower fade takes them down to a calm sheen.
                float farLod = 1.0 - 0.85 * saturate((camDist - 40.0) / 80.0);
                g = g * _WaveNormalScale * lerp(0.3, 1.0, lod) * farLod + Detail(wp, t, storm, wind) * lod;
                float3 n = normalize(float3(-g.x, 1.0, -g.y));

                float n1 = DriftNoise(wp * 1.7 + t * 0.6);
                float n2 = DriftNoise(wp * 3.1 - t * 0.4);
                float churn = n1 * 0.6 + n2 * 0.4;

                // The analytic shore is a circle around a blob-shaped island, so its edge is pushed
                // around by noise and never reaches the full shallow colour; the depth path (where
                // the pipeline provides it) still can.
                // The depth path draws the real outline. The analytic circle (bounding radius, far
                // wider than the beach) is NOT used for the tint: it produced a turquoise disc 3x the
                // island. `shore` only feeds the surf foam below.
                float depthFade = saturate(diff / _DepthRange);
                float shallowMask = 1.0 - smoothstep(0.15, 0.9, depthFade);
                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, pow(depthFade, 0.7));
                col *= 1.0 + h * (0.05 + 0.05 * storm) * farLod;

                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                Light l = GetMainLight();
                float3 H = normalize(l.direction + V);
                // Glints from a calmed normal too: the broad lobe on the full wave normal lit every crest facing the sun
                // and drew a lattice of white bands across the whole sea.
                float3 nSpec = normalize(float3(-g.x * 0.35, 1.0, -g.y * 0.35));
                float nh = saturate(dot(nSpec, H));
                float spec = pow(nh, _SpecPower) * _Specular * 0.55 + pow(nh, 12.0) * 0.03 * _Specular;
                spec *= saturate(l.direction.y * 4.0) * (1.0 - 0.5 * storm) * farLod * farLod;
                // Sky reflection from a calmed normal: with the full wave normal every crest facing away from the camera
                // turned into a long white streak across the sea, which read as stripy clouds.
                float3 nSky = normalize(float3(-g.x * 0.25, 1.0, -g.y * 0.25));
                float fres = pow(1.0 - saturate(dot(nSky, V)), 4.0);
                col = lerp(col, _SkyColor.rgb, fres * _Fresnel);
                col += l.color * spec;

                // Shore foam that surges and breaks up over time instead of a fixed rim.
                float surge = 0.5 + 0.5 * sin(t * 0.9 + diff * 5.0 + n2 * 3.0);
                // Depth only: the analytic circle drew a white disc around every elongated island.
                float foamBase = 1.0 - saturate(diff / _FoamWidth);
                float foam = smoothstep(0.5, 0.9, foamBase * (0.3 + 0.5 * n1 + 0.35 * surge)) * (0.45 + 0.4 * n2);
                float ring = smoothstep(0.85, 1.0, sin(diff * 7.0 - _Time.y * 1.6 + n2 * 2.0) * 0.5 + 0.5)
                             * (1.0 - saturate(diff / (_FoamWidth * 3.2))) * 0.35;

                // Shallow-water caustic web and sparkle, gated to where the sea floor is close.
                float c1 = sin(dot(wp, float2(0.83, 0.55)) * 4.0 + t * 1.3 + n1 * 3.5);
                float c2 = sin(dot(wp, float2(-0.6, 0.8)) * 4.6 - t * 1.1 + n2 * 3.5);
                float caustic = smoothstep(0.55, 1.0, c1 * c2) * _Caustic * shallowMask * (1.0 - storm);
                float sparkle = smoothstep(0.9, 1.0, n2) * _Sparkle * shallowMask * saturate(nh * 2.0);

                float wakeFoam, bowFoam, churnTint;
                PlayerWake(wp, churn, n1, wakeFoam, bowFoam, churnTint);
                float impact = ImpactRing(wp, _RingPos.xy, _RingAge.x, _RingStrength.x, churn)
                             + ImpactRing(wp, _RingPos.zw, _RingAge.y, _RingStrength.y, churn)
                             + SplashRing(wp) * (0.4 + 0.5 * churn);

                float streakMask = smoothstep(0.48, 0.8, DriftNoise(wp * 0.045 + wind * (_Time.y * 0.02)));
                float streaks = WindStreaks(wp, wind, storm, streakMask);
                float capTex = n2 * (0.45 + 0.55 * n1);
                // Broken up by low-frequency noise so the periodic wave crests don't read as a lattice.
                float capMask = smoothstep(0.35, 0.8, DriftNoise(wp * 0.11 + wind * (_Time.y * 0.08)));
                float whitecap = smoothstep(0.62, 0.95, saturate(h * 0.5 + 0.5) * (0.35 + 0.9 * capTex)) * storm * 0.4 * capMask;

                col *= lerp(1.0, _StormTint.rgb, storm);
                col = lerp(col, _ShallowColor.rgb, saturate(churnTint));
                col = lerp(col, _FoamColor.rgb * 0.92, streaks * 0.16);
                float foamMask = saturate(foam + ring + wakeFoam + bowFoam + impact + whitecap);
                col = lerp(col, _FoamColor.rgb, foamMask);
                col += (sparkle + caustic * 0.35) * _FoamColor.rgb;
                col *= CloudShadow(wp);

                float alpha = lerp(_MinAlpha, 0.97, pow(depthFade, 0.6));
                alpha = max(alpha, max(foamMask, streaks * 0.3));
                // The sea dissolves into the sky colour at the limb, so the horizon is closed whatever the mesh does there.
                float haze = DriftFogAmount(IN.positionWS);
                col = lerp(col, _CurveFogColor.rgb, haze);
                alpha = max(alpha, haze);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
