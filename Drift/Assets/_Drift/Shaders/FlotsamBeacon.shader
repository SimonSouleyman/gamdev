Shader "Drift/FlotsamBeacon"
{
    // Beacons over the pickups on the player's course (Drift.Visuals.ShipSystem.Beacons builds one static mesh of
    // slots; every vertex sits at the origin and is placed here from _Beacons). Unlit, no textures, premultiplied
    // alpha: the glows return alpha 0 (= additive), the outline ring covers the water behind it.
    // uv0 = (slot, part, corner x, corner y); part 0 = halo billboard over the piece, 1 = ring rippling out on the
    // water, 2 = pillar of light (night only), 3 = the outline ring of the cozy watch marker around the piece
    // (a screen-facing ellipse flattened to _RingParams.y, so it keeps its shape from the low race camera).
    Properties
    {
    }
    SubShader
    {
        // After the sea and the rim foam: the ring lies on the water.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+120" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Beacon"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DriftCurve.hlsl"

            // The outline quad reaches this far past the line (in ring radii) for the soft dark edge.
            #define RING_QUAD 1.3

            float4 _Beacons[32];     // xyz = unbent world position of the piece (y = its hop while collected), w = strength
            float4 _BeaconParams;    // x = halo radius, y = min size as tan(angle), z = ring radius, w = pillar height
            float4 _BeaconColor;     // rgb = linear colour, a = night 0..1
            float4 _BeaconRings[32]; // x = outline ring radius (u), y = its strength (0 = none)
            float4 _RingParams;      // x = min radius as tan(angle), y = height squash, z = dark edge by day
            float4 _RingColor;       // rgb = linear line colour, a = line opacity

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv0        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 uv          : TEXCOORD0;   // xy = corner, z = part, w = strength
                float4 data        : TEXCOORD1;   // x = halo dim, y = fog keep, z = phase
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                int slot = (int)IN.uv0.x;
                float part = IN.uv0.y;
                float2 c = IN.uv0.zw;
                float4 b = _Beacons[slot];
                float night = saturate(_BeaconColor.a);
                float ph = slot * 1.618;
                float strength = b.w;
                if (part > 2.5) strength = _BeaconRings[slot].y;
                else if (part > 1.5) strength *= night;
                OUT.uv = float4(c, part, strength);
                OUT.data = float4(1.0, 1.0 - DriftFogAmount(b.xyz) * _CurveFogColor.a, ph, 0.0);
                if (strength <= 0.002)
                {
                    // All four corners on one point outside the clip volume: nothing is rasterised.
                    OUT.positionHCS = float4(2.0, 2.0, 2.0, 1.0);
                    return OUT;
                }

                float t = _Time.y;
                float3 cam = _WorldSpaceCameraPos;
                if (part < 0.5)
                {
                    float3 centre = DriftCurveWS(b.xyz + float3(0.0, 0.6 + 0.08 * sin(t * 2.1 + ph), 0.0));
                    float d = length(centre - cam);
                    float r = max(_BeaconParams.x, _BeaconParams.y * d);
                    // A halo blown up to stay visible far away is dimmed a little, so it does not glare.
                    OUT.data.x = lerp(0.65, 1.0, saturate(_BeaconParams.x / r));
                    float3 w = centre + (UNITY_MATRIX_V[0].xyz * c.x + UNITY_MATRIX_V[1].xyz * c.y) * r;
                    OUT.positionHCS = TransformWorldToHClip(w);
                }
                else if (part < 1.5)
                {
                    float d = length(DriftCurveWS(float3(b.x, 0.0, b.z)) - cam);
                    float r = max(_BeaconParams.z, _BeaconParams.y * 2.0 * d);
                    OUT.positionHCS = DriftCurveHClip(float3(b.x + c.x * r, 0.05, b.z + c.y * r));
                }
                else if (part > 2.5)
                {
                    // Around the piece where it floats (and hops while collected), facing the camera, a little larger
                    // than the line so the soft dark edge fits; slow breathing like the watch marker.
                    float3 centre = DriftCurveWS(b.xyz + float3(0.0, 0.06, 0.0));
                    float d = length(centre - cam);
                    float r = max(_BeaconRings[slot].x, _RingParams.x * d) * (1.0 + 0.05 * sin(t * 2.5 + ph)) * RING_QUAD;
                    // A piece drifting past right beside the camera would sweep a huge arc across the screen.
                    OUT.uv.w *= saturate((d - 5.0) * 0.25);
                    float3 w = centre + (UNITY_MATRIX_V[0].xyz * c.x + UNITY_MATRIX_V[1].xyz * (c.y * _RingParams.y)) * r;
                    OUT.positionHCS = TransformWorldToHClip(w);
                }
                else
                {
                    float3 lo = DriftCurveWS(float3(b.x, 0.0, b.z));
                    float3 hi = DriftCurveWS(float3(b.x, _BeaconParams.w, b.z));
                    float3 mid = 0.5 * (lo + hi);
                    float3 side = cross(hi - lo, mid - cam);
                    side = dot(side, side) > 1e-8 ? normalize(side) : UNITY_MATRIX_V[0].xyz;
                    float wdt = max(0.16, _BeaconParams.y * 0.5 * length(mid - cam));
                    OUT.positionHCS = TransformWorldToHClip(lerp(lo, hi, c.y * 0.5 + 0.5) + side * (c.x * wdt));
                }
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv.xy;
                float part = IN.uv.z;
                float k = IN.uv.w * IN.data.y;
                float t = _Time.y;
                float ph = IN.data.z;
                float night = saturate(_BeaconColor.a);
                float3 col = _BeaconColor.rgb;
                float g = 0.0;
                if (part < 0.5)
                {
                    float r2 = dot(c, c);
                    float pulse = 0.82 + 0.18 * sin(t * 3.0 + ph * 2.0);
                    g = (exp(-r2 * 4.5) * 0.55 + exp(-r2 * 30.0) * 0.9) * pulse * IN.data.x;
                    // By day a white-hot core, at night a warmer, wider glow.
                    col = lerp(col, float3(1.0, 0.95, 0.85), exp(-r2 * 30.0) * (1.0 - 0.5 * night));
                }
                else if (part < 1.5)
                {
                    float r = length(c);
                    float wave = frac(t * 0.55 + ph * 0.37);
                    float at = 0.2 + 0.78 * wave;
                    float dr = (r - at) / 0.075;
                    float ripple = exp(-dr * dr) * (1.0 - wave) * (1.0 - wave);
                    float disc = exp(-r * r * 7.0) * (0.25 + 0.2 * night);
                    g = (ripple * 0.9 + disc) * step(r, 1.0);
                }
                else if (part < 2.5)
                {
                    float across = 1.0 - abs(c.x);
                    float v = c.y * 0.5 + 0.5;
                    g = across * across * pow(saturate(1.0 - v), 1.6) * 0.5;
                }
                else
                {
                    // The watch marker's line (sprite CircleRing: 8 % of the radius), never thinner than ~1.5 px and
                    // anti-aliased; around it a faint dark edge that lifts it off the bright day sea.
                    float rr = length(c) * RING_QUAD;
                    float px = max(fwidth(rr), 1e-4);
                    float hw = max(0.045, 0.75 * px);
                    float off = abs(rr - 0.96);
                    float ln = 1.0 - smoothstep(hw - px, hw + px, off);
                    float edge = (1.0 - smoothstep(hw, hw + 0.16 + 1.5 * px, off)) * _RingParams.z * (1.0 - 0.6 * night);
                    float pulse = 0.8 + 0.2 * (0.5 + 0.5 * sin(t * 2.5 + ph));
                    float a = ln * _RingColor.a;
                    float cover = (a + edge * (1.0 - a)) * k * pulse;
                    return float4(_RingColor.rgb * (a * k * pulse), cover);
                }
                return float4(col * (g * k), 0.0);
            }
            ENDHLSL
        }
    }
}
