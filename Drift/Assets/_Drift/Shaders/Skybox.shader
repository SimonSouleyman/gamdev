Shader "Drift/Skybox"
{
    // RenderSettings.skybox fallback for cameras that clear to the skybox but do not get the curved world's dome
    // (Drift/CurvedSky): the same sky (DriftSky.hlsl) with whatever horizon CurvedWorld pushed for that camera
    // (eye level for every camera that is not bent).
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DriftCurve.hlsl"
            #include "DriftClouds.hlsl"
            #include "DriftSky.hlsl"

            float _CurveSkyDraw;

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dir         : TEXCOORD0;
                float3 sdir        : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // A camera that draws Drift/CurvedSky needs no skybox: the dome covers everything the water does not
                // (it used to reject this pass by depth, but it is drawn after the water now).
                if (_CurveSkyDraw > 0.5) OUT.positionHCS = float4(0, 0, 0, 1);
                OUT.dir = IN.positionOS.xyz;
                OUT.sdir = DriftSkyStarSpace(IN.positionOS.xyz);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return float4(DriftSky(normalize(IN.dir), normalize(IN.sdir), _WorldSpaceCameraPos, IN.positionHCS.xy), 1);
            }
            ENDHLSL
        }
    }
}
