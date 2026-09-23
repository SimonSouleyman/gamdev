Shader "Drift/SpeedStreaks"
{
    // Screen-space speed streaks (Drift.Visuals.SpeedStreaks), drawn straight in clip space: no world position, no
    // ring bend, independent of the field of view. One quad per streak, built once:
    //   POSITION.xy = (along 0 tail .. 1 head, side -1..1)
    //   TEXCOORD0   = (angle around the focus in radians, seed 0..1, length scale, sector width in radians)
    // Each streak runs outward from _StreakShape.z (share of the way to the screen edge) past the screen edge, fading in
    // and out, and jumps to a new angle inside its sector every lap so the pattern never repeats visibly.
    Properties
    {
        _Color ("Farbe", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "SpeedStreaks"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            // x = opacity 0..1, y = lap clock, z = length (share of half the screen height), w = width
            float4 _StreakParams;
            // xy = focus in NDC (-1..1), z = inner radius, w = aspect (width / height)
            float4 _StreakShape;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 streak     : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 fade        : TEXCOORD0; // x = along, y = side, z = life
            };

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float along = v.positionOS.x;
                float side = v.positionOS.y;
                float seed = v.streak.y;

                float clock = _StreakParams.y * (0.75 + 0.5 * seed) + seed * 7.13;
                float lap = floor(clock);
                float t = clock - lap;
                float angle = v.streak.x + (Hash(lap * 3.7 + seed * 91.1) - 0.5) * v.streak.w * 0.8;
                float2 dir = float2(cos(angle), sin(angle));

                // Positions in units of half the screen height (x is divided by the aspect for clip space). The rays
                // are stretched to the screen's shape, so on a portrait phone the sideways streaks are not all cut
                // off by the near side edges.
                float aspect = max(0.1, _StreakShape.w);
                float2 ray = dir * float2(aspect, 1.0);
                float2 along2 = normalize(ray);
                float len = _StreakParams.z * v.streak.z * (0.4 + t);
                float r = lerp(_StreakShape.z, 2.2, t * t);
                float width = _StreakParams.w * (0.35 + t) * (0.7 + 0.6 * Hash(seed * 17.3));

                float2 p = float2(_StreakShape.x * aspect, _StreakShape.y) + ray * r + along2 * (len * along)
                         + float2(-along2.y, along2.x) * (side * width);
                float4 pos = float4(p.x / aspect, p.y, UNITY_NEAR_CLIP_VALUE * 0.5, 1.0);
                pos.y *= _ProjectionParams.x;
                o.positionHCS = pos;
                o.fade = float3(along, side, sin(t * PI));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float head = smoothstep(0.0, 1.0, i.fade.x);
                float edge = saturate(1.0 - abs(i.fade.y));
                float a = _Color.a * _StreakParams.x * head * edge * i.fade.z;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
