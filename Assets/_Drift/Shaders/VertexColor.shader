Shader "Drift/VertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 1
        _Ambient ("Ambient", Range(0,1)) = 0.45
        _Sway ("Sway", Float) = 0
        _SwaySpeed ("Sway Speed", Float) = 2
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _LightAmount;
                float _Ambient;
                float _Sway;
                float _SwaySpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float sway = IN.color.a * _Sway;
                posWS.x += sin(_Time.y * _SwaySpeed + posWS.x * 0.8 + posWS.z * 0.6) * sway;
                posWS.z += cos(_Time.y * _SwaySpeed * 0.9 + posWS.z * 0.7 - posWS.x * 0.5) * sway;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float nd = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float lit = lerp(1.0, _Ambient + (1.0 - _Ambient) * nd, _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                return float4(IN.color.rgb * _Tint.rgb * lit * lightCol, 1);
            }
            ENDHLSL
        }
    }
}
