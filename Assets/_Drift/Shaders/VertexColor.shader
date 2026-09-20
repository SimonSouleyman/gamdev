Shader "Drift/VertexColor"
{
    // Vertex-colour look with cloud shadows. Two optional animations, both zero by default:
    //   - legacy alpha sway/flap: colour alpha * _Sway (lateral) and * _Flap (vertical; the flock wings)
    //   - Phase 5 wind sway from the vertex channel UV0 = (height weight 0..1, phase, vertex height above the
    //     ground in world units) baked by TemplateBatch.AddPlant: the vertex bends along the global _LifeWind
    //     (xy = wind direction * speed with storms folded in, z = storm 0..1, pushed by IslandLifeSystem once per
    //     frame) by weight^2 * height * _WindBend with a slow gust and a fast flutter that grows in storms.
    //     A mesh without the channel (herds, flocks, plate borders) reads zero and stays rigid.
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 1
        _Ambient ("Ambient", Range(0,1)) = 0.45
        _Sway ("Sway", Float) = 0
        _SwaySpeed ("Sway Speed", Float) = 2
        _Flap ("Flap", Float) = 0
        _FlapSpeed ("Flap Speed", Float) = 14
        _WindBend ("Wind Bend", Float) = 0.45
        _WindFlutter ("Wind Flutter", Float) = 0.18
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
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
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _LightAmount;
                float _Ambient;
                float _Sway;
                float _SwaySpeed;
                float _Flap;
                float _FlapSpeed;
                float _WindBend;
                float _WindFlutter;
            CBUFFER_END

            float4 _LifeWind;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y;
                float sway = IN.color.a * _Sway;
                posWS.x += sin(t * _SwaySpeed + posWS.x * 0.8 + posWS.z * 0.6) * sway;
                posWS.z += cos(t * _SwaySpeed * 0.9 + posWS.z * 0.7 - posWS.x * 0.5) * sway;
                posWS.y += sin(t * _FlapSpeed + posWS.x * 1.3 + posWS.z * 1.9) * IN.color.a * _Flap;

                float w = saturate(IN.sway.x);
                float phase = IN.sway.y;
                float storm = saturate(_LifeWind.z);
                float2 wind = _LifeWind.xy;
                float gust = 0.55 + 0.45 * sin(t * 0.6 + posWS.x * 0.12 + posWS.z * 0.09 + phase * 0.5);
                float flutter = sin(t * (3.2 + 3.0 * storm) + phase + posWS.x * 1.7 + posWS.z * 1.1) * (_WindFlutter + 0.5 * storm);
                float bend = w * w * IN.sway.z * _WindBend;
                float2 lean = wind * (gust + flutter) + float2(-wind.y, wind.x) * flutter * 0.35;
                posWS.xz += lean * bend;
                posWS.y -= length(lean) * bend * w * 0.25;

                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float nd = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float lit = lerp(1.0, (_Ambient + (1.0 - _Ambient) * nd) * CloudShadow(IN.positionWS.xz), _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                return float4(DriftFog(IN.color.rgb * _Tint.rgb * lit * lightCol, IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
