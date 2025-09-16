Shader "Unlit/NiceBG_Gradient"
{
	Properties
	{
		_ColA ("Color A (Teal)", Color) = (0.10, 0.75, 0.70, 1)
		_ColB ("Color B (Indigo)", Color) = (0.29, 0.00, 0.51, 1)
		_ColC ("Color C (Purple)", Color) = (0.48, 0.17, 0.75, 1)
		_ColD ("Color D (Blue)", Color) = (0.23, 0.52, 1.00, 1)
		_Speed ("Animation Speed", Range(0, 3)) = 0.35
		_Scale ("Subtle Wave Scale", Range(0.5, 10)) = 3.0
		_Distort ("Subtle Distortion", Range(0, 0.1)) = 0.02
		_Vignette ("Vignette Strength", Range(0, 1)) = 0.2
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Background" }
		LOD 100
		Cull Off
		ZWrite Off

		Pass
		{
			ZTest Always
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv     : TEXCOORD0;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv  : TEXCOORD0;
			};

			fixed4 _ColA, _ColB, _ColC, _ColD;
			float _Speed, _Scale, _Distort, _Vignette;

			v2f vert (appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv  = v.uv;
				return o;
			}

			// Simple 2D rotation around center 0.5
			float2 rotateAround01(float2 uv, float ang)
			{
				float2 p = uv - 0.5;
				float s = sin(ang), c = cos(ang);
				float2 r = float2(c * p.x - s * p.y, s * p.x + c * p.y);
				return r + 0.5;
			}

			// Soft vignette from center
			float vignette(float2 uv, float k)
			{
				float2 p = uv * (1 - uv); // 0 at edges, max ~0.25 at center
				float vig = pow(saturate(4.0 * p.x * p.y), 1.0 + 2.0 * k);
				return vig; // 0..1, center bright
			}

			fixed4 frag (v2f i) : SV_Target
			{
				// Animated UVs (very subtle)
				float t = _Time.y * _Speed;
				float2 uv = i.uv;

				// Tiny rotation and drift
				uv = rotateAround01(uv, sin(t * 0.5) * 0.08);
				uv += 0.02 * float2(sin(t * 0.35), cos(t * 0.27));

				// Subtle wavy distortion
				float2 w = float2(
					sin((uv.y * 6.28318 + t) * _Scale),
					cos((uv.x * 6.28318 - t * 0.8) * (_Scale * 0.85))
				);
				uv += _Distort * w;

				// Two orthogonal gradients blended together
				float gy = smoothstep(0.0, 1.0, saturate(uv.y));
				float gx = smoothstep(0.0, 1.0, saturate(uv.x));

				fixed3 ly = lerp(_ColA.rgb, _ColB.rgb, gy); // bottom -> top
				fixed3 lx = lerp(_ColC.rgb, _ColD.rgb, gx); // left -> right

				// Radial mix factor to emphasize center hue
				float2 pc = uv - 0.5;
				float r = length(pc) * 1.4142; // ~0..1 to corners
				float radial = smoothstep(0.2, 1.0, 1.0 - r);

				fixed3 col = lerp(lx, ly, 0.6) * (1 - radial) + lerp(ly, lx, 0.4) * radial;

				// Gentle vignette for focus
				float vig = vignette(i.uv, _Vignette);
				col *= lerp(1.0, vig, _Vignette);

				return fixed4(col, 1.0);
			}
			ENDCG
		}
	}
	Fallback Off
}

