// The camera's luma (Y) plane, uploaded as an R8 texture, shown as a grayscale image (row 0 = top of the image).
// Drawn as UI (transparent queue, no depth writes) so it sorts with the panel it sits on instead of fighting it.
Shader "MRDemo/GrayPreview"
{
    Properties { _MainTex ("Luma", 2D) = "black" {} }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = float2(i.uv.x, 1.0 - i.uv.y);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half y = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                return half4(y, y, y, i.color.a);
            }
            ENDHLSL
        }
    }
}
