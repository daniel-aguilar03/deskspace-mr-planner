Shader "DeskSpace/ShadowReceiver"
{
    Properties { _Opacity("Shadow opacity", Range(0,1)) = 0.45 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ XR_HARD_OCCLUSION XR_SOFT_OCCLUSION
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../MRDemo/Shaders/Occlusion/OcclusionComputation.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
            CBUFFER_END
            struct A { float4 p:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A i)
            {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(i.p.xyz); o.p=TransformWorldToHClip(o.world); return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Light l=GetMainLight(TransformWorldToShadowCoord(i.world));
                float4 col=float4(0,0,0,(1-l.shadowAttenuation)*_Opacity);
                float4 result; SetOcclusion_float(i.world,col,result); return result;
            }
            ENDHLSL
        }
    }
}
