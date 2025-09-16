Shader "Custom/HLSL2D" {
	Properties {
		_Scale ("Grid Scale", Float) = 20.0
        _Speed ("Speed", Float) = 7.0
        _FieldStrength ("Field Influence", Float) = 0.0
	}
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // 2D hash for integer lattice points
            float hash21(float2 p, float w) {
                p = floor(p);
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123 + w);
            }

            // Value noise with smooth bilinear interpolation
            float valueNoise2D(float2 st, float w) {
                float2 i = floor(st);
                float2 f = frac(st);

                float a = hash21(i, w);
                float b = hash21(i + float2(1.0, 0.0), w);
                float c = hash21(i + float2(0.0, 1.0), w);
                float d = hash21(i + float2(1.0, 1.0), w);

                // Smooth interpolation curve
                float2 u = f * f * (3.0 - 2.0 * f);

                float nx0 = lerp(a, b, u.x);
                float nx1 = lerp(c, d, u.x);
                return lerp(nx0, nx1, u.y);
            }

            float _Scale;
            float _Speed; // replaces _W
            float _FieldStrength;

            sampler2D _ParticleFieldTex;
            float4 _ParticleFieldWorldMin;
            float4 _ParticleFieldWorldMax;

            float4 frag(v2f i) : SV_Target {
                float2 uv = i.uv * _Scale;
                float w = _Time.y * _Speed; // time-driven phase
                float n = valueNoise2D(uv, w / 100.0);

                // Sample particle field in world XY if provided
                float field = 0.0;
                if (_FieldStrength != 0.0)
                {
                    float2 fmin = _ParticleFieldWorldMin.xy;
                    float2 fmax = _ParticleFieldWorldMax.xy;
                    float2 denom = max(fmax - fmin, float2(1e-5, 1e-5));
                    float2 fuv = (i.worldPos.xy - fmin) / denom;
                    field = tex2D(_ParticleFieldTex, saturate(fuv)).r;
                }

                float v = saturate(n + field * _FieldStrength);
                return float4(v, v, v, 1.0);
            }
            ENDCG
        }
    }
}
