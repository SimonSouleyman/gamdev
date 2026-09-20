Shader "Drift/Critter"
{
    // Drift/VertexColor plus the Phase 4 critter channels baked by TemplateBatch.AddCritter:
    //   UV0 = (wing tip offset from the body midline in world xz, phase, glow 0/1)
    //   UV1 = (bob amplitude in world units, 0, 0, 0)
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 anim0      : TEXCOORD0;
                float4 anim1      : TEXCOORD1;
                float4 anim2      : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float2 glow        : TEXCOORD2;
                float3 halo        : TEXCOORD3;
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

                if (_GlowMode > 0.5)
                {
                    float kind = IN.anim0.w;
                    float blinkWave = smoothstep(0.05, 0.75, sin(t * _BlinkSpeed + phase * 3.1) * 0.5 + 0.5);
                    float blink = lerp(1.0, lerp(_GlowMin, 1.0, blinkWave), IN.anim1.w);
                    float vis = saturate((_FireflyAmount - IN.anim2.x) * 10.0);
                    float ds = t * _DriftSpeed;
                    posWS.xz += IN.anim1.z * 0.66 * float2(
                        sin(ds + phase * 1.7) + 0.5 * sin(ds * 2.3 + phase * 3.1),
                        cos(ds * 0.8 + phase * 2.3) + 0.5 * cos(ds * 1.9 + phase * 1.3));
                    posWS.y += sin(t * _BobSpeed * 0.5 + phase) * IN.anim1.x;
                    OUT.positionWS = posWS;
                    float3 bent = DriftCurveWS(posWS);
                    float intensity = blink * vis;
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

                float2 tip = IN.anim0.xy;
                float glow = IN.anim0.w;
                float ang = abs(sin(t * _FlapSpeed + phase)) * _FlapAngle * 0.0174533;
                posWS.xz -= tip * (1.0 - cos(ang));
                posWS.y += length(tip) * sin(ang);
                posWS.y += sin(t * _BobSpeed + phase) * IN.anim1.x;
                float blinkLit = smoothstep(0.05, 0.75, sin(t * _BlinkSpeed + phase * 3.1) * 0.5 + 0.5);
                OUT.glow = float2(glow, lerp(_GlowMin, _GlowMax, blinkLit));
                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
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
                float lit = lerp(1.0, (_Ambient + (1.0 - _Ambient) * nd) * CloudShadow(IN.positionWS.xz), _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                float3 shaded = IN.color.rgb * _Tint.rgb * lit * lightCol;
                float3 emissive = IN.color.rgb * _Tint.rgb * IN.glow.y;
                return float4(DriftFog(lerp(shaded, emissive, IN.glow.x), IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
