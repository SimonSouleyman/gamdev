Shader "Drift/PlateSeam"
{
    // Plate boundaries as something happening in the water. The mesh (PlateSystem.AddSeam) is one soft ribbon
    // per boundary that follows the meandering seam curve (PlateSystem.Seams.cs) as a mitered strip, split along
    // the centre line into a plate-A and a plate-B strip so every per-side value below is flat across its strip
    // (and interpolated along it):
    //   UV0 = (across -1..1, along = true arc length [u] offset to the plate-midpoint coordinate of end 0, along of end 0, along of end 1)
    //   UV1 = (closing speed / transformThreshold [> 1 convergent, < -1 divergent], strength 0..1, local half width [u, pinches and swells], side -1 = A / +1 = B)
    //   UV2 = (along scroll of this side [u], inward scroll [u] - both projected on the local seam frame, storm 0..1 at the seam, signed plate speed)
    //   UV3 = (plate wave axis x, z, plate position along / across that axis modulo the chevron cell)
    // All scrolls are closed-form functions of the plate positions (no shader time involved), so the foam on a
    // side moves exactly with that plate relative to the seam: toward the centre on a convergent boundary, away
    // on a divergent one, in opposite directions along a transform one. Only the looks' own life (fronts
    // rolling in, rings welling up, shimmer) runs on _Time + _SeamTimeOffset.
    // side == 0 marks a tectonic event pulse quad: UV0 = (x, y, event type, progress), UV1 = (0, strength, _, 0).
    Properties
    {
        _FoamColor ("Foam", Color) = (0.93,0.97,1,1)
        _UpwellColor ("Upwelling Turquoise", Color) = (0.36,0.86,0.84,1)
        _TroughColor ("Trough / Slip Line", Color) = (0.03,0.15,0.3,1)
        _WarmColor ("Rift Warm Glow", Color) = (1,0.6,0.32,1)
        _FoamAmount ("Foam Amount", Range(0,1)) = 0.8
        _TintAmount ("Water Tint Amount", Range(0,1)) = 0.55
        _WarmAmount ("Rift Warm Amount", Range(0,1)) = 0.16
        _ChevronAmount ("Current Chevron Amount", Range(0,1)) = 0.3
        _SeamOpacity ("Opacity", Range(0,1)) = 0.85
        _SeamFocus ("Focus xz, fade start, fade end", Vector) = (0,0,80,120)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "PlateSeam"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftCurve.hlsl"

            // Keep in sync with PlateSystem.ChevronPeriodU / ChevronPeriodV.
            #define CHEVRON_U 1.8
            #define CHEVRON_V 1.2

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 seam        : TEXCOORD1;
                float4 info        : TEXCOORD2;
                float4 flow        : TEXCOORD3;
                float4 plate       : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamColor, _UpwellColor, _TroughColor, _WarmColor;
                float _FoamAmount, _TintAmount, _WarmAmount, _ChevronAmount, _SeamOpacity;
                float4 _SeamFocus;
            CBUFFER_END

            float _SeamTimeOffset;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(vpi.positionWS);
                OUT.positionWS = vpi.positionWS;
                OUT.seam = IN.uv0;
                OUT.info = IN.uv1;
                OUT.flow = IN.uv2;
                OUT.plate = IN.uv3;
                return OUT;
            }

            float Band(float x, float a, float b, float c, float d)
            {
                return smoothstep(a, b, x) * (1.0 - smoothstep(c, d, x));
            }

            float4 EventPulse(Varyings IN, float3 shade, float master)
            {
                float rr = length(IN.seam.xy);
                float type = IN.seam.z;
                float prog = saturate(IN.seam.w);
                float sharp = saturate(step(abs(type - 1.0), 0.5) + step(2.5, type));
                float env = lerp(sin(prog * 3.14159), 1.0 - prog, sharp);
                float radius = lerp(0.18, 0.78, prog);
                float w = 0.07 + 0.09 * prog;
                float ring = exp(-(rr - radius) * (rr - radius) / (w * w));
                float inner = (1.0 - smoothstep(0.0, radius, rr)) * 0.3;
                float rift = step(abs(type - 2.0), 0.5);
                float3 col = lerp(_FoamColor.rgb, _UpwellColor.rgb, rift * 0.7);
                float a = (ring * 0.55 + inner) * env * (0.45 + 0.55 * IN.info.y) * (1.0 - smoothstep(0.8, 1.0, rr));
                a = saturate(a) * master * _FoamAmount;
                float3 warm = _WarmColor.rgb * (rift * inner * env * _WarmAmount * master);
                return float4((col * a + warm) * shade, a);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 wp = IN.positionWS.xz;
                float t = _Time.y + _SeamTimeOffset;
                float camDist = length(GetCameraPositionWS() - IN.positionWS);
                float fade = 1.0 - smoothstep(_SeamFocus.z, _SeamFocus.w, length(wp - _SeamFocus.xy));
                fade *= lerp(0.6, 1.0, smoothstep(6.0, 26.0, camDist));
                float master = fade * _SeamOpacity;

                Light l = GetMainLight();
                float lum = saturate(dot(l.color, float3(0.3, 0.59, 0.11)));
                float3 shade = lerp(0.55, 1.0, lum) * lerp(float3(1, 1, 1), saturate(l.color), 0.35) * CloudShadow(wp);

                float side = IN.info.w;
                if (abs(side) < 0.5) return EventPulse(IN, shade, master);

                float hw = max(IN.info.z, 0.01);
                float x = IN.seam.x * hw;
                float m = abs(x);
                float dEnd = max(max(IN.seam.z - IN.seam.y, IN.seam.y - IN.seam.w), 0.0);
                float r = length(float2(dEnd, x)) / hw;
                // Ragged outline: world-space noise pushes the soft edge in and out (exact at the centre line and at the
                // mesh edge, continuous across both halves and across junctions), so a gentle bend never reads as a ruler.
                float edgeN = DriftNoise(wp * 0.23 + 3.1) * 0.65 + DriftNoise(wp * 0.71 + float2(t * 0.04, 7.7)) * 0.35;
                r = saturate(r + (edgeN - 0.5) * 1.5 * r * (1.0 - r));
                float profile = 1.0 - smoothstep(0.3, 1.0, r);

                float closing = IN.info.x;
                float wC = smoothstep(0.6, 1.4, closing);
                float wD = smoothstep(0.6, 1.4, -closing);
                float wT = saturate(1.0 - wC - wD);
                float strength = saturate(IN.info.y);
                float storm = saturate(IN.flow.z);

                // The two halves sample different noise (each scrolls with its own plate), so everything built on it
                // fades out on the centre line; what sits on the line itself (crest, slip line, glow) only uses the
                // shared along coordinate and is continuous across it.
                float u = IN.seam.y - IN.flow.x;
                float v = m + IN.flow.y;
                float2 q = float2(u + side * 31.7, v);
                float n1 = DriftNoise(q * 0.6 + float2(t * 0.03, -t * 0.05));
                float n2 = DriftNoise(float2(q.x * 0.3, q.y * 2.3) + 5.3);
                float lod = saturate(1.0 - (camDist - 60.0) / 80.0);
                float offLine = smoothstep(0.03, 0.2, r);
                float sc = IN.seam.y;

                // Convergent: fronts rolling in on the centre from both sides, broken crest, dark troughs.
                float saw = 1.0 - frac(v * 0.85 + t * 0.3 + n1 * 0.9 + side * 0.23);
                float front = smoothstep(0.25, 0.85, saw) * (1.0 - smoothstep(0.85, 1.0, saw));
                float ridge = 1.0 - smoothstep(0.0, 0.85, r);
                float cw = sin(sc * 1.9 + t * 0.8) + sin(sc * 0.73 - t * 0.55 + 1.3) + 0.5 * sin(sc * 4.3 + t * 1.7);
                float wob = 0.05 * sin(sc * 0.9 + t * 0.6) + 0.03 * sin(sc * 2.3 - t * 0.9);
                float rc = length(float2(dEnd / hw, IN.seam.x + wob));
                float crestW = 0.05 + 0.08 * saturate(cw * 0.4 + 0.5);
                float crestTex = 0.72 + 0.28 * sin(sc * 6.1 + t * 2.1) * sin(sc * 2.7 - t * 1.3 + IN.seam.x * 9.0);
                float crest = (1.0 - smoothstep(crestW * 0.2, crestW * 1.35, rc)) * smoothstep(-1.0, 0.1, cw) * crestTex;
                float foamC = front * smoothstep(0.25, 0.55, n2) * ridge * offLine + crest * 0.95;
                float tintC = Band(r, 0.08, 0.22, 0.3, 0.7) * 0.55;

                // Divergent: a light turquoise band, rings welling up out of the noise maxima and drifting outward.
                float swell = 1.0 - smoothstep(0.0, 0.9, r);
                float ringPhase = frac(v * 0.22 - n1 * 3.2 - t * 0.16);
                float rings = Band(ringPhase, 0.0, 0.08, 0.1, 0.24) * smoothstep(0.35, 0.7, n1) * swell;
                float bubbles = smoothstep(0.8, 0.93, n2 * 0.5 + n1 * 0.5 + 0.12 * sin(t * 1.3 + u * 2.1)) * swell;
                float foamD = (rings * 0.5 + bubbles * 0.45) * offLine;
                float tintD = swell * swell * (0.85 + 0.3 * (n1 - 0.5) * offLine) * 0.85;
                float warm = pow(saturate(1.0 - r * 1.6), 2.0) * (0.65 + 0.35 * sin(t * 0.7 + sc * 0.35)) * wD;

                // Transform: long foam lines carried along the seam, opposite ways on the two sides; whitecaps in storms.
                float streakN = n2 * 0.72 + n1 * 0.28 + 0.04 * sin(t * 1.1 + u * 0.6);
                float shear = Band(r, 0.04, 0.2, 0.45, 0.95);
                float lines = smoothstep(0.15, 0.75, 0.5 + 0.5 * sin(v * 6.5 + n1 * 5.0));
                float streaks = smoothstep(0.45, 0.66, streakN) * lines * shear;
                float caps = smoothstep(0.42, 0.7, n1 * 0.6 + n2 * 0.4) * storm * (1.0 - smoothstep(0.2, 0.95, r)) * offLine;
                float foamT = streaks * 0.7 + caps * 0.7;
                float tintT = (1.0 - smoothstep(0.0, 0.12, rc)) * 0.5;

                // Current hints: soft chevrons anchored to the plate (so they travel with it) pointing the way it moves.
                float2 e = IN.plate.xy;
                float speedSigned = IN.flow.w;
                float sgn = speedSigned < 0.0 ? -1.0 : 1.0;
                float cu = (dot(wp, e) - IN.plate.z) * sgn;
                float cvRaw = (dot(wp, float2(-e.y, e.x)) - IN.plate.w) / CHEVRON_V;
                float row = floor(cvRaw);
                float cvc = abs(cvRaw - row - 0.5);
                float cph = (cu + cvc * CHEVRON_V * 0.9) / CHEVRON_U + frac(row * 0.5) * 1.37;
                float cf = frac(cph);
                float pick = DriftHash(float2(floor(cph), row));
                float chev = smoothstep(0.2, 0.85, cf) * (1.0 - smoothstep(0.85, 1.0, cf)) * (1.0 - smoothstep(0.15, 0.45, cvc));
                chev *= smoothstep(0.4, 0.6, pick) * (0.5 + 0.5 * pick);
                chev *= Band(r, 0.2, 0.4, 0.6, 0.95) * saturate(abs(speedSigned) * 0.6) * _ChevronAmount;

                float foam = foamC * wC + foamD * wD + foamT * wT;
                float foamAvg = (0.22 * wC + 0.12 * wD + 0.16 * wT) * (1.0 - r);
                foam = lerp(foamAvg, foam + chev, lod);
                float vis = lerp(0.45, 1.0, strength);
                float foamA = saturate(foam * vis) * _FoamAmount;

                float aC = tintC * wC, aD = tintD * wD, aT = tintT * wT;
                float tintA = saturate(aC + aD + aT) * _TintAmount * vis;
                float3 tintCol = (_TroughColor.rgb * (aC + aT) + _UpwellColor.rgb * aD) / max(aC + aD + aT, 1e-4);

                float k = profile * master;
                float3 rgb = _FoamColor.rgb * foamA + tintCol * tintA * (1.0 - foamA) + _WarmColor.rgb * (warm * _WarmAmount * vis);
                float alpha = foamA + tintA * (1.0 - foamA);
                float fog = DriftFogAmount(IN.positionWS);
                float haze = fog * _CurveFogColor.a;
                // Seams pile up near the limb; without fading the alpha too they drew a bright line on the horizon.
                k *= 1.0 - fog;
                return float4(lerp(rgb * shade, _CurveFogColor.rgb * alpha, haze) * k, alpha * k);
            }
            ENDHLSL
        }
    }
}
