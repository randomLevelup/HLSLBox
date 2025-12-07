Shader "Unlit/Polygon"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_LineWidth ("Line Width (UV)", Float) = 0.01
		_SoftEdge ("Soft Edge", Range(0,1)) = 0.75
		_ShowVertices ("Show Vertices", Float) = 0
		_VertexRadiusUV ("Vertex Radius UV", Vector) = (0.01, 0.01, 0, 0)
		[HideInInspector]_PositionsTex ("Positions", 2D) = "black" {}
		[HideInInspector]_ParticleTexSize ("Positions Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_ParticleCount ("Particle Count", Int) = 0
		[HideInInspector]_VirtualPositionsTex ("Virtual Positions", 2D) = "black" {}
		[HideInInspector]_VirtualTexSize ("Virtual Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_VirtualCount ("Virtual Count", Int) = 0
		[HideInInspector]_PolyIndicesTex ("Polygon Indices", 2D) = "black" {}
		[HideInInspector]_PolyTexSize ("Polygon Tex Size", Vector) = (1,1,0,0)
		[HideInInspector]_PolyCount ("Polygon Count", Int) = 0
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
			#pragma target 3.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_PositionsTex);
			SAMPLER(sampler_PositionsTex);
			TEXTURE2D(_VirtualPositionsTex);
			SAMPLER(sampler_VirtualPositionsTex);
			TEXTURE2D(_PolyIndicesTex);
			SAMPLER(sampler_PolyIndicesTex);

			float4 _ParticleTexSize; // xy = width,height
			float4 _VirtualTexSize;  // xy = width,height
			float4 _PolyTexSize;     // xy = width,height

			int _ParticleCount;
			int _VirtualCount;
			int _PolyCount;
			int _PolyClosed;

			float4 _Color;
			float _LineWidth; // in UV units
			float _SoftEdge;  // 0..1 fraction of width
			int _ShowVertices;
			float2 _VertexRadiusUV;

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

			int SamplePolyIndex(int idx)
			{
				float2 texSize = _PolyTexSize.xy;
				float2 uv = float2((idx + 0.5) / texSize.x, 0.5 / texSize.y);
				float raw = SAMPLE_TEXTURE2D(_PolyIndicesTex, sampler_PolyIndicesTex, uv).r;
				return (int)round(raw);
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

			int MaxIndex()
			{
				return _ParticleCount + _VirtualCount - 1;
			}

			float DrawPolygonLines(float2 uv)
			{
				// Return min distance to any edge defined by indices
				if (_PolyCount <= 0) return 1.0; // far away

				float d = 1e5;
				int last = _PolyClosed != 0 ? _PolyCount : _PolyCount - 1;
				int maxIdx = MaxIndex();
				[loop]
				for (int i = 0; i < last; i++)
				{
					int i0 = SamplePolyIndex(i);
					int i1 = SamplePolyIndex((i + 1) % _PolyCount);
					// Clamp safety (though CPU already clamps)
					i0 = clamp(i0, 0, maxIdx);
					i1 = clamp(i1, 0, maxIdx);
					float2 a = FetchPosition(i0);
					float2 b = FetchPosition(i1);
					d = min(d, sdSegment(uv, a, b));
				}
				return d;
			}

			// Anti-aliased circle alpha falloff (copied from Particles.shader)
			float circleAlpha(float2 p, float2 center, float2 r)
			{
				float2 q = (p - center) / r;
				float d = length(q);
				float w = saturate(_SoftEdge * 0.99);
				float a = 1.0 - smoothstep(1.0 - w, 1.0, d);
				return a;
			}

			float4 frag(v2f i) : SV_Target
			{
				float d = DrawPolygonLines(i.uv);
				// SDF to alpha: width with soft edge falloff
				float halfW = max(1e-6, _LineWidth * 0.5);
				float feather = _SoftEdge * halfW;
				float alphaLines = 1.0 - smoothstep(halfW - feather, halfW + feather, d);

				// Optionally draw vertex discs
				float alphaVerts = 0.0;
				if (_ShowVertices != 0 && _PolyCount > 0)
				{
					int count = _PolyCount;
					int maxIdx = MaxIndex();
					[loop]
					for (int k = 0; k < count; k++)
					{
						int idx = SamplePolyIndex(k);
						idx = clamp(idx, 0, maxIdx);
						float2 c = FetchPosition(idx);
						alphaVerts += circleAlpha(i.uv, c, _VertexRadiusUV);
					}
					alphaVerts = saturate(alphaVerts);
				}

				float alpha = saturate(alphaLines + alphaVerts - alphaLines * alphaVerts); // screen-like blend
				return float4(_Color.rgb, _Color.a * alpha);
			}
			ENDHLSL
		}
	}
}

