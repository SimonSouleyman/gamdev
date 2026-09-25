Shader "Drift/WaterFlecks"
{
    // The foam flecks the plate currents carry over the sea (Drift.Visuals.WaterFlecks owns the mesh and the renderer,
    // Drift.Visuals.WaterFeedback pushes the same property block as to the water). They used to be searched for in
    // every sea pixel - two cell sizes x two phases = four cell lookups each, about 40 % of the water's cost on the
    // phone - although only a few percent of the sea shows one. Now each (level, phase, cell) is a quad whose vertex
    // shader decides from the very same hashes whether its fleck is alive; dead ones collapse outside the clip space,
    // living ones are drawn as a small quad around the comet and blended over the finished water.
    //
    // Levels: the old per-pixel code picked the cell size 2 * 2^lv from lv = floor(log2(camDist / 12)) and cross-faded
    // into 2^(lv + 1); here every level lv is its own grid around the camera and its weight at a pixel is the same
    // tent, saturate(1 - |log2(camDist / 12) - lv|). Phases, drift, life window, density, comet shape, fade, emphasis
    // and the haze and cloud shadow on top are the old formulas; the random layout of a cycle differs (its seed is
    // per level now, which also removes the seam the old layer swap drew where log2(camDist / 12) is a whole number).
    Properties
    {
        _FoamColor ("Foam Color", Color) = (0.95,0.98,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "WaterFlecks"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // Only where Drift/Water drew this frame (it marks its pixels in the stencil).
            Stencil { Ref 8 ReadMask 8 Comp Equal }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"
            #include "DriftWater.hlsl"

            // Must match Drift.Visuals.WaterFlecks.LevelMin.
            #define FLECK_LEVEL_MIN -3.0

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // xy = quad corner (-1 / +1)
                float4 cellInfo   : TEXCOORD0;  // xy = cell offset from the camera's cell, z = level index, w = phase
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 posAB       : TEXCOORD0;  // xy = world xz, zw = (along, across) the comet in cell units
                float4 fleck       : TEXCOORD1;  // x = half length (cells), y = cell size, z = level, w = life x brightness
                // Everything that scales a fleck's strength and changes only over metres (field fade, speed, plate
                // boundary, emphasis round the player, storm): once per corner instead of per pixel - a fleck is a small
                // quad. Includes StormField, whose rim noise used to run in every fleck pixel.
                half   gain        : TEXCOORD2;
            };

            float4 FieldAt(float2 wp)
            {
                float2 uv = (wp - _CurrentFieldParams.xy) * _CurrentFieldParams.z;
                return SAMPLE_TEXTURE2D_LOD(_CurrentField, sampler_CurrentField, uv, 0);
            }

            float FieldFade(float2 wp)
            {
                float2 uv = (wp - _CurrentFieldParams.xy) * _CurrentFieldParams.z;
                float2 inside = saturate(min(uv, 1.0 - uv) * 12.0);
                return inside.x * inside.y;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.positionHCS = float4(2.0, 2.0, 2.0, 1.0);   // outside the clip volume: a dead fleck draws nothing

                float lv = FLECK_LEVEL_MIN + IN.cellInfo.z;
                float k = IN.cellInfo.w;
                float cell = 2.0 * exp2(lv);
                float T = 2.2 + 0.08 * cell;
                float tt = _Time.y / T + 0.5 * k;
                float ph = frac(tt);
                float seed = frac(floor(tt) * 0.618034) * 89.0 + 31.0 * k + 17.0 * lv;

                float3 cam = GetCameraPositionWS();
                float seaY = UNITY_MATRIX_M._m13;
                float maxSpeed = _CurrentFieldParams.w;
                // The grid drifts with the current (q = (wp - vel T ph) / cell), so it is laid around where the
                // camera's own water came from.
                float2 velCam = (FieldAt(cam.xz).rg * 2.0 - 1.0) * maxSpeed;
                float2 id = floor((cam.xz - velCam * (T * ph)) / cell) + IN.cellInfo.xy;
                float2 pc = (id + 0.5) * cell + velCam * (T * ph);
                float3 toC = cam - float3(pc.x, seaY, pc.y);
                float distC = length(toC);
                float Lc = log2(max(distC, 1.0) / 12.0);
                float fadeC = FieldFade(pc);
                // Cheap rejections before any hash: no field / knob off, the level's tent is 0 all over the quad
                // (it spans at most ~0.3 in log2 distance), the field has faded out, or the haze hides it.
                if (maxSpeed <= 0.0 || _CurrentFoam <= 0.0 || abs(Lc - lv) > 1.35 || fadeC <= 0.0
                    || DriftFogAmount(float3(pc.x, seaY, pc.y)) >= 0.985)
                    return OUT;

                float4 fld = FieldAt(pc);
                float2 vel = (fld.rg * 2.0 - 1.0) * maxSpeed;
                float speed = length(vel);
                float2 dir = speed > 1e-3 ? vel / speed : float2(1.0, 0.0);
                float speedN = saturate(speed / 3.0);
                float hl = 0.03 + 0.15 * speedN + 0.12 * _SpeedFeel.y * _SpeedFeelGains.z;
                float edge = fld.b * fadeC;
                float closing = fld.a * 2.0 - 1.0;
                float density = 0.4 + 0.25 * edge * (1.0 + 2.0 * saturate(closing * 3.0));

                float2 hs = id + seed;
                float h1 = DriftHash(hs);
                float h2 = DriftHash(hs + float2(7.31, 1.93));
                float h3 = DriftHash(hs + float2(2.17, 9.41));
                float start = h1 * 0.45;
                if (h2 * 0.7 + h3 * 0.3 > density || ph <= start || ph >= start + 0.55)
                    return OUT;

                float life = sin(3.14159 * saturate((ph - start) / 0.55));
                float len = hl * (0.6 + 0.8 * h3);
                float2 centre = (id + 0.3 + 0.4 * float2(h1, h2)) * cell + vel * (T * ph);

                // Room for the anti-aliasing fringe: aa = 0.7 * fw / cell + 0.008 with fw the pixel's footprint on the
                // sea, estimated generously from the distance and the grazing angle.
                float pixelAngle = 2.0 / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float fwEst = distC * pixelAngle / max(abs(toC.y) / max(distC, 1e-3), 0.05);
                float aaMax = min(1.4 * fwEst / cell + 0.008, 0.6);
                float2 ext = float2(len + 0.045 + aaMax, 0.045 + aaMax);
                float2 ab = IN.positionOS.xy * ext;
                float2 perp = float2(-dir.y, dir.x);
                float2 wp = centre + (dir * ab.x + perp * ab.y) * cell;
                float3 posWS = float3(wp.x, seaY, wp.y);

                float2 coastN;
                float coastFade;
                float coastD = CoastDistance(wp, coastN, coastFade);
                float storm = saturate(max(_Storm, StormField(wp)));
                float nearPlayer = (1.0 - smoothstep(0.35, 1.0, coastD / max(_CurrentEmphasis.x, 1e-3))) * coastFade;
                float gain = FieldFade(wp) * (0.35 + 0.65 * speedN);
                gain *= _CurrentFoam * (1.0 + _CurrentEmphasis.y * nearPlayer) * (1.0 + 0.4 * edge) * (1.0 - 0.6 * storm);
                // Water highway: the longer the island travels with the current, the brighter the flecks around it.
                gain *= 1.0 + _SpeedFeelGains.z * _SpeedFeel.y * (0.35 + 0.95 * nearPlayer);

                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.posAB = float4(wp, ab);
                OUT.fleck = float4(len, cell, lv, life * (0.6 + 0.4 * h3));
                OUT.gain = (half)gain;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 wp = IN.posAB.xy;
                float fw = max(fwidth(wp.x), fwidth(wp.y));
                // The comet itself is measured in cell units (well under 1): half precision from here on.
                half aa = (half)(0.7 * fw / IN.fleck.y + 0.008);
                half a = (half)IN.posAB.z, b = (half)IN.posAB.w;
                half len = (half)IN.fleck.x;
                half ca = clamp(a, -len, len);
                half taper = saturate((len - ca) / max(2.0h * len, 1e-4h));
                half r = 0.045h * (1.0h - 0.7h * taper);
                half dist = length(half2(a - ca, b));
                half shape = 1.0h - smoothstep(r * 0.3h - aa, r + aa, dist);

                float3 posWS = float3(wp.x, UNITY_MATRIX_M._m13, wp.y);
                float camDist = length(GetCameraPositionWS() - posWS);
                half w = (half)saturate(1.0 - abs(log2(max(camDist, 1.0) / 12.0) - IN.fleck.z));
                float haze = DriftFogAmount(posWS);
                // Deep in the haze a fleck keeps under 3 % of its contrast: the sea never drew them there.
                half flecks = haze < 0.97 ? saturate(saturate(shape * (half)IN.fleck.w * w) * IN.gain) : 0.0h;

                half3 light = _WaterLight.w > 0.0 ? (half3)_WaterLight.rgb : half3(1, 1, 1);
                half3 col = (half3)_FoamColor.rgb * light * 0.95h;
                // The sea used to mix the flecks in before its cloud shadow and haze: the same two on top here (the
                // shadow from the texture the sea reads too).
                if (haze < 0.985) col *= (half)CloudShadowTex(wp);
                col = lerp(col, (half3)_CurveFogColor.rgb, (half)haze);
                return float4(col, flecks * 0.5h);
            }
            ENDHLSL
        }
    }
}
