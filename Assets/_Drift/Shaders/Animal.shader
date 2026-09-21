Shader "Drift/Animal"
{
    // Drift/VertexColor plus per-animal animation baked by TemplateBatch.AddAnimal:
    //   UV0 = (pitch lever: local forward in world units, up: height above ground in world units, phase, hop amplitude)
    //   UV1 = (moving 0/1, alert 0/1, sleep 0/1, t0: state-change time on the _LifeClock)
    //   UV2 = (pitchFrom, pitchTo in degrees, restFrom, restTo 0..1)
    //   colour alpha = signed step lift of a leg vertex in world units (detail templates only, 0 otherwise)
    //   UV3 = procedural markings (Markings / DriftMarkings.hlsl: pattern coordinates in template units + code)
    // Pitch and rest blend from/to over _TransitionTime after t0 (_LifeClock is set by IslandHerdSystem;
    // when it is never set every animal shows its target pose), so a state change costs one mesh rebuild.
    // Moving animals hop (or bob when the amplitude is tiny); grazing ones swing the head; alert ones twitch
    // the rear; resting ones are lowered towards the ground by rest * _RestLower of their height; everyone
    // breathes, sleepers slower. Growth (Phase 3) is not a shader input: a young animal is baked smaller with a
    // paler vertex colour and the lever/up channels scale with it, so nothing here needs a per-animal size.
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 1
        _Ambient ("Ambient", Range(0,1)) = 0.45
        _HopSpeed ("Hop Speed", Float) = 9
        _GrazeSwing ("Graze Swing (deg)", Float) = 6
        _GrazeSpeed ("Graze Speed", Float) = 1.5
        _TransitionTime ("Pose Transition (s)", Float) = 0.8
        _RestLower ("Rest Lowering", Range(0,1)) = 0.38
        _Breath ("Breath Amount", Float) = 0.035
        _BreathSpeed ("Breath Speed", Float) = 2.4
        _Twitch ("Alert Twitch", Float) = 0.14
        _Markings ("Markings", Range(0,1)) = 1
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
            #include "DriftMarkings.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 anim0      : TEXCOORD0;
                float4 anim1      : TEXCOORD1;
                float4 anim2      : TEXCOORD2;
                float4 mark       : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 mark        : TEXCOORD2;
                nointerpolation float2 markId : TEXCOORD3;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _LightAmount;
                float _Ambient;
                float _HopSpeed;
                float _GrazeSwing;
                float _GrazeSpeed;
                float _TransitionTime;
                float _RestLower;
                float _Breath;
                float _BreathSpeed;
                float _Twitch;
                float _Markings;
            CBUFFER_END

            float _LifeClock;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float lever = IN.anim0.x;
                float up = IN.anim0.y;
                float phase = IN.anim0.z;
                float hopAmp = IN.anim0.w;
                float movingTo = saturate(IN.anim1.x);
                float alert = saturate(IN.anim1.y);
                float sleep = saturate(IN.anim1.z);
                float age = _LifeClock - IN.anim1.w;
                float blend = _LifeClock > 0.0 ? saturate(age / max(_TransitionTime, 0.01)) : 1.0;
                float gaitBlend = _LifeClock > 0.0 ? saturate(age / 0.25) : 1.0;
                float moving = lerp(1.0 - movingTo, movingTo, gaitBlend);
                float pitch = lerp(IN.anim2.x, IN.anim2.y, blend);
                float rest = lerp(IN.anim2.z, IN.anim2.w, blend);

                float t = _Time.y;
                float hop = abs(sin(t * _HopSpeed + phase)) * hopAmp * moving;
                float swing = _GrazeSwing * sin(t * _GrazeSpeed + phase) * (1.0 - moving) * (1.0 - alert) * (1.0 - sleep) * (1.0 - rest);
                float breath = sin(t * _BreathSpeed * (1.0 - 0.5 * sleep) + phase) * _Breath * up * (1.0 - moving);
                float behind = saturate(-lever * 60.0);
                float gate = step(0.78, frac(t * 0.43 + phase * 0.159));
                float twitch = alert * behind * gate * abs(sin(t * 21.0 + phase)) * _Twitch * up;
                // Detail templates: colour alpha = signed step lift in world units (diagonal leg pairs +/-), 0 elsewhere.
                float stride = sin(t * _HopSpeed + phase);
                float legLift = abs(IN.color.a) * saturate(IN.color.a > 0.0 ? stride : -stride) * moving;
                posWS.y += hop - sin((pitch + swing) * 0.0174533) * lever - rest * _RestLower * up + breath + twitch + legLift;
                OUT.positionHCS = DriftCurveHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                float markId, markStrength, markSeed;
                DriftMarkDecode(IN.mark.w, markId, markStrength, markSeed);
                OUT.mark = float4(IN.mark.xyz, markStrength);
                OUT.markId = float2(markId, markSeed);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float nd = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float lit = lerp(1.0, (_Ambient + (1.0 - _Ambient) * nd) * CloudShadow(IN.positionWS.xz), _LightAmount);
                float3 lightCol = lerp(float3(1,1,1), mainLight.color, _LightAmount);
                float3 col = DriftMarkings(IN.color.rgb, IN.mark, IN.markId.x, IN.markId.y, _Markings);
                return float4(DriftFog(col * _Tint.rgb * lit * lightCol, IN.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
