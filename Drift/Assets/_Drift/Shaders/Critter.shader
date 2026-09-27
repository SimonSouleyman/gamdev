Shader "Drift/Critter"
{
    // Drift/VertexColor plus the Phase 4 critter channels baked by TemplateBatch.AddCritter:
    //   UV0 = (wing tip offset from the body midline in xz, phase, glow 0/1)
    //   UV1 = (bob amplitude in world units, move id, move amplitude in world units, flap rate (0 = 1))
    //   UV2 = (offset from the part's pivot in xz, side, part weight)   (IslandCrittersSystem.CritterBatch)
    //   UV3 = procedural markings (Markings.CritterMarks, DriftMarkings.hlsl): crab speckle, turtle scutes, wings
    // Offsets are in the island's local frame and turned into world space here, so a turned island flaps right.
    // Special moves: 1 crab claw wave (claw tips rise and open, left and right alternating, the body bobs to the
    // beat), 2 turtle flipper sweep (flippers swing about their roots, rear pair half a beat behind, the body rocks).
    // Wings fold up about the midline by _FlapAngle (the tip moves in towards the body, so the flap is visible
    // from above), everything bobs by its amplitude, and glow vertices are drawn unlit with a slow blink
    // between _GlowMin and _GlowMax. Cull is off because wings, legs and claws are single triangles.
    //
    // The same shader is the additive GLOW material (_GlowMode 1, Blend One One, no depth write, transparent
    // queue; IslandCrittersSystem.GlowMaterial) for fireflies, their ground glow and the settlements' window
    // halos. Its meshes carry (IslandCrittersSystem.GlowBatch):
    //   POSITION = centre of a halo (all four corners) / the real vertex of a ground blob
    //   COLOR    = rgb emissive colour (HDR), a = radial falloff of a blob vertex (1 centre, 0 rim)
    //   UV0 = (corner x, corner y in -1..1, phase, kind: 2 halo billboard, 3 ground blob)
    //   UV1 = (bob amplitude, halo radius, drift radius, blink 0..1)
    //   UV2 = (rank 0..1, minimum angular radius (tan), 0, 0)
    // A halo is expanded camera-facing in the vertex shader AFTER the curve bend and never gets smaller on screen
    // than its minimum angular radius, so a swarm still twinkles from the chase camera; it drifts and bobs on the
    // GPU (the static swarm of the mid / far tier costs no CPU) and shows while rank < _FireflyAmount (per
    // renderer, MaterialPropertyBlock: dusk, dawn and storms thin the swarm out one by one). Emissive, unlit, no
    // cloud shadow; the haze only dims it.
    // Firefly light wave: _SyncWave = (origin x, z in the island's local frame, start time, duration) per renderer;
    // while it runs every halo and blob falls dark until a front (_SyncSpeed units/s) reaches it and then flashes
    // every _SyncPeriod s with its neighbours, so rings of light run out over the island. w 0 = off.
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 1
        _Ambient ("Ambient", Range(0,1)) = 0.45
        _FlapSpeed ("Flap Speed", Float) = 17
        _FlapAngle ("Flap Angle (deg)", Float) = 75
        _BobSpeed ("Bob Speed", Float) = 3.2
        _BlinkSpeed ("Blink Speed", Float) = 1.7
        _GlowMin ("Glow Min", Float) = 0.15
        _GlowMax ("Glow Max", Float) = 1.8
        _GlowMode ("Glow Mode (additive halos)", Float) = 0
        _FireflyAmount ("Firefly Amount", Range(0,1)) = 1
        _HaloStrength ("Halo Strength", Float) = 0.6
        _CoreStrength ("Core Strength", Float) = 1.6
        _CoreRadius ("Core Radius (of the halo)", Range(0.02,1)) = 0.22
        _BlobStrength ("Ground Glow Strength", Float) = 1
        _DriftSpeed ("Drift Speed", Float) = 0.35
        _WaveSpeed ("Claw Wave Speed", Float) = 7
        _SweepSpeed ("Flipper Sweep Speed", Float) = 4.5
        _SyncWave ("Firefly Wave (origin xz, start, duration)", Vector) = (0,0,-1000,0)
        _SyncSpeed ("Firefly Wave Front Speed", Float) = 2.2
        _SyncPeriod ("Firefly Wave Flash Period", Float) = 1.2
        _Markings ("Markings", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"
            #include "DriftMarkings.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 anim0      : TEXCOORD0;
                float4 anim1      : TEXCOORD1;
                float4 anim2      : TEXCOORD2;
                float4 mark       : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float2 glow        : TEXCOORD2;
                float3 halo        : TEXCOORD3;
                float4 mark        : TEXCOORD4;
                nointerpolation float2 markId : TEXCOORD5;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _LightAmount;
                float _Ambient;
                float _FlapSpeed;
                float _FlapAngle;
                float _BobSpeed;
                float _BlinkSpeed;
                float _GlowMin;
                float _GlowMax;
                float _GlowMode;
                float _FireflyAmount;
                float _HaloStrength;
                float _CoreStrength;
                float _CoreRadius;
                float _BlobStrength;
                float _DriftSpeed;
                float _WaveSpeed;
                float _SweepSpeed;
                float4 _SyncWave;
                float _SyncSpeed;
                float _SyncPeriod;
                float _Markings;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float phase = IN.anim0.z;
                float t = _Time.y;
                OUT.normalWS = float3(0, 1, 0);
                OUT.color = IN.color;
                OUT.halo = 0;
                float markId, markStrength, markSeed;
                DriftMarkDecode(IN.mark.w, markId, markStrength, markSeed);
                OUT.mark = float4(IN.mark.xyz, markStrength);
                OUT.markId = float2(_GlowMode > 0.5 ? 0.0 : markId, markSeed);

                if (_GlowMode > 0.5)
                {
                    float kind = IN.anim0.w;
                    float blinkWave = smoothstep(0.05, 0.75, sin(t * _BlinkSpeed + phase * 3.1) * 0.5 + 0.5);
                    float boost = 1.0;
                    if (_SyncWave.w > 0.0)
                    {
                        float age = t - _SyncWave.z;
                        float env = saturate(age * 2.0) * saturate((_SyncWave.w - age) * 1.5);
                        float lt = age - length(IN.positionOS.xz - _SyncWave.xy) / max(_SyncSpeed, 0.01);
                        float p = frac(lt / max(_SyncPeriod, 0.05));
                        float flash = lt < 0.0 ? 0.0 : smoothstep(0.0, 0.06, p) * (1.0 - smoothstep(0.12, 0.45, p));
                        blinkWave = lerp(blinkWave, flash, env);
                        boost = 1.0 + env * flash * 0.7;
                    }
                    float blink = lerp(1.0, lerp(_GlowMin, 1.0, blinkWave), max(IN.anim1.w, _SyncWave.w > 0.0 ? 0.6 : 0.0));
                    float vis = saturate((_FireflyAmount - IN.anim2.x) * 10.0);
                    float ds = t * _DriftSpeed;
                    posWS.xz += IN.anim1.z * 0.66 * float2(
                        sin(ds + phase * 1.7) + 0.5 * sin(ds * 2.3 + phase * 3.1),
                        cos(ds * 0.8 + phase * 2.3) + 0.5 * cos(ds * 1.9 + phase * 1.3));
                    posWS.y += sin(t * _BobSpeed * 0.5 + phase) * IN.anim1.x;
                    OUT.positionWS = posWS;
                    float3 bent = DriftCurveWS(posWS);
                    float intensity = blink * vis * boost;
                    if (kind < 2.5)
                    {
                        float3 toCam = _WorldSpaceCameraPos - bent;
                        float dist = max(length(toCam), 1e-3);
                        float radius = IN.anim1.y * lerp(0.7, 1.0, blink);
                        float shown = max(radius, dist * IN.anim2.y);
                        // A halo kept alive by its minimum size is dimmed a little, or a far archipelago would
                        // burn brighter than the island under the camera.
                        intensity *= lerp(1.0, 0.7, saturate(1.0 - radius / shown));
                        shown *= step(0.001, vis);
                        bent += toCam / dist * min(shown * 0.6, dist * 0.5);
                        bent += (UNITY_MATRIX_V[0].xyz * IN.anim0.x + UNITY_MATRIX_V[1].xyz * IN.anim0.y) * shown;
                    }
                    OUT.positionHCS = TransformWorldToHClip(bent);
                    OUT.glow = float2(kind, intensity);
                    OUT.halo = float3(IN.anim0.xy, IN.color.a);
                    return OUT;
                }

                float3x3 o2w = (float3x3)GetObjectToWorldMatrix();
                float2 tip = mul(o2w, float3(IN.anim0.x, 0, IN.anim0.y)).xz;
                float glow = IN.anim0.w;
                float flapRate = IN.anim1.w > 0.0 ? IN.anim1.w : 1.0;
                float ang = abs(sin(t * _FlapSpeed * flapRate + phase)) * _FlapAngle * 0.0174533;
                posWS.xz -= tip * (1.0 - cos(ang));
                posWS.y += length(tip) * sin(ang);
                posWS.y += sin(t * _BobSpeed + phase) * IN.anim1.x;
                float move = IN.anim1.y;
                if (move > 0.5)
                {
                    float2 off = mul(o2w, float3(IN.anim2.x, 0, IN.anim2.y)).xz;
                    float side = IN.anim2.z;
                    float w = IN.anim2.w;
                    float amp = IN.anim1.z;
                    float sa, ca;
                    if (move < 1.5)
                    {
                        float beat = t * _WaveSpeed + phase;
                        float raise = saturate(sin(beat * 0.5 + (side > 0.0 ? 0.0 : 3.14159)));
                        raise *= raise * w;
                        sincos(side * 0.5 * raise, sa, ca);
                        posWS.xz += float2(off.x * ca - off.y * sa, off.x * sa + off.y * ca) * (1.0 - 0.35 * raise) - off;
                        posWS.y += length(off) * 1.4 * raise + amp * abs(sin(beat));
                    }
                    else
                    {
                        float beat = t * _SweepSpeed + phase + (abs(side) > 1.5 ? 1.5708 : 0.0);
                        sincos(sign(side) * 0.75 * w * sin(beat), sa, ca);
                        posWS.xz += float2(off.x * ca - off.y * sa, off.x * sa + off.y * ca) - off;
                        posWS.y += amp * sin(t * _SweepSpeed * 2.0 + phase);
                    }
                }
                float blinkLit = smoothstep(0.05, 0.75, sin(t * _BlinkSpeed + phase * 3.1) * 0.5 + 0.5);
                OUT.glow = float2(glow, lerp(_GlowMin, _GlowMax, blinkLit));
                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                // The lit path reads the cloud shadow from the colour alpha (per vertex, DriftClouds.hlsl); the glow
                // path above keeps its falloff in halo.z.
                OUT.color.a = CloudShadow(posWS.xz);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                if (_GlowMode > 0.5)
                {
                    float shape;
                    if (IN.glow.x < 2.5)
                    {
                        float r = length(IN.halo.xy);
                        float soft = saturate(1.0 - r);
                        float core = saturate(1.0 - r / _CoreRadius);
                        shape = soft * soft * soft * _HaloStrength + core * core * _CoreStrength;
                    }
                    else
                    {
                        shape = IN.halo.z * IN.halo.z * _BlobStrength;
                    }
                    float haze = 1.0 - DriftFogAmount(IN.positionWS) * _CurveFogColor.a;
                    return float4(IN.color.rgb * _Tint.rgb * (shape * IN.glow.y * haze), 1);
                }

                Light mainLight = GetMainLight();
                float nd = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float lit = lerp(1.0, (_Ambient + (1.0 - _Ambient) * nd) * IN.color.a, _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                float3 marked = DriftMarkings(IN.color.rgb, IN.mark, IN.markId.x, IN.markId.y, _Markings);
                float3 shaded = marked * _Tint.rgb * lit * lightCol;
                float3 emissive = IN.color.rgb * _Tint.rgb * IN.glow.y;
                return float4(DriftFog(lerp(shaded, emissive, IN.glow.x), IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
