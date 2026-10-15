Shader "Custom/UI/BallIndicatorGlow"
{
	Properties
	{
		[PerRendererData]
		_MainTex("Sprite Texture", 2D) = "white" {}

		_Color("Tint", Color) = (1, 1, 1, 1)

		[HDR]
		_GlowColor("Glow Color", Color) = (1, 1, 1, 1)

		_GlowIntensity(
			"Glow Intensity",
			Range(0.0, 4.0)) = 1.5

		_GlowWidth(
			"Glow Width",
			Range(0.0, 8.0)) = 2.0

		_GlowSoftness(
			"Glow Softness",
			Range(0.0, 1.0)) = 0.8

		_PulseSpeed(
			"Pulse Speed",
			Range(0.0, 10.0)) = 2.0

		_PulseAmount(
			"Pulse Amount",
			Range(0.0, 1.0)) = 0.08

		_StencilComp(
			"Stencil Comparison",
			Float) = 8

		_Stencil(
			"Stencil ID",
			Float) = 0

		_StencilOp(
			"Stencil Operation",
			Float) = 0

		_StencilWriteMask(
			"Stencil Write Mask",
			Float) = 255

		_StencilReadMask(
			"Stencil Read Mask",
			Float) = 255

		_ColorMask(
			"Color Mask",
			Float) = 15

		[Toggle(UNITY_UI_ALPHACLIP)]
		_UseUIAlphaClip(
			"Use Alpha Clip",
			Float) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
			"CanUseSpriteAtlas" = "True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]

		// Glow用Imageなので加算合成します。
		Blend SrcAlpha One

		ColorMask [_ColorMask]

		Pass
		{
			Name "BallIndicatorSoftGlow"

			CGPROGRAM

			#pragma vertex Vert
			#pragma fragment Frag
			#pragma target 3.0

			#include "UnityCG.cginc"
			#include "UnityUI.cginc"

			#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
			#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

			struct Attributes
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				float4 worldPosition : TEXCOORD1;
			};

			sampler2D _MainTex;

			fixed4 _Color;
			fixed4 _GlowColor;

			float4 _MainTex_TexelSize;
			float4 _ClipRect;

			float _GlowIntensity;
			float _GlowWidth;
			float _GlowSoftness;

			float _PulseSpeed;
			float _PulseAmount;

			/// <summary>
			/// UI頂点を描画用座標へ変換します。
			/// </summary>
			Varyings Vert(
				Attributes input)
			{
				Varyings output;

				output.worldPosition =
					input.vertex;

				output.vertex =
					UnityObjectToClipPos(
						input.vertex);

				output.texcoord =
					input.texcoord;

				output.color =
					input.color *
					_Color;

				return output;
			}

			/// <summary>
			/// SpriteのAlpha値を取得します。
			/// </summary>
			fixed SampleAlpha(
				float2 uv)
			{
				return tex2D(
					_MainTex,
					uv).a;
			}

			/// <summary>
			/// 指定半径の周囲8方向をサンプリングし、
			/// Alphaの平均値を返します。
			/// </summary>
			fixed SampleRing(
				float2 uv,
				float radius)
			{
				float2 offset =
					_MainTex_TexelSize.xy *
					_GlowWidth *
					radius;

				float2 diagonalOffset =
					offset *
					0.70710678;

				fixed alpha =
					0.0;

				alpha +=
					SampleAlpha(
						uv +
						float2(
							offset.x,
							0.0));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							-offset.x,
							0.0));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							0.0,
							offset.y));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							0.0,
							-offset.y));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							diagonalOffset.x,
							diagonalOffset.y));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							-diagonalOffset.x,
							diagonalOffset.y));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							diagonalOffset.x,
							-diagonalOffset.y));

				alpha +=
					SampleAlpha(
						uv +
						float2(
							-diagonalOffset.x,
							-diagonalOffset.y));

				return alpha /
					8.0;
			}

			/// <summary>
			/// 複数距離のAlphaを細かく混ぜ、
			/// 連続した柔らかい発光を生成します。
			/// </summary>
			fixed4 Frag(
				Varyings input) : SV_Target
			{
				fixed centerAlpha =
					SampleAlpha(
						input.texcoord);

				// 0.5刻みで取得することで、
				// 離れた輪郭が何本も見える状態を防ぎます。
				fixed ring05 =
					SampleRing(
						input.texcoord,
						0.5);

				fixed ring10 =
					SampleRing(
						input.texcoord,
						1.0);

				fixed ring15 =
					SampleRing(
						input.texcoord,
						1.5);

				fixed ring20 =
					SampleRing(
						input.texcoord,
						2.0);

				fixed ring25 =
					SampleRing(
						input.texcoord,
						2.5);

				fixed ring30 =
					SampleRing(
						input.texcoord,
						3.0);

				fixed ring35 =
					SampleRing(
						input.texcoord,
						3.5);

				fixed ring40 =
					SampleRing(
						input.texcoord,
						4.0);

				// Softnessが低い場合に使う、
				// 輪郭付近へ集中したぼかしです。
				fixed tightBlur =
					ring05 * 0.38 +
					ring10 * 0.30 +
					ring15 * 0.20 +
					ring20 * 0.12;

				// Softnessが高い場合に使う、
				// 外側まで滑らかに広がるぼかしです。
				fixed softBlur =
					ring05 * 0.20 +
					ring10 * 0.18 +
					ring15 * 0.16 +
					ring20 * 0.14 +
					ring25 * 0.11 +
					ring30 * 0.09 +
					ring35 * 0.07 +
					ring40 * 0.05;

				fixed blurAlpha =
					lerp(
						tightBlur,
						softBlur,
						_GlowSoftness);

				// Softnessを上げるほど、
				// 薄い部分も残して「ぼわっ」と見せます。
				float blurPower =
					lerp(
						1.0,
						0.55,
						_GlowSoftness);

				blurAlpha =
					pow(
						saturate(
							blurAlpha),
						blurPower);

				// 中心も少し発光させることで、
				// 外周だけが独立した線に見えるのを防ぎます。
				fixed innerGlow =
					centerAlpha *
					0.22;

				fixed glowAlpha =
					saturate(
						max(
							blurAlpha,
							innerGlow));

				float pulse =
					1.0 +
					sin(
						_Time.y *
						_PulseSpeed) *
					_PulseAmount;

				float finalIntensity =
					_GlowIntensity *
					max(
						0.0,
						pulse);

				glowAlpha *=
					_GlowColor.a *
					input.color.a;

				#ifdef UNITY_UI_CLIP_RECT

				glowAlpha *=
					UnityGet2DClipping(
						input.worldPosition.xy,
						_ClipRect);

				#endif

				#ifdef UNITY_UI_ALPHACLIP

				clip(
					glowAlpha -
					0.001);

				#endif

				fixed3 finalColor =
					_GlowColor.rgb *
					input.color.rgb *
					finalIntensity;

				return fixed4(
					finalColor,
					glowAlpha);
			}

			ENDCG
		}
	}
}