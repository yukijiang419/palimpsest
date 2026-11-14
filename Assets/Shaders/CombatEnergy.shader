Shader "Caffeine/Combat Energy"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        [HDR] _Tint ("Tint", Color) = (0.3, 1.5, 2, 1)
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Tint;
            half _Fade;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }
            half4 frag(v2f input) : SV_Target
            {
                half4 shape = tex2D(_MainTex, input.uv);
                half4 color = input.color * _Tint * shape;
                color.a *= _Fade;
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
