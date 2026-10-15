Shader "Custom/ToolOccludedMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref (Mask ID)", Range(1, 255)) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+12" // 壁(2000)の後、プレイヤーシルエット(2015)の前
        }

        Pass
        {
            Name "ToolOccludedMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite Off
            ZTest Greater    // 壁の奥にあるピクセルにのみ印をつける
            ColorMask 0      // 色は一切描画しない

            // 壁裏の道具領域にステンシルIDを書き込む
            Stencil
            {
                Ref  [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
