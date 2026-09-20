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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

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
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Wet, _Sand, _Grass, _Rock, _Snow, _Ash;
                float _GrassStart, _RockStart, _SnowStart, _Ambient;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = DriftCurveHClip(vpi.positionWS);
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float y = IN.positionWS.y;
                clip(y + 0.45);
                float3 n = normalize(IN.normalWS);

                float3 col = lerp(_Wet.rgb, _Sand.rgb, smoothstep(-0.05, 0.12, y));
                float3 groundCol = lerp(_Ash.rgb, _Grass.rgb * IN.color.rgb, IN.color.a);
                col = lerp(col, groundCol, smoothstep(_GrassStart - 0.1, _GrassStart + 0.25, y));
                col = lerp(col, _Rock.rgb, smoothstep(_RockStart, _RockStart + 0.7, y));
                col = lerp(col, _Snow.rgb, smoothstep(_SnowStart, _SnowStart + 0.6, y));

                float slope = 1.0 - saturate(n.y);
                col = lerp(col, _Rock.rgb, smoothstep(0.32, 0.55, slope) * smoothstep(0.25, 0.6, y));

                Light mainLight = GetMainLight();
                float nd = saturate(dot(n, mainLight.direction));
                float lit = _Ambient + (1.0 - _Ambient) * nd;
                lit *= CloudShadow(IN.positionWS.xz);
                return float4(DriftFog(col * lit * mainLight.color, IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
