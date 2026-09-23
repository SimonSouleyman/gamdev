Shader "Drift/Vegetation"
{
    // The island vegetation mesh (IslandLifeSystem): the Drift/VertexColor look and wind sway plus Poly Haven
    // material detail. The vertex colour stays the whole colour (species, season, biome, bloom, burn/char, snow
    // caps); the detail only modulates it, because each array layer is stored divided by its own mean colour
    // (x0.3, linear): its mip average is 1, so far away and at the distance fade it is exactly the flat look.
    //   UV0 = (sway weight, phase, vertex height above ground, part) from TemplateBatch.AddPlant; part is
    //   PlantModels.Part* (0 = flat, 1 bark, 2 palm/birch bark, 3 foliage, 4 blade fibres) = array layer + 1.
    // No texture coordinates are stored: the templates are flat-shaded (every triangle has its own three
    // vertices and one normal), so each triangle is projected onto the island-local plane its normal faces most
    // (one sample per map instead of three for triplanar); the plane changes only where the facet edge already is.
    // Island-local, pre-sway positions keep the pattern fixed on the plant while the island turns and the wind bends.
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 1
        _Ambient ("Ambient", Range(0,1)) = 0.45
        _WindBend ("Wind Bend", Float) = 0.45
        _WindFlutter ("Wind Flutter", Float) = 0.18
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [NoScaleOffset] _DetailArray ("Detail Array (Bark, Palm Bark, Foliage, Blade)", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal Array (same layers)", 2DArray) = "" {}
        _Tile ("Tile Size Bark/Palm/Foliage/Blade (world units)", Vector) = (0.6,0.7,3,1.2)
        _Strength ("Detail Bark/Palm/Foliage/Blade", Vector) = (1,1,1,0.8)
        _Bump ("Normal Bark/Palm/Foliage/Blade", Vector) = (1,1,1.2,0.6)
        _DetailAmount ("Detail Amount (0 = flat look)", Range(0,1)) = 1
        _DetailHue ("Detail Hue", Range(0,1)) = 0.3
        _MipBias ("Mip Bias", Range(0,2)) = 0.25
        _FadeStart ("Detail Fade Start (camera distance)", Float) = 35
        _FadeEnd ("Detail Fade End", Float) = 70
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #pragma multi_compile _ DRIFT_NEAR_FADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 sway       : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalOS    : TEXCOORD1;
                float3 uvl         : TEXCOORD2;
                float4 color       : COLOR;
            };

            TEXTURE2D_ARRAY(_DetailArray); SAMPLER(sampler_DetailArray);
            TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _LightAmount;
                float _Ambient;
                float _WindBend;
                float _WindFlutter;
                float4 _Tile, _Strength, _Bump;
                float _DetailAmount, _DetailHue, _MipBias, _FadeStart, _FadeEnd;
            CBUFFER_END

            float4 _LifeWind;
            // (sway amplitude, gust phase, flutter phase, flutter amplitude) from IslandLifeSystem.PushWind: the
            // sway calms down as the camera comes close. x <= 0 means nobody pushed it (material preview), and the
            // old _Time-driven sway is used unchanged.
            float4 _LifeWindSway;
            // Watch / photo mode only (global keyword from WatchTools): plants nearer to the camera than x dissolve,
            // fully gone below y, so the trees between the orbit camera and the watched animals stop filling the view.
            float4 _DriftNearFade;

            float Bayer4(float2 p)
            {
                uint2 q = (uint2)p & 3u;
                uint i = q.y * 4u + q.x;
                static const float B[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (B[i] + 0.5) / 16.0;
            }

            float PerLayer(float4 v, float layer)
            {
                return dot(v, float4(layer < 0.5, abs(layer - 1.0) < 0.5, abs(layer - 2.0) < 0.5, layer > 2.5));
            }

            // Projection plane of an island-local normal: the axes u/v run along (t, b); side faces keep v on island y.
            void Plane(float3 n, out float3 t, out float3 b)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) { t = float3(sign(n.y), 0, 0); b = float3(0, 0, 1); }
                else if (a.x >= a.z)          { t = float3(0, 0, -sign(n.x)); b = float3(0, 1, 0); }
                else                          { t = float3(sign(n.z), 0, 0); b = float3(0, 1, 0); }
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y;

                float w = saturate(IN.sway.x);
                float phase = IN.sway.y;
                float storm = saturate(_LifeWind.z);
                float2 wind = _LifeWind.xy;
                bool pushed = _LifeWindSway.x > 0.0;
                float gustPhase = pushed ? _LifeWindSway.y : t * 0.6;
                float flutterPhase = pushed ? _LifeWindSway.z : t * (3.2 + 3.0 * storm);
                float swayAmp = pushed ? _LifeWindSway.x : 1.0;
                float flutterAmp = pushed ? _LifeWindSway.w : 1.0;
                float gust = 0.55 + 0.45 * sin(gustPhase + posWS.x * 0.12 + posWS.z * 0.09 + phase * 0.5);
                float flutter = sin(flutterPhase + phase + posWS.x * 1.7 + posWS.z * 1.1) * (_WindFlutter + 0.5 * storm) * flutterAmp;
                float bend = w * w * IN.sway.z * _WindBend * swayAmp;
                float2 lean = wind * (gust + flutter) + float2(-wind.y, wind.x) * flutter * 0.35;
                posWS.xz += lean * bend;
                posWS.y -= length(lean) * bend * w * 0.25;

                float3 tA, bA;
                Plane(IN.normalOS, tA, bA);
                float layer = IN.sway.w - 1.0;
                float s = 1.0 / max(PerLayer(_Tile, layer), 0.01);
                // Bark and blade fibres need their grain along the plant, i.e. the texture's v along island y on side
                // faces (Plane's b); the offset keeps neighbouring layers from starting on the same texel.
                OUT.uvl = float3(float2(dot(IN.positionOS.xyz, tA), dot(IN.positionOS.xyz, bA)) * s + layer * float2(0.37, 0.61), layer);

                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalOS = IN.normalOS;
                // The fragment only reads rgb; alpha carries the cloud shadow (per vertex, DriftClouds.hlsl).
                OUT.color = float4(IN.color.rgb, CloudShadow(posWS.xz));
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float layer = IN.uvl.z;
                float has = layer > -0.5 ? 1.0 : 0.0;
                float li = max(layer, 0.0);
                float dist = distance(IN.positionWS, GetCameraPositionWS());
            #if defined(DRIFT_NEAR_FADE)
                clip(saturate((dist - _DriftNearFade.y) / max(_DriftNearFade.x - _DriftNearFade.y, 0.01)) - Bayer4(IN.positionHCS.xy));
            #endif
                float k = has * _DetailAmount * (1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01)));

                float2 uv = IN.uvl.xy;
                float bias = exp2(_MipBias);
                float2 dx = ddx(uv) * bias, dy = ddy(uv) * bias;
                float3 d = SAMPLE_TEXTURE2D_ARRAY_GRAD(_DetailArray, sampler_DetailArray, uv, li, dx, dy).rgb * 3.3333;
                float2 tn = SAMPLE_TEXTURE2D_ARRAY_GRAD(_NormalArray, sampler_NormalArray, uv, li, dx, dy).rg * 2.0 - 1.0;

                float dl = dot(d, float3(0.2126, 0.7152, 0.0722));
                d = lerp(dl.xxx, d, _DetailHue);
                d = lerp(1.0.xxx, d, PerLayer(_Strength, li) * k);
                tn *= PerLayer(_Bump, li) * k;

                float3 nOS = normalize(IN.normalOS);
                float3 tA, bA;
                Plane(nOS, tA, bA);
                float3 n = normalize(TransformObjectToWorldNormal(normalize(nOS + tA * tn.x + bA * tn.y)));

                Light mainLight = GetMainLight();
                float nd = saturate(dot(n, mainLight.direction));
                float lit = lerp(1.0, (_Ambient + (1.0 - _Ambient) * nd) * IN.color.a, _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                return float4(DriftFog(IN.color.rgb * d * _Tint.rgb * lit * lightCol, IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
