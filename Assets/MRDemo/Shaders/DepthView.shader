// Meta's environment depth (the texture AR Foundation hands to the occlusion shaders), one eye, as a colour map:
// near = warm, far = cool, black = no measurement. Drawn as UI.
Shader "MRDemo/DepthView"
{
    Properties { _Slice ("Eye (0 left, 1 right)", Float) = 0  _Near ("Near (m)", Float) = 0.3  _Far ("Far (m)", Float) = 4 }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"
            TEXTURE2D_ARRAY_FLOAT(_EnvironmentDepthTexture); SAMPLER(sampler_EnvironmentDepthTexture);
            float _Slice, _Near, _Far;
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.color = i.color; o.uv = i.uv; return o;
            }
            half3 Ramp(float t)   // a short turbo-like ramp
            {
                t = saturate(t);
                return saturate(half3(1.6 - abs(4.0 * t - 1.0) * 1.2, 1.5 - abs(4.0 * t - 2.0), 1.6 - abs(4.0 * t - 3.0) * 1.2));
            }
            half4 frag(Varyings i) : SV_Target
            {
                float d = SAMPLE_TEXTURE2D_ARRAY(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, i.uv, _Slice).r;
                if (d <= 0.0 || d >= 1.0) return half4(0, 0, 0, i.color.a);
                float metres = LinearizeDepth(ConvertDepthToSymmetricRange(d));
                return half4(Ramp((metres - _Near) / (_Far - _Near)), i.color.a);
            }
            ENDHLSL
        }
    }
}
