Shader "Drift/CurvedSky"
{
    // Sky backdrop for the curved world (Drift.Visuals.CurvedWorld owns the mesh: a unit sphere whose vertices
    // are directions). The skybox puts its horizon at eye level, but the bent sea ends at a limb up to
    // 45 degrees below it, which would show the ground half of the skybox. This dome is drawn around whichever
    // bent camera renders it (_CurveSkyDraw, pushed per camera: the island preview and portrait cameras keep
    // their own clear colour), just inside the far plane, last of the opaques with depth write on (so the skybox
    // pass is rejected behind it), and starts its gradient at the limb, all the way round the planet:
    // _CurveFogColor there (the colour the sea and far islands fade into). The sky itself is DriftSky.hlsl.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+480" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "CurvedSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

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
                float3 dir = normalize(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(_WorldSpaceCameraPos + dir * (_ProjectionParams.z * 0.9));
                if (_CurveSkyDraw < 0.5) OUT.positionHCS = float4(0, 0, 0, 1);
                OUT.dir = dir;
                OUT.sdir = DriftSkyStarSpace(dir);
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
