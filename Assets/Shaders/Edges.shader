Shader "Unlit/Edges"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_LineWidth ("Line Width (UV)", Float) = 0.01
		_SoftEdge ("Soft Edge", Range(0,1)) = 0.75
		[HideInInspector]_PositionsTex ("Positions", 2D) = "black" {}
		[HideInInspector]_ParticleTexSize ("Positions Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_ParticleCount ("Particle Count", Int) = 0
		[HideInInspector]_EdgePairsTex ("Edge Pairs", 2D) = "black" {}
		[HideInInspector]_EdgeTexSize ("Edge Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_VirtualPositionsTex ("Virtual Positions", 2D) = "black" {}
		[HideInInspector]_VirtualTexSize ("Virtual Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_VirtualCount ("Virtual Count", Int) = 0
	}
	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_PositionsTex);
			SAMPLER(sampler_PositionsTex);
			TEXTURE2D(_EdgePairsTex);
			SAMPLER(sampler_EdgePairsTex);
			TEXTURE2D(_VirtualPositionsTex);
			SAMPLER(sampler_VirtualPositionsTex);

			float4 _ParticleTexSize; // xy = width,height
			float4 _EdgeTexSize;     // xy = width,height
			float4 _VirtualTexSize;  // xy = width,height

			int _ParticleCount;
			int _VirtualCount;
			int _EdgeCount;

			float4 _Color;
			float _LineWidth; // in UV units
			float _SoftEdge;  // 0..1 fraction of width

			struct appdata
			{
				float3 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.positionCS = TransformObjectToHClip(v.positionOS);
				o.uv = v.uv; // assume mesh is a quad with 0..1 UVs
				return o;
			}

			float sdSegment(float2 p, float2 a, float2 b)
			{
				float2 pa = p - a, ba = b - a;
				float denom = max(dot(ba, ba), 1e-8);
				float h = saturate(dot(pa, ba) / denom);
				return length(pa - ba * h);
			}

			float2 SamplePosition(int index)
			{
				float2 texSize = _ParticleTexSize.xy;
				float2 uv = float2((index + 0.5) / texSize.x, 0.5 / texSize.y);
				return SAMPLE_TEXTURE2D(_PositionsTex, sampler_PositionsTex, uv).rg;
			}

			float2 SampleVirtualPosition(int vIndex)
			{
				float2 texSize = _VirtualTexSize.xy;
				float2 uv = float2((vIndex + 0.5) / texSize.x, 0.5 / texSize.y);
				return SAMPLE_TEXTURE2D(_VirtualPositionsTex, sampler_VirtualPositionsTex, uv).rg;
			}

			int2 SampleEdgePair(int idx)
			{
				float2 texSize = _EdgeTexSize.xy;
				float2 uv = float2((idx + 0.5) / texSize.x, 0.5 / texSize.y);
				float2 raw = SAMPLE_TEXTURE2D(_EdgePairsTex, sampler_EdgePairsTex, uv).rg;
				return int2(round(raw));
			}

			float2 FetchPosition(int index)
			{
				if (index >= 0 && index < _ParticleCount)
				{
					return SamplePosition(index);
				}
				int v = index - _ParticleCount;
				if (v >= 0 && v < _VirtualCount)
				{
					return SampleVirtualPosition(v);
				}
				return float2(0, 0);
			}

			float minDistanceToEdges(float2 uv)
			{
				if (_EdgeCount <= 0 || _ParticleCount <= 0) return 1e5;

				float d = 1e5;
				[loop]
				for (int i = 0; i < _EdgeCount; i++)
				{
					int2 pair = SampleEdgePair(i);
					int i0 = pair.x;
					int i1 = pair.y;

					float2 a = FetchPosition(i0);
					float2 b = FetchPosition(i1);

					// Handle degenerate edge as a point
					if (all(a == b))
					{
						d = min(d, length(uv - a));
					}
					else
					{
						d = min(d, sdSegment(uv, a, b));
					}
				}
				return d;
			}

			float4 frag(v2f i) : SV_Target
			{
				float d = minDistanceToEdges(i.uv);

				// SDF to alpha with soft feather
				float halfW = max(1e-6, _LineWidth * 0.5);
				float feather = saturate(_SoftEdge) * halfW;
				float alpha = 1.0 - smoothstep(halfW - feather, halfW + feather, d);

				return float4(_Color.rgb, _Color.a * alpha);
			}
			ENDHLSL
		}
	}
}
