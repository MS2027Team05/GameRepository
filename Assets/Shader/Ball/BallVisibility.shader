Shader "Custom/BallVisibility"
{
	Properties
	{
		[Header(Base)]
		_BaseMap("Base Texture", 2D) = "white" {}
		_BaseColor("Base Color", Color) = (1, 1, 1, 1)

		[Header(Rim Light)]
		[HDR] _RimColor("Rim Color", Color) = (1, 1, 1, 1)
		_RimPower("Rim Power", Range(0.5, 8.0)) = 3.0
		_RimIntensity("Rim Intensity", Range(0.0, 5.0)) = 1.0

		[Header(XRay)]
		[Toggle] _XRayMode("X-Ray Mode", Float) = 0
		[HDR] _XRayColor("X-Ray Color", Color) = (1, 1, 0, 1)
		_XRayOpacity("X-Ray Opacity", Range(0.0, 1.0)) = 0.4

		[Header(Render Settings)]
		[Enum(UnityEngine.Rendering.CompareFunction)]
		_ZTest("Depth Test", Float) = 4

		[Toggle]
		_ZWrite("Depth Write", Float) = 1

		[Enum(UnityEngine.Rendering.BlendMode)]
		_SrcBlend("Source Blend", Float) = 1

		[Enum(UnityEngine.Rendering.BlendMode)]
		_DstBlend("Destination Blend", Float) = 0

		[Enum(UnityEngine.Rendering.CullMode)]
		_Cull("Cull", Float) = 2
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
			Name "BallVisibility"
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			ZTest [_ZTest]
			ZWrite [_ZWrite]
			Blend [_SrcBlend] [_DstBlend]
			Cull [_Cull]

			HLSLPROGRAM

			#pragma vertex Vert
			#pragma fragment Frag
			#pragma target 3.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float2 uv : TEXCOORD2;
			};

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4 _BaseColor;

				half4 _RimColor;
				float _RimPower;
				float _RimIntensity;

				float _XRayMode;
				half4 _XRayColor;
				float _XRayOpacity;
			CBUFFER_END

			Varyings Vert(Attributes input)
			{
				Varyings output;

				VertexPositionInputs positionInputs =
					GetVertexPositionInputs(
						input.positionOS.xyz);

				VertexNormalInputs normalInputs =
					GetVertexNormalInputs(
						input.normalOS);

				output.positionHCS =
					positionInputs.positionCS;

				output.positionWS =
					positionInputs.positionWS;

				output.normalWS =
					normalInputs.normalWS;

				output.uv =
					TRANSFORM_TEX(
						input.uv,
						_BaseMap);

				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half3 normalWS =
					normalize(input.normalWS);

				half3 viewDirectionWS =
					SafeNormalize(
						GetWorldSpaceViewDir(
							input.positionWS));

				half rim =
					pow(
						saturate(
							1.0h -
							dot(
								normalWS,
								viewDirectionWS)),
						_RimPower);

				if (_XRayMode > 0.5)
				{
					half3 xRayColor =
						_XRayColor.rgb +
						(_RimColor.rgb *
							rim *
							_RimIntensity);

					return half4(
						xRayColor,
						_XRayOpacity *
						_XRayColor.a);
				}

				half4 baseSample =
					SAMPLE_TEXTURE2D(
						_BaseMap,
						sampler_BaseMap,
						input.uv);

				half3 baseColor =
					baseSample.rgb *
					_BaseColor.rgb;

				Light mainLight =
					GetMainLight();

				half lightAmount =
					saturate(
						dot(
							normalWS,
							mainLight.direction));

				half3 ambientLight =
					SampleSH(normalWS);

				half3 lighting =
					ambientLight +
					(mainLight.color *
						lightAmount);

				half3 rimColor =
					_RimColor.rgb *
					rim *
					_RimIntensity;

				half3 finalColor =
					(baseColor * lighting) +
					rimColor;

				return half4(
					finalColor,
					1.0h);
			}

			ENDHLSL
		}
	}
}