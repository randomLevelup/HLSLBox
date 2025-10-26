Shader "Unlit/Edges"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_LineWidth ("Line Width (UV)", Float) = 0.01
		_SoftEdge ("Soft Edge", Range(0,1)) = 0.75
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
			#pragma target 4.5

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			StructuredBuffer<float2> _Positions; // UV-space [0..1]
			int _ParticleCount;

			StructuredBuffer<int2> _EdgePairs;   // pairs of indices
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

			float minDistanceToEdges(float2 uv)
			{
				if (_EdgeCount <= 0 || _ParticleCount <= 0) return 1e5;

				float d = 1e5;
				[loop]
				for (int i = 0; i < _EdgeCount; i++)
				{
					int2 pair = _EdgePairs[i];
					int i0 = clamp(pair.x, 0, _ParticleCount - 1);
					int i1 = clamp(pair.y, 0, _ParticleCount - 1);

					float2 a = _Positions[i0];
					float2 b = _Positions[i1];

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
