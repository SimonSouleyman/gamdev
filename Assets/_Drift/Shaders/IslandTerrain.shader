Shader "Drift/IslandTerrain"
{
    Properties
    {
        _Wet ("Wet Sand", Color) = (0.72,0.66,0.45,1)
        _Sand ("Sand", Color) = (0.90,0.82,0.55,1)
        _Grass ("Grass", Color) = (0.36,0.62,0.28,1)
        _Rock ("Rock", Color) = (0.50,0.47,0.44,1)
        _Snow ("Snow", Color) = (0.95,0.97,1.0,1)
        _Ash ("Ash", Color) = (0.16,0.11,0.08,1)
        _GrassStart ("Grass Start Height", Float) = 0.28
        _RockStart ("Rock Start Height", Float) = 1.7
        _SnowStart ("Snow Start Height", Float) = 3.4
        _Ambient ("Ambient", Range(0,1)) = 0.4

        [NoScaleOffset] _DetailArray ("Detail Array (Sand, Grass, Forest, Dry, Tundra, Rock, Snow)", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal Array (same layers)", 2DArray) = "" {}
        _TileA ("Tile Size Sand/Grass/Forest/Dry (world units)", Vector) = (5,8,3,3.5)
        _TileB ("Tile Size Tundra/Rock/Snow (world units)", Vector) = (5,5,3,1)
        _BumpA ("Normal Sand/Grass/Forest/Dry", Vector) = (0.35,0.9,1,1)
        _BumpB ("Normal Tundra/Rock/Snow", Vector) = (1,1.2,0.5,1)
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.85
        _DetailHue ("Detail Hue", Range(0,1)) = 0.5
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
        _Macro ("Macro Variation", Range(0,0.5)) = 0.14
    }
    SubShader
    {
        // The sea floor below -0.45 is cut away per pixel (clip in frag), so the water's depth tint sees deep water
        // there. A shader with clip() loses early depth writes on tile-based GPUs; in the AlphaTest queue it draws
        // after every plain opaque (plants, animals, critters, buildings), whose pixels then reject the terrain's
        // hidden fragments by the depth test, and no later opaque draw has to wait on its late depth writes.
        // Collapsing the sunk triangles in the vertex shader instead would move the edge of the shallow tint:
        // the contour at -0.45 runs through the middle of the beach-slope triangles.
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"

            // Layers of both arrays, in this order.
            #define L_SAND 0
            #define L_GRASS 1
            #define L_FOREST 2
            #define L_DRY 3
            #define L_TUNDRA 4
            #define L_ROCK 5
            #define L_SNOW 6
            #define L_COUNT 7

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;
                float4 normalOS    : TEXCOORD3;   // xyz = object normal, w = cloud shadow (per vertex, DriftClouds.hlsl)
                float4 color       : COLOR;
            };

            TEXTURE2D_ARRAY(_DetailArray); SAMPLER(sampler_DetailArray);
            TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);

            CBUFFER_START(UnityPerMaterial)
                float4 _Wet, _Sand, _Grass, _Rock, _Snow, _Ash;
                float _GrassStart, _RockStart, _SnowStart, _Ambient;
                float4 _TileA, _TileB, _BumpA, _BumpB;
                float _DetailStrength, _DetailHue, _NormalStrength, _Macro;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(vpi.positionWS);
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = float4(IN.normalOS, CloudShadow(vpi.positionWS.xz));
                OUT.color = IN.color;
                return OUT;
            }

            float PerLayer(float4 a, float4 b, int i)
            {
                float4 t = i < 4 ? a : b;
                int j = i & 3;
                return j == 0 ? t.x : j == 1 ? t.y : j == 2 ? t.z : t.w;
            }

            // Island-local planar projection (the island drifts and turns, the ground must not swim). The top-two
            // layer indices differ between neighbouring pixels, so the gradients come from the one continuous
            // position instead of the per-layer UV, or quads on a layer swap would pick a wrong mip.
            void SampleLayer(int i, float2 p, float2 dpx, float2 dpy, out float3 detail, out float3 tn)
            {
                float s = 1.0 / max(PerLayer(_TileA, _TileB, i), 0.01);
                float2 uv = p * s + float2(0.37, 0.61) * i;
                detail = SAMPLE_TEXTURE2D_ARRAY_GRAD(_DetailArray, sampler_DetailArray, uv, i, dpx * s, dpy * s).rgb * 3.3333;
                tn = UnpackNormalScale(SAMPLE_TEXTURE2D_ARRAY_GRAD(_NormalArray, sampler_NormalArray, uv, i, dpx * s, dpy * s), _NormalStrength * PerLayer(_BumpA, _BumpB, i));
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float y = IN.positionWS.y;
                clip(y + 0.45);
                float3 n = normalize(IN.normalWS);

                // Base colour: exactly the flat look the life system tints (stage, season, biome, snow, burn).
                float3 ground = _Grass.rgb * IN.color.rgb;
                float3 col = lerp(_Wet.rgb, _Sand.rgb, smoothstep(-0.05, 0.12, y));
                float3 groundCol = lerp(_Ash.rgb, ground, IN.color.a);
                float land = smoothstep(_GrassStart - 0.1, _GrassStart + 0.25, y);
                float rockH = smoothstep(_RockStart, _RockStart + 0.7, y);
                float snowH = smoothstep(_SnowStart, _SnowStart + 0.6, y);
                col = lerp(col, groundCol, land);
                col = lerp(col, _Rock.rgb, rockH);
                col = lerp(col, _Snow.rgb, snowH);

                float slope = 1.0 - saturate(n.y);
                float rockS = smoothstep(0.32, 0.55, slope) * smoothstep(0.25, 0.6, y);
                col = lerp(col, _Rock.rgb, rockS);

                // Which material the ground is, read back from the tint itself: the biome palettes differ enough in
                // hue (savanna red-over-blue, nordic grey, snow white, forest dark) that no extra mesh channel is needed.
                float lum = dot(ground, float3(0.2126, 0.7152, 0.0722));
                float mx = max(ground.r, max(ground.g, ground.b));
                float mn = min(ground.r, min(ground.g, ground.b));
                float sat = 1.0 - mn / max(mx, 1e-4);
                float snowT = smoothstep(0.6, 0.85, lum) * (1.0 - smoothstep(0.25, 0.45, sat));
                float dry = smoothstep(0.45, 0.9, (ground.r - ground.b) / max(ground.g, 1e-4));
                float tundra = 1.0 - smoothstep(0.4, 0.6, sat);
                float forest = max(1.0 - smoothstep(0.12, 0.3, lum), 1.0 - IN.color.a);

                float wG = land * (1.0 - rockH);
                float wRock = land * rockH;
                float wSand = 1.0 - land;
                float keep = 1.0 - snowH;
                wG *= keep; wRock *= keep; wSand *= keep;
                float wSnow = snowH;
                keep = 1.0 - rockS;
                wG *= keep; wSand *= keep; wSnow *= keep;
                wRock = wRock * keep + rockS;
                float t = wG * snowT; wSnow += t; wG -= t;
                float wDry = wG * dry; wG -= wDry;
                float wTundra = wG * tundra; wG -= wTundra;
                float wForest = wG * forest; wG -= wForest;

                float w[L_COUNT];
                w[L_SAND] = wSand; w[L_GRASS] = wG; w[L_FOREST] = wForest; w[L_DRY] = wDry;
                w[L_TUNDRA] = wTundra; w[L_ROCK] = wRock; w[L_SNOW] = wSnow;

                // Only the two strongest layers are sampled (4 fetches instead of 14). Their weights are taken
                // relative to the third strongest, so a layer enters or leaves the pair at zero weight: no seam.
                int i1 = 0, i2 = 0;
                float a1 = -1.0, a2 = -1.0, a3 = -1.0;
                [unroll] for (int k = 0; k < L_COUNT; k++)
                {
                    float v = w[k];
                    if (v > a1) { a3 = a2; a2 = a1; i2 = i1; a1 = v; i1 = k; }
                    else if (v > a2) { a3 = a2; a2 = v; i2 = k; }
                    else if (v > a3) { a3 = v; }
                }

                float2 p = IN.positionOS.xz;
                float2 dpx = ddx(p), dpy = ddy(p);
                float3 d1, d2, tn1, tn2;
                SampleLayer(i1, p, dpx, dpy, d1, tn1);
                SampleLayer(i2, p, dpx, dpy, d2, tn2);
                // The top-down projection smears into streaks where the ground stands steep (the beach skirt): fade
                // the soft layers out there; rock keeps its streaks, they read as eroded cliff.
                float flatK = smoothstep(0.3, 0.75, abs(normalize(IN.normalOS.xyz).y));
                float k1 = i1 == L_ROCK ? 1.0 : flatK, k2 = i2 == L_ROCK ? 1.0 : flatK;
                d1 = lerp(1.0.xxx, d1, k1); tn1.xy *= k1;
                d2 = lerp(1.0.xxx, d2, k2); tn2.xy *= k2;
                float b1 = (a1 - a3) * (0.5 + dot(d1, 0.3333));
                float b2 = (a2 - a3) * (0.5 + dot(d2, 0.3333));
                float bt = b2 / max(b1 + b2, 1e-5);
                float3 detail = lerp(d1, d2, bt);
                float3 tn = lerp(tn1, tn2, bt);

                float dl = dot(detail, float3(0.2126, 0.7152, 0.0722));
                detail = lerp(dl.xxx, detail, _DetailHue);
                detail = lerp(1.0.xxx, detail, _DetailStrength);
                detail *= 1.0 + (DriftNoise(p * 0.23 + 31.7) - 0.5) * 2.0 * _Macro;
                col *= detail;

                float3 nOS = normalize(normalize(IN.normalOS.xyz) + float3(tn.x, 0.0, tn.y));
                float3 nl = normalize(TransformObjectToWorldNormal(nOS));

                Light mainLight = GetMainLight();
                float nd = saturate(dot(nl, mainLight.direction));
                float lit = _Ambient + (1.0 - _Ambient) * nd;
                lit *= IN.normalOS.w;
                return float4(DriftFog(col * lit * mainLight.color, IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
