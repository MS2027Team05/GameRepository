Shader "Custom/OccludedSilhouette"
{
    Properties
    {
        _SilhouetteColor ("Silhouette Color", Color) = (0.5, 0.5, 0.5, 0.6)
        _RimPower ("Rim Power", Range(0.5, 8.0)) = 2.0
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.5
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Float) = 3 // 3 = Equal
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass Operation", Float) = 6 // 6 = IncrSat
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "OccludedSilhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite Off
            ZTest Greater
            Blend SrcAlpha OneMinusSrcAlpha

            // スクリプトから指定された条件でステンシル判定と描画済みマークを実行
            Stencil
            {
                Ref  [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SilhouetteColor;
                float  _RimPower;
                float  _RimIntensity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimIntensity;

                half4 col = _SilhouetteColor;
                col.rgb += rim * col.a;
                return col;
            }
            ENDHLSL
        }
    }
}
