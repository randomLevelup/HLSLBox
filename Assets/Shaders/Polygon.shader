Shader "Unlit/Polygon"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_LineWidth ("Line Width (UV)", Float) = 0.01
		_SoftEdge ("Soft Edge", Range(0,1)) = 0.75
	}
	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
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

			StructuredBuffer<int> _PolyIndices;
			int _PolyCount;
			int _PolyClosed;

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
				float2 uv : TEXCOORD0; // uv in 0..1
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.positionCS = TransformObjectToHClip(v.positionOS);
				o.uv = v.uv; // assume mesh provides quad 0..1
				return o;
			}

			float sdSegment(float2 p, float2 a, float2 b)
			{
				float2 pa = p - a, ba = b - a;
				float h = saturate(dot(pa, ba) / dot(ba, ba) + 1e-8);
				return length(pa - ba * h);
			}

			float DrawPolygonLines(float2 uv)
			{
				// Return min distance to any edge defined by indices
				if (_PolyCount <= 0) return 1.0; // far away

				float d = 1e5;
				int last = _PolyClosed != 0 ? _PolyCount : _PolyCount - 1;
				[loop]
				for (int i = 0; i < last; i++)
				{
					int i0 = _PolyIndices[i];
					int i1 = _PolyIndices[(i + 1) % _PolyCount];
					// Clamp safety (though CPU already clamps)
					i0 = clamp(i0, 0, _ParticleCount - 1);
					i1 = clamp(i1, 0, _ParticleCount - 1);
					float2 a = _Positions[i0];
					float2 b = _Positions[i1];
					d = min(d, sdSegment(uv, a, b));
				}
				return d;
			}

			float4 frag(v2f i) : SV_Target
			{
				float d = DrawPolygonLines(i.uv);
				// SDF to alpha: width with soft edge falloff
				float halfW = max(1e-6, _LineWidth * 0.5);
				float feather = _SoftEdge * halfW;
				float alpha = 1.0 - smoothstep(halfW - feather, halfW + feather, d);
				return float4(_Color.rgb, _Color.a * alpha);
			}
			ENDHLSL
		}
	}
}

