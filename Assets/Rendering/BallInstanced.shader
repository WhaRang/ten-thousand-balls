// Draws every ball in one instanced call. The mesh is a unit icosphere; each instance reads its
// centre from a structured buffer by SV_InstanceID and scales the mesh by the ball radius. No
// per-instance matrices ever cross to the GPU: 12 bytes per ball per frame is the whole upload.
//
// Two passes: UniversalForward (Lambert from the main light + ambient, receives main-light shadows)
// and ShadowCaster (same vertex logic, writes depth into the shadow map). Deliberately no
// DepthOnly / DepthNormals passes: only SSAO and depth-based post effects need them and the scene
// has neither.
Shader "SdfBalls/BallInstanced"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.25, 0.45, 1.0, 1.0)
        _AccentColor ("Accent Color", Color) = (0.35, 0.9, 1.0, 1.0)
        _Radius ("Radius", Float) = 0.06
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // Shared by both passes so the instance lookup exists exactly once.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // One float3 per ball, uploaded straight from the simulation's positions array.
        StructuredBuffer<float3> _Positions;

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _AccentColor;
            float _Radius;
        CBUFFER_END

        // Object space = unit sphere, so a vertex's position is also its normal and no matrix is
        // needed: world position is centre + normal * radius, world normal is the normal itself.
        float3 BallPositionWS(uint instanceID, float3 normalOS)
        {
            return _Positions[instanceID] + normalOS * _Radius;
        }

        // PCG integer hash: turns an instance id into a well-spread value in [0, 1). Gives every
        // ball a stable tint for free, with no per-ball data uploaded.
        float HashToUnit(uint id)
        {
            uint state = id * 747796405u + 2891336453u;
            uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
            return ((word >> 22u) ^ word) / 4294967295.0;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Main-light shadow receiving: which shadow map layout URP is using this frame.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 albedo : COLOR0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = BallPositionWS(input.instanceID, input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = input.normalOS;
                output.albedo = lerp(_BaseColor.rgb, _AccentColor.rgb, HashToUnit(input.instanceID));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                // GetMainLight with a shadow coordinate fills in shadowAttenuation from the shadow map.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 direct = mainLight.color * lambert * mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normalWS);

                return half4(input.albedo * (direct + ambient), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Set by URP when rendering a point/spot light's shadow map instead of the directional one.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // URP's shadow caster passes read these; they are set per shadow-map render.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                float3 positionWS = BallPositionWS(input.instanceID, input.normalOS);
                float3 normalWS = input.normalOS;

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                // Standard URP shadow bias: nudge along the normal and the light so a surface does not
                // shadow itself (acne), then clamp to the near plane so nothing is clipped away.
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                Varyings output;
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0; // depth is all the shadow map wants; colour writes are masked off anyway
            }
            ENDHLSL
        }
    }
}
