Shader "Ninjas4744/WhiteVinyl"
{
    Properties
    {
        _BaseMap("Original sponsor alpha",2D)="white"{}
        _BaseColor("Vinyl color",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10"}
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1,-1
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END
            struct Input {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Output {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            Output Vert(Input input)
            {
                Output output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                return output;
            }
            half4 Frag(Output input):SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a;
                return half4(_BaseColor.rgb,alpha*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
