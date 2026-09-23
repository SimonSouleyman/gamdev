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
            // Island outline for the wake, all pushed by WaterFeedback:
            //   _PlayerWakeEdge = widest outline points across the velocity: (along, across) of the +perp side, then of the -perp side
            //   _PlayerWake     = (churn reach from the shore, V line length, cutoff range from the island centre [0 = no wake], foam band scale)
            float4 _PlayerWakeEdge;
            float4 _PlayerWake;
            float4 _RingPos;        // xy = ring 0 centre, zw = ring 1 centre
            float4 _RingAge;        // x, y = ring ages in seconds
            float4 _RingStrength;   // x, y = ring strengths (0 = inactive)
            float4 _CurrentDir;     // xy = normalized plate current direction
            float  _CurrentSpeed;
            float  _CurrentFoam;    // current fleck strength knob (WaterFeedback.currentStreakStrength)
            // Plate currents around the view (Drift.Visuals.CurrentField): rg = velocity / max * 0.5 + 0.5,
            // b = closeness to a plate boundary, a = closing speed there. Params: xy = world origin, z = 1 / size,
            // w = max speed (0 = no field: no flecks).
            TEXTURE2D(_CurrentField);
            SAMPLER(sampler_CurrentField);
            float4 _CurrentFieldParams;
            float4 _CurrentEmphasis;  // x = distance from the coastline, y = extra strength (carry), z = carry against steering
            // Drift.Islands.SpeedFeel through WaterFeedback: x = drive (0 = idling, 1 = own top speed, up to 1.4 when
            // the water carries), y = flow build-up while travelling with the current, z = surf on a plate boundary,
            // w = the island's speed in u/s. Gains: x = speed lines, y = bow/wake boost, z = flow, w = surf. All zero
            // (the default and "Tempogefühl aus") leaves the water exactly as it was.
            float4 _SpeedFeel;
            float4 _SpeedFeelGains;
            float4 _SprayPos;       // xy = merge contact, z = age, w = strength
            float  _Storm;          // 0..1 storm intensity at the player
            // Every visible storm (Drift.Visuals.StormVisuals): xy centre, z radius, w intensity. Unset = no storms.
            float4 _DriftStorms[4];
            float  _DriftStormCount;
            float4 _WindDir;        // xy = normalized wind (current + global wind), z = wind speed
            float4 _SplashPos;      // xy = fish splash centre, z = age, w = strength
            // The player island's real coastline (Drift.Visuals.CoastField): a small signed-distance field in
            // the island's BODY space. r = distance to the waterline in world units (negative on land),
            // gb = outward normal there. Everything the water draws around the player reads this - a bounding
            // circle or the radial profile that came before fills the notches of a branched island and turns
            // the wake into a white box with straight edges.
            TEXTURE2D(_CoastField);
            SAMPLER(sampler_CoastField);
            float4 _CoastParams;    // xy = field origin in body space, z = 1 / size, w = 1 when the field is valid
            float4 _CoastRot;       // xy = (cos, sin) of the island's yaw (world -> body)

            // Distance to the player island's shore with its outward normal (world space). `fade` goes to 0 at
            // the field's own border, so nothing anchored to it can ever draw a straight edge there.
            float CoastDistance(float2 wp, out float2 nrm, out float fade)
            {
                nrm = float2(0.0, 1.0);
                fade = 0.0;
                if (_CoastParams.w <= 0.0) return 1e4;
                float2 rel = wp - _PlayerPos.xy;
                float2 lp = float2(rel.x * _CoastRot.x - rel.y * _CoastRot.y, rel.x * _CoastRot.y + rel.y * _CoastRot.x);
                float2 uv = (lp - _CoastParams.xy) * _CoastParams.z;
                float2 e = saturate(min(uv, 1.0 - uv) * 14.0);
                fade = e.x * e.y;
                if (fade <= 0.0) return 1e4;
                float4 s = SAMPLE_TEXTURE2D_LOD(_CoastField, sampler_CoastField, uv, 0);
                float2 nb = s.gb;
                nb = dot(nb, nb) > 1e-6 ? normalize(nb) : float2(0.0, 1.0);
                nrm = float2(nb.x * _CoastRot.x + nb.y * _CoastRot.y, -nb.x * _CoastRot.y + nb.y * _CoastRot.x);
                return s.r;
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

            // Stretched noise without its lattice: a value noise sampled on a squashed grid shows its cells as a
            // plaid of rectangles, so the two octaves sit on grids ROTATED against the stretch and are warped by
            // a slow isotropic noise. Used wherever foam is drawn out along a direction.
            float StretchedNoise(float2 sp, float2 wp)
            {
                float warp = DriftNoise(wp * 0.11 + 7.1) - 0.5;
                float2 r1 = float2(sp.x * 0.819 - sp.y * 0.574, sp.x * 0.574 + sp.y * 0.819) + warp * 0.9;
                float2 r2 = float2(sp.x * 0.545 + sp.y * 0.839, -sp.x * 0.839 + sp.y * 0.545) * 1.7 + 0.5 - warp * 1.3;
                return DriftNoise(r1) * 0.55 + DriftNoise(r2) * 0.45;
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
                float streak = DriftNoise(float2(sp.x * 0.819 - sp.y * 0.574, sp.x * 0.574 + sp.y * 0.819));
                float wake = astern * (1.0 - tailN) * (1.0 - tailN) * speedF * coastFade;

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
                float d = length(wp - c);
                float r = age * 6.0;
                float w = 0.3 + 0.25 * age;
                float band = exp(-(d - r) * (d - r) / (w * w));
                float inner = saturate(1.0 - d / max(r, 0.01)) * saturate(1.0 - age / 0.6) * 0.5;
                float life = saturate(1.0 - age / 2.5);
                return (band + inner) * life * strength * (0.25 + 0.5 * churn) * step(0.0, age);
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
                float d = length(rel) + (DriftNoise(rel * 0.55 + 5.3) - 0.5) * (1.5 + 3.0 * age);
                float r = age * (12.0 + 16.0 * st);
                float w = 0.6 + 2.6 * age;
                float ring = exp(-(d - r) * (d - r) / (w * w));
                float speck = smoothstep(0.3, 0.92, DriftNoise(wp * 2.1 + 17.0) * 0.6 + churn * 0.4);
                return ring * life * life * st * (0.35 + 1.0 * speck);
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
                float s1 = StretchedNoise(sp, wp);
                float s2 = DriftNoise(wp * 0.3 + wind * (_Time.y * 0.25) + 13.7);
                float streak = smoothstep(0.42, 0.95, s1 * 0.6 + s2 * 0.4);
                // Storm-only now: the plate current has its own drifting flecks (CurrentFlecks), and wind-aligned
                // streaks that also grew with the current pointed the wrong way whenever wind and current disagreed.
                float strength = 0.63 * storm;
                return streak * mask * saturate(strength);
            }

            // One foam fleck per cell of q (cell units): a short comet whose round head leads along the current and
            // whose tail thins out behind it, so even a still frame shows which way the water goes. hl = half length.
            // The centre keeps 0.3 cells from the cell edge and the fleck stays within that, so one cell is enough.
            float Fleck(float2 q, float2 dir, float hl, float seed, float ph, float density, float aa)
            {
                float2 id = floor(q);
                float2 f = q - id;
                float2 hs = id + seed;
                float h1 = DriftHash(hs);
                float h2 = DriftHash(hs + float2(7.31, 1.93));
                float h3 = DriftHash(hs + float2(2.17, 9.41));
                float2 d = f - (0.3 + 0.4 * float2(h1, h2));
                float a = dot(d, dir);
                float b = dot(d, float2(-dir.y, dir.x));
                float len = hl * (0.6 + 0.8 * h3);
                float ca = clamp(a, -len, len);
                float taper = saturate((len - ca) / max(2.0 * len, 1e-4));
                float r = 0.045 * (1.0 - 0.7 * taper);
                float dist = length(float2(a - ca, b));
                float shape = 1.0 - smoothstep(r * 0.3 - aa, r + aa, dist);
                // Each fleck lives for its own random window inside the phase, so they fade in and out one by one.
                float start = h1 * 0.45;
                float life = sin(3.14159 * saturate((ph - start) / 0.55));
                return shape * life * step(h2 * 0.7 + h3 * 0.3, density) * (0.6 + 0.4 * h3);
            }

            // Foam flecks carried by the plate current under them: a two-phase flow map (each phase restarts its flecks
            // at fresh random spots while they are invisible) at two cell sizes picked by camera distance, so the
            // flecks keep roughly the same size on screen from the chase view to fully zoomed out.
            float CurrentFlecks(float2 wp, float camDist, out float edge)
            {
                edge = 0.0;
                if (_CurrentFieldParams.w <= 0.0) return 0.0;
                float2 uv = (wp - _CurrentFieldParams.xy) * _CurrentFieldParams.z;
                float2 inside = saturate(min(uv, 1.0 - uv) * 12.0);
                float fade = inside.x * inside.y;
                if (fade <= 0.0) return 0.0;
                float4 fld = SAMPLE_TEXTURE2D_LOD(_CurrentField, sampler_CurrentField, uv, 0);
                float2 vel = (fld.rg * 2.0 - 1.0) * _CurrentFieldParams.w;
                edge = fld.b * fade;
                float speed = length(vel);
                float2 dir = speed > 1e-3 ? vel / speed : float2(1.0, 0.0);
                float speedN = saturate(speed / 3.0);
                // Riding with the current stretches the comets: the longer you stay in the stream, the more the
                // water reads as streaming past you.
                float hl = 0.03 + 0.15 * speedN + 0.12 * _SpeedFeel.y * _SpeedFeelGains.z;
                // Converging plates bunch the flecks up along the seam, parting ones leave it calmer.
                float closing = fld.a * 2.0 - 1.0;
                float density = 0.4 + 0.25 * edge * (1.0 + 2.0 * saturate(closing * 3.0));

                float L = log2(max(camDist, 1.0) / 12.0);
                float lv = floor(L);
                float blend = L - lv;
                float res = 0.0;
                [unroll]
                for (int layer = 0; layer < 2; layer++)
                {
                    float cell = 2.0 * exp2(lv + layer);
                    float w = layer == 0 ? 1.0 - blend : blend;
                    float T = 2.2 + 0.08 * cell;
                    float aa = 0.7 * max(fwidth(wp.x), fwidth(wp.y)) / cell + 0.008;
                    [unroll]
                    for (int k = 0; k < 2; k++)
                    {
                        float tt = _Time.y / T + 0.5 * k;
                        float ph = frac(tt);
                        float cyc = frac(floor(tt) * 0.618034) * 89.0 + 31.0 * k + 17.0 * layer;
                        float2 q = (wp - vel * (T * ph)) / cell;
                        res += w * Fleck(q, dir, hl, cyc, ph, density, aa);
                    }
                }
                return saturate(res) * fade * (0.35 + 0.65 * speedN);
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

                float2 wp = IN.positionWS.xz;
                float2 coastN;
                float coastFade;
                float coastD = CoastDistance(wp, coastN, coastFade);
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

                // Tint and shore foam come from the camera depth texture, which is the real beach outline of
                // EVERY island. No analytic island distance is used for them: a circle on the bounding radius
                // once produced a turquoise disc three times the size of the island.
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
                // noise behind them is skipped instead of multiplied by zero.
                float streaks = 0.0, whitecap = 0.0;
                float capTex = n2 * (0.45 + 0.55 * n1);
                [branch] if (storm > 0.001)
                {
                    float streakMask = smoothstep(0.48, 0.8, DriftNoise(wp * 0.045 + wind * (_Time.y * 0.02)));
                    streaks = WindStreaks(wp, wind, storm, streakMask);
                    float capMask = smoothstep(0.35, 0.8, DriftNoise(wp * 0.11 + wind * (_Time.y * 0.08)));
                    whitecap = smoothstep(0.62, 0.95, saturate(h * 0.5 + 0.5) * (0.35 + 0.9 * capTex)) * storm * 0.4 * capMask;
                }
                float curEdge;
                float flecks = CurrentFlecks(wp, camDist, curEdge);
                // Emphasis along the shore, not in a circle: _CurrentEmphasis.x is a distance from the coastline.
                float nearPlayer = (1.0 - smoothstep(0.35, 1.0, coastD / max(_CurrentEmphasis.x, 1e-3))) * coastFade;
                flecks *= _CurrentFoam * (1.0 + _CurrentEmphasis.y * nearPlayer) * (1.0 + 0.4 * curEdge) * (1.0 - 0.6 * storm);
                // Water highway: the longer the island travels with the current, the brighter the flecks around it.
                flecks *= 1.0 + _SpeedFeelGains.z * _SpeedFeel.y * (0.35 + 0.95 * nearPlayer);
                flecks = saturate(flecks);
                // Surfing a plate boundary lights the seam up, so gaining and losing it is unmistakable.
                float surfFoam = smoothstep(0.22, 0.8, curEdge * curEdge * _SpeedFeel.z * (0.3 + 1.2 * churn))
                               * _SpeedFeelGains.w * (0.2 + 0.8 * nearPlayer) * 0.7;
                float speedLines = SpeedLines(wp, coastD, coastFade, max(fwidth(wp.x), fwidth(wp.y)));

                col *= lerp(1.0, _StormTint.rgb, storm);
                col = lerp(col, _ShallowColor.rgb, saturate(churnTint));
                col = lerp(col, _FoamColor.rgb * 0.92, streaks * 0.16);
                col = lerp(col, _FoamColor.rgb * 0.95, flecks * 0.5);
                col = lerp(col, _FoamColor.rgb * 0.98, saturate(speedLines) * 0.45);
                float foamMask = saturate(foam + ring + wakeFoam + bowFoam + impact + whitecap + surfFoam);
                col = lerp(col, _FoamColor.rgb, foamMask);
                col += (sparkle + caustic * 0.35) * _FoamColor.rgb;
                col *= CloudShadow(wp);

                float alpha = lerp(_MinAlpha, 0.97, pow(depthFade, 0.6));
                alpha = max(alpha, max(foamMask, max(streaks * 0.3, max(flecks * 0.5, saturate(speedLines) * 0.45))));
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
