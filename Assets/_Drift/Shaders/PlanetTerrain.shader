Shader "Drift/PlanetTerrain"
{
    Properties
    {
        _PlanetCenter ("Planet Center (World)", Vector) = (0,0,0,0)
        _PlanetRadius ("Planet Radius", Float) = 50
        _OceanColor ("Ocean Color", Color) = (0.16,0.38,0.62,1)
        _SandColor ("Sand Color", Color) = (0.82,0.75,0.52,1)
        _GrassColor ("Grass Color", Color) = (0.28,0.55,0.24,1)
        _RockColor ("Rock Color", Color) = (0.55,0.52,0.48,1)
        _SandThreshold ("Sand Threshold", Float) = 0.5
        _GrassThreshold ("Grass Threshold", Float) = 2.5
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            float4 _PlanetCenter;
            float _PlanetRadius;
            float4 _OceanColor, _SandColor, _GrassColor, _RockColor;
            float _SandThreshold, _GrassThreshold;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float elevation = length(IN.positionWS - _PlanetCenter.xyz) - _PlanetRadius;

                float3 col = _OceanColor.rgb;
                col = lerp(col, _SandColor.rgb, smoothstep(-0.05, 0.05, elevation));
                col = lerp(col, _GrassColor.rgb, smoothstep(_SandThreshold * 0.5, _SandThreshold, elevation));
                col = lerp(col, _RockColor.rgb, smoothstep(_GrassThreshold * 0.7, _GrassThreshold, elevation));

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction)) * 0.7 + 0.3;
                return float4(col * ndotl * mainLight.color, 1);
            }
            ENDHLSL
        }
    }
}
