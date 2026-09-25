Shader "Drift/CurvedSky"
{
    // Sky backdrop for the curved world (Drift.Visuals.CurvedWorld owns the mesh: a unit sphere whose vertices
    // are directions). The skybox puts its horizon at eye level, but the bent sea ends at a limb up to
    // 45 degrees below it, which would show the ground half of the skybox. This dome is drawn around whichever
    // bent camera renders it (_CurveSkyDraw, pushed per camera: the island preview and portrait cameras keep
    // their own clear colour), just inside the far plane, and starts its gradient at the limb, all the way round
    // the planet: _CurveFogColor there (the colour the sea and far islands fade into). The sky itself is DriftSky.hlsl.
    // Drawn right after the water (Transparent+1, before every other transparent) and not on any pixel the water
    // marked in the stencil: in the portrait chase view the sea covers nearly the whole screen, and the dome used to
    // be shaded (and overdrawn) behind all of it. Drift/Water blends _CurveFogColor - what the dome showed below the
    // limb - under itself where nothing opaque lies behind it. The skybox is skipped for a camera that draws the dome.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "CurvedSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            // Depth stays written: transparents beyond 0.9 of the far plane (far cloud puffs) keep being hidden behind it.
            ZWrite On
            Stencil { Ref 8 ReadMask 8 Comp NotEqual }

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
