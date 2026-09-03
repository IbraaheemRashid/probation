// The sky over the ward.
//
// Black. Not dark blue, not "night" — actually black, so the only light in an exterior shot is
// whatever the building is leaking. The stars are procedural (no cubemap, no texture memory) and
// laid out on the six faces of the view cube so they stay round and evenly spread instead of
// bunching at the poles the way a lat/long mapping does.
//
// Three layers: a sparse layer of bright ones that read as individual stars, a mid layer, and a
// fine dust layer that only shows up once your eyes adjust. That spread is what stops it looking
// like noise.
Shader "Probation/StarSky"
{
    Properties
    {
        _SkyColor    ("Sky", Color)                 = (0, 0, 0, 1)
        _StarColor   ("Star tint", Color)           = (1, 1, 1, 1)

        _Density     ("Density", Range(8, 160))     = 46
        _Coverage    ("Coverage", Range(0, 1))      = 0.10
        _StarSize    ("Star size", Range(0.01, 0.5)) = 0.12
        _Exposure    ("Brightness", Range(0, 4))    = 1.0

        _Twinkle     ("Twinkle", Range(0, 1))       = 0.35
        _TwinkleSpeed("Twinkle speed", Range(0, 8)) = 1.6

        _HorizonFade ("Horizon fade", Range(0, 1))  = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Background"
            "Queue"          = "Background"
            "PreviewType"    = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyColor;
                float4 _StarColor;
                float  _Density;
                float  _Coverage;
                float  _StarSize;
                float  _Exposure;
                float  _Twinkle;
                float  _TwinkleSpeed;
                float  _HorizonFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // The skybox mesh is drawn with the camera pinned at the origin, so object space
                // position is the view direction.
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir        = v.positionOS.xyz;
                return o;
            }

            // Dave Hoskins' hash33. Cheap, no texture, no visible lattice.
            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Project a direction onto the dominant cube face. Returns uv in [-1,1] and a face id,
            // which keeps cells square-ish everywhere instead of pinching at the zenith.
            void CubeFace(float3 d, out float2 uv, out float faceId)
            {
                float3 a = abs(d);
                if (a.x >= a.y && a.x >= a.z)
                {
                    uv     = d.zy / a.x;
                    faceId = d.x > 0 ? 0.0 : 1.0;
                }
                else if (a.y >= a.z)
                {
                    uv     = d.xz / a.y;
                    faceId = d.y > 0 ? 2.0 : 3.0;
                }
                else
                {
                    uv     = d.xy / a.z;
                    faceId = d.z > 0 ? 4.0 : 5.0;
                }
            }

            // One grid of stars. Each cell either holds a star or doesn't; the ones that do get a
            // random position inside the cell so the grid never reads as a grid.
            float3 StarLayer(float2 uv, float faceId, float density, float coverage,
                             float sizeScale, float gain, float seed)
            {
                float2 g    = uv * density;
                float2 cell = floor(g);
                float2 f    = g - cell;

                float3 h = Hash33(float3(cell, faceId * 7.0 + seed));

                // Most cells are empty. Bail before doing the expensive part.
                float present = step(1.0 - coverage, frac(h.z * 41.17));
                if (present < 0.5) return 0.0;

                float3 h2 = Hash33(float3(cell + 19.7, faceId * 3.0 + seed + 5.1));

                // Magnitude: heavily weighted towards faint, so a handful of stars carry the eye.
                float mag = pow(h2.x, 3.5);

                float2 pos = float2(h.x, h.y);
                float  d   = length(f - pos);

                float core   = _StarSize * sizeScale * (0.45 + 0.55 * mag);
                float sharp  = pow(saturate(1.0 - d / max(core, 1e-4)), 6.0);
                float glow   = pow(saturate(1.0 - d / max(core * 3.5, 1e-4)), 3.0) * 0.18;
                float shape  = sharp + glow;

                float phase   = h2.y * 6.2831853;
                float rate    = _TwinkleSpeed * (0.6 + h2.z * 1.2);
                float twinkle = 1.0 + _Twinkle * sin(_Time.y * rate + phase);

                // Cool white through to a faint amber, so the field isn't a flat grey speckle.
                float3 tint = lerp(float3(0.72, 0.82, 1.0), float3(1.0, 0.86, 0.70), h2.z);

                return shape * (0.15 + mag) * gain * twinkle * tint;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);

                float2 uv;
                float  faceId;
                CubeFace(d, uv, faceId);

                float3 stars = 0.0;
                stars += StarLayer(uv, faceId, _Density * 0.55, _Coverage * 0.55, 1.60, 1.00,  0.0);
                stars += StarLayer(uv, faceId, _Density,        _Coverage,        1.00, 0.65, 13.0);
                stars += StarLayer(uv, faceId, _Density * 2.10, _Coverage * 0.75, 0.70, 0.30, 29.0);

                // Thin out towards the horizon — cheap stand-in for atmospheric extinction, and it
                // stops stars sitting on the rooftops.
                float horizon = smoothstep(-0.05, 0.35, d.y);
                stars *= lerp(1.0, horizon, _HorizonFade);

                float3 col = _SkyColor.rgb + stars * _StarColor.rgb * _Exposure;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
