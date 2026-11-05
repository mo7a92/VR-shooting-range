// Riflescope eyepiece: shows the scope camera's render texture inside a circular field stop with a PU-style reticle
// (two side bars and a pointed bottom post). The exit-pupil shadow follows the eye: looking down the scope off-axis
// darkens the image from one side, like a real scope. Inactive (scope camera off) it is dark coated glass.
// URP unlit, single-pass-instanced stereo aware.
Shader "VRRange/ScopeLens"
{
    Properties
    {
        _ScopeTex ("Scope view", 2D) = "black" {}
        _Active ("Active", Float) = 0
        _ReticleColor ("Reticle colour", Color) = (0.02, 0.02, 0.02, 1)
        _ReticleWidth ("Reticle line half-width (uv)", Float) = 0.007
        _EyeBox ("Exit pupil softness", Float) = 1.6
        _GlassColor ("Inactive glass colour", Color) = (0.03, 0.045, 0.06, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ScopeTex); SAMPLER(sampler_ScopeTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _ScopeTex_ST;
                float _Active;
                float4 _ReticleColor;
                float _ReticleWidth;
                float _EyeBox;
                float4 _GlassColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 eyeOS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                // eye position in the lens frame (lens faces -z, the eye sits at negative z)
                o.eyeOS = TransformWorldToObject(GetCameraPositionWS()) - v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.uv - 0.5;
                float r = length(p);
                if (_Active < 0.5)
                {
                    // coated glass with a faint bluish sheen towards the rim
                    return half4(_GlassColor.rgb + 0.06 * smoothstep(0.2, 0.5, r) * float3(0.3, 0.45, 0.6), 1);
                }
                // exit pupil: the bright disc shifts against the field stop when the eye is off the optical axis
                float dz = max(-i.eyeOS.z, 0.01);
                float2 shift = i.eyeOS.xy / dz * 0.5 * _EyeBox;
                float pupil = 1.0 - smoothstep(0.30, 0.48, length(p + shift));
                float stop = 1.0 - smoothstep(0.46, 0.5, r);
                half3 col = SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, i.uv).rgb;
                // PU reticle: horizontal bars from the edge to 0.08 of the centre, bottom post tapering to a point at the centre
                float w = _ReticleWidth;
                float bars = step(abs(p.y), w) * step(0.08, abs(p.x));
                float post = step(p.y, 0.0) * step(abs(p.x), w * 1.6 * saturate(-p.y / 0.12));
                float reticle = saturate(bars + post);
                col = lerp(col, _ReticleColor.rgb, reticle);
                return half4(col * pupil * stop, 1);
            }
            ENDHLSL
        }
    }
}
