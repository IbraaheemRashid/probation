// The identity shader.
//
// The ward is deliberately flat, clean and cold. The patients are the only organic thing in the
// building, and this is what makes that read: light passes through them, they glow from the
// inside, and the glow beats at their real heart rate (see PatientAppearance).
Shader "Probation/PatientFlesh"
{
    Properties
    {
        _BaseColor      ("Surface", Color)            = (0.62, 0.38, 0.46, 1)
        _DeepColor      ("Interior glow", Color)      = (0.95, 0.25, 0.42, 1)
        _RimColor       ("Rim", Color)                = (0.55, 0.90, 0.85, 1)
        _SickColor      ("Sick tint", Color)          = (0.95, 0.85, 0.30, 1)

        _Translucency   ("Translucency", Range(0,3))  = 1.4
        _WrapLight      ("Light wrap", Range(0,1))    = 0.55
        _RimPower       ("Rim falloff", Range(0.5,8)) = 3.0
        _RimStrength    ("Rim strength", Range(0,3))  = 0.9

        _VeinScale      ("Vein scale", Range(1,40))   = 11
        _VeinStrength   ("Vein strength", Range(0,1)) = 0.35

        _Pulse          ("Pulse (driven)", Range(0,1))    = 0
        _PulseStrength  ("Pulse strength", Range(0,4))    = 1.6
        _Sickness       ("Sickness (driven)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _DeepColor;
            float4 _RimColor;
            float4 _SickColor;
            float  _Translucency;
            float  _WrapLight;
            float  _RimPower;
            float  _RimStrength;
            float  _VeinScale;
            float  _VeinStrength;
            float  _Pulse;
            float  _PulseStrength;
            float  _Sickness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

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

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = nrm.normalWS;
                return OUT;
            }

            // Cheap value noise - enough for the suggestion of something under the surface.
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 L = mainLight.direction;
                float3 lightColour = mainLight.color * mainLight.shadowAttenuation;

                // Wrapped diffuse. Flesh does not fall to black at the terminator - light bleeds
                // round the curve, and that soft shoulder is most of the effect.
                float wrapped = saturate((dot(N, L) + _WrapLight) / (1.0 + _WrapLight));

                // Light coming through from behind. This is what sells thickness.
                float back = pow(saturate(dot(-V, L)), 3.0) * _Translucency;

                float veins = Noise(IN.positionWS * _VeinScale + float3(0, _Time.y * 0.12, 0));
                veins = smoothstep(0.42, 0.72, veins) * _VeinStrength;

                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;
                float beat = _Pulse * _PulseStrength;

                float3 surface = lerp(_BaseColor.rgb, _SickColor.rgb, _Sickness);
                float3 interior = _DeepColor.rgb * (0.35 + beat);

                float3 colour = surface * (wrapped * lightColour + 0.18);
                colour += interior * (back + veins * (0.5 + beat * 0.5));
                colour += _RimColor.rgb * rim * (0.6 + beat * 0.4);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Written out rather than pulled in with UsePass. The URP Lit pass names are not what
        // you would guess, and getting them wrong drops every subshader with a bare
        // "Shader Unsupported - All subshaders removed" and no hint as to why.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            float4 ShadowVert (ShadowAttributes IN) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                return positionCS;
            }

            half4 ShadowFrag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 DepthVert (float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 DepthFrag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
