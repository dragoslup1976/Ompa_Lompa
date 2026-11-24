Shader "OmpaLompa/VideoWhiteKey"
{
    // Unlit shader that makes the white background of a video transparent.
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Threshold ("White threshold (lower = removes more)", Range(0, 1)) = 0.85
        _Softness ("Edge softness", Range(0.001, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Threshold;
            float _Softness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);

                // How white is this pixel? (all three channels high = white)
                float whiteness = min(c.r, min(c.g, c.b));
                float a = 1.0 - smoothstep(_Threshold, saturate(_Threshold + _Softness), whiteness);

                // Remove the white halo from semi-transparent edge pixels.
                float3 rgb = a > 0.001 ? saturate((c.rgb - (1.0 - a)) / a) : c.rgb;

                return fixed4(rgb * _Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
}
