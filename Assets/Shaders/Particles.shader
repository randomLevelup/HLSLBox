Shader "Unlit/Particles2D_QuadDots"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_SoftEdge ("Soft Edge", Range(0,1)) = 0.35
		_RadiusUV ("Radius UV", Vector) = (0.01, 0.01, 0, 0)
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
		LOD 100
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			StructuredBuffer<float2> _Positions;
			int _ParticleCount;
			float2 _RadiusUV;
			float _SoftEdge;
			fixed4 _Color;

			v2f vert (appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv; // assume quad uv 0..1 covers bounds
				return o;
			}

			// Anti-aliased circle alpha falloff
			float circle(float2 p, float2 center, float2 r)
			{
				// Convert to anisotropic distance by scaling axes by radius
				float2 q = (p - center) / r;
				float d = length(q); // 1 at edge
				// Soft edge width as fraction of radius
				float w = saturate(_SoftEdge * 0.99);
				// Inside (d<=1) alpha ~1, then fades to 0 over softness band
				float a = 1.0 - smoothstep(1.0 - w, 1.0, d);
				return a;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				float2 uv = saturate(i.uv);
				// Accumulate alpha from all particles
				// Note: O(n) per pixel on the fragment shader; keep counts modest.
				float a = 0.0;
				[loop]
				for (int idx = 0; idx < _ParticleCount; idx++)
				{
					float2 c = _Positions[idx];
					a += circle(uv, c, _RadiusUV);
				}
				// Prevent overbright - map to 0..1 with soft saturation
				a = saturate(a);
				return fixed4(_Color.rgb, a * _Color.a);
			}
			ENDCG
		}
	}
	Fallback Off
}

