Shader "Custom/PlayerOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.2, 0.2, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 10)) = 3.0
        [IntRange] _StencilRef ("Stencil Ref (Player ID)", Range(1, 15)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+10"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front          // 表面を捨てて裏面だけ描く(背面法)
            ZWrite On
            ZTest LEqual

            // 自分の本体がすでに描かれているピクセルには描かない
            Stencil
            {
                Ref  [_StencilRef]
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR; // R = アウトライン幅の倍率 (1=通常, 0=非表示)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 通常のクリップ座標
                float4 positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                // 法線をクリップ空間へ(ワールド→ビュー→射影)
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS);

                // 画面上で一定のピクセル幅になるように押し出す
                // 頂点カラーRを倍率として乗算(黒く塗った箇所は線が出ない)
                float2 offset = normalize(normalCS.xy)
                              * (_OutlineWidth * 2.0 / _ScreenParams.xy)
                              * positionCS.w
                              * IN.color.r;

                positionCS.xy += offset;
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
