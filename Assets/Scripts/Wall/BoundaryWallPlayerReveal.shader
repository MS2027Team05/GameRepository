Shader "Custom/BoundaryWallPlayerReveal"
{
	Properties
	{
		[MainTexture]
		_GridTexture("格子テクスチャ", 2D) = "white" {}

		[MainColor]
		_TintColor("色", Color) = (1, 1, 1, 1)

		_FullVisibleDistance("完全表示距離", Float) = 5.0
		_FadeDistance("フェード距離", Float) = 3.0
		_EmissionStrength("発光強度", Range(0.0, 5.0)) = 0.0
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
		}

		Pass
		{
			Name "ForwardUnlit"

			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off

			HLSLPROGRAM

			#pragma vertex Vert
			#pragma fragment Frag
			#pragma target 2.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
			};

			TEXTURE2D(_GridTexture);
			SAMPLER(sampler_GridTexture);

			CBUFFER_START(UnityPerMaterial)

			float4 _GridTexture_ST;
			float4 _TintColor;

			float _FullVisibleDistance;
			float _FadeDistance;
			float _EmissionStrength;

			CBUFFER_END

			// C#側から全Shader共通で渡される
			// ローカルプレイヤーのワールド座標です。
			float4 _LocalPlayerPosition;

			// ローカルプレイヤーが存在する場合は1、
			// 存在しない場合は0になります。
			float _LocalPlayerVisibilityEnabled;

			Varyings Vert(
				Attributes input)
			{
				Varyings output;

				VertexPositionInputs positionInputs =
					GetVertexPositionInputs(
						input.positionOS.xyz);

				output.positionCS =
					positionInputs.positionCS;

				output.positionWS =
					positionInputs.positionWS;

				output.uv =
					TRANSFORM_TEX(
						input.uv,
						_GridTexture);

				return output;
			}

			half4 Frag(
				Varyings input) : SV_Target
			{
				half4 textureColor =
					SAMPLE_TEXTURE2D(
						_GridTexture,
						sampler_GridTexture,
						input.uv);

				float distanceToPlayer =
					distance(
						input.positionWS,
						_LocalPlayerPosition.xyz);

				float safeFadeDistance =
					max(
						_FadeDistance,
						0.001);

				float fadeEndDistance =
					_FullVisibleDistance +
					safeFadeDistance;

				// プレイヤーに近いほど1、
				// 遠ざかるほど0になります。
				float distanceFade =
					1.0 -
					smoothstep(
						_FullVisibleDistance,
						fadeEndDistance,
						distanceToPlayer);

				distanceFade *=
					saturate(
						_LocalPlayerVisibilityEnabled);

				half finalAlpha =
					textureColor.a *
					_TintColor.a *
					distanceFade;

				// 完全に透明な部分は描画しません。
				clip(
					finalAlpha -
					0.001h);

				half3 finalColor =
					textureColor.rgb *
					_TintColor.rgb;

				finalColor *=
					1.0h +
					(half)_EmissionStrength;

				return half4(
					finalColor,
					finalAlpha);
			}

			ENDHLSL
		}
	}

	Fallback Off
}