Shader "Drift/Clouds"
{
    // Fair-weather cloud clumps ("Schaefchenwolken") over the whole map in one static mesh built by
    // Drift.Visuals.CloudShadows: a grid of clump slots round the player, nine puff billboards each. The vertex
    // shader looks up which clump of DriftClouds.hlsl belongs in the slot this frame (the same clumps whose shadows
    // the water and the islands draw), so the mesh never changes: drifting and cover are all uniforms.
    // uv0 = (slot x, slot z relative to the focus cell, puff index, -), uv1 = (corner x, corner y, -, -).
    Properties
    {
        _Alpha ("Alpha", Range(0,1)) = 0.9
        _StormColor ("Storm Cloud Color", Color) = (0.24,0.26,0.31,1)
        _Boil ("Boil Speed", Range(0,0.5)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+8" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Clouds"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 uvSeed      : TEXCOORD0;   // xy = billboard uv, z = seed, w = alpha
                float4 fogStorm    : TEXCOORD1;   // x = fog, y = storm
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _StormColor;
                float _Alpha, _Boil;
            CBUFFER_END

            float4 _DriftStorms[4];
            float  _DriftStormCount;
            float  _LightningFlash;
            float  _CloudGridHalf;   // half the slot grid in cells (CloudShadows.GridSize / 2)
            // Adventure ring: the stretch of track in front of the player stays free of cloud (CloudShadows pushes it,
            // mirrored by Drift.Visuals.RingReadability.CloudCorridor). x = clear half width across the track around
            // the player, y = fade width beyond it, z = clear distance ahead, w = fade length beyond that; z = 0 = off.
            float4 _CloudRingClear;

            // 0 = the puff hangs over the track just ahead of the player (hidden), 1 = scenery.
            float RingCorridor(float2 xz)
            {
                if (_CurveRing.x <= 0.0 || _CloudRingClear.z <= 0.0) return 1.0;
                float period = 2.0 * _CurveRing.w;
                float dz = xz.y - _CloudFocus.z;
                dz -= period * round(dz / max(period, 1.0));
                // Ahead = the way the camera looks along the track (its forward is -V[2]).
                float ahead = UNITY_MATRIX_V[2].z <= 0.0 ? dz : -dz;
                float across = abs(xz.x - _CloudFocus.x);
                float side = smoothstep(_CloudRingClear.x, _CloudRingClear.x + max(_CloudRingClear.y, 1e-3), across);
                // Behind the camera too: it sits a few units back and low, under the layer.
                float along = smoothstep(_CloudRingClear.z, _CloudRingClear.z + max(_CloudRingClear.w, 1e-3), ahead)
                            + (1.0 - smoothstep(-40.0, -25.0, ahead));
                return saturate(max(side, along));
            }

            float StormAt(float2 wp)
            {
                float storm = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    if (i >= (int)_DriftStormCount) break;
                    float4 s = _DriftStorms[i];
                    float d = length(wp - s.xy) / max(s.z, 1e-3);
                    d *= 0.82 + 0.36 * DriftNoise(wp * 0.05 + s.xy * 0.013);
                    storm = max(storm, s.w * (1.0 - smoothstep(0.45, 1.05, d)));
                }
                return storm;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float cell = 1.0 / max(_CloudScale, 1e-4);
                float cover = saturate(_CloudCover);
                float2 fq = _CloudFocus.xz * _CloudScale + _CloudOffset.xy;
                float2 id = floor(fq) + IN.uv0.xy;
                float2 c, axis;
                float r, aspect;
                DriftCloudClump(id, cover, c, r, axis, aspect);
                r *= step(0.001, cover);
                float2 centre = (c - _CloudOffset.xy) * cell;
                float seed = DriftHash(id + 0.37);
                float2 off;
                float size, h;
                DriftPuffLayout(IN.uv0.z, seed, axis, aspect, r * cell, off, size, h);

                float2 xz = centre + off;
                float storm = StormAt(xz);
                size *= 1.0 + 0.35 * storm;
                float3 pos = float3(xz.x, _CloudHeight + h, xz.y);

                float grid = 1.0 - smoothstep(_CloudGridHalf - 2.5, _CloudGridHalf - 0.6, length(c - fq));
                // The adventure ring ends at its rims: DriftCurveWS folds anything beyond onto the lip, where the clumps
                // of the whole grid would pile up as a wall of cloud hanging out over the edge into space. A puff
                // fades as soon as its own billboard would reach past the rim, so the cover ends inside the band.
                float band = 1.0;
                if (_CurveRing.x > 0.0)
                {
                    float over = abs(xz.x - _CurveRing.y) + size * 2.0 + 2.0 - _CurveRing.z;
                    band = 1.0 - smoothstep(-2.0, 2.0, over);
                }
                float alpha = _Alpha * grid * band * RingCorridor(xz) * DriftPuffClearView(pos, size);
                size *= 0.55 + 0.45 * alpha / max(_Alpha, 1e-3);

                OUT.positionHCS = TransformWorldToHClip(DriftPuffCorner(pos, IN.uv1.xy, size * step(0.004, alpha)));
                OUT.uvSeed = float4(IN.uv1.xy, seed * 7.0 + IN.uv0.z * 0.31, alpha);
                OUT.fogStorm = float4(DriftFogAmount(pos) * _CurveFogColor.a, storm, 0, 0);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float storm = IN.fogStorm.y;
                float3 lit = DriftPuffLitColor();
                float3 dark = DriftPuffDarkColor();
                // Inside a storm the fair-weather clumps turn into storm clouds (same colours as Drift/Storm's puffs).
                float bright = saturate(dot(lit, float3(0.3, 0.5, 0.2)));
                float k = saturate(storm * 1.6);
                lit = lerp(lit, lerp(_StormColor.rgb * 1.35, dark, 0.2) * (0.35 + 0.65 * bright), k);
                dark = lerp(dark, _StormColor.rgb * 0.35 * (0.3 + 0.7 * bright), k);
                float4 p = DriftPuffShade(IN.uvSeed.xy, IN.uvSeed.z, _Time.y * _Boil, lit, dark);
                float3 col = p.rgb + saturate(_LightningFlash) * float3(0.75, 0.8, 0.95) * 0.6;
                col = lerp(col, _CurveFogColor.rgb, IN.fogStorm.x);
                return float4(col, p.a * IN.uvSeed.w);
            }
            ENDHLSL
        }
    }
}
