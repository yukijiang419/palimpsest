Shader "Caffeine/Archive Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.025, 0.045, 0.085, 1)
        _Horizon ("Horizon", Color) = (0.14, 0.20, 0.27, 1)
        _Ground ("Ground", Color) = (0.055, 0.075, 0.09, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Zenith, _Horizon, _Ground;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;
                float upper = smoothstep(-0.03, 0.82, y);
                float lower = smoothstep(-0.7, 0.04, y);
                fixed3 sky = lerp(_Ground.rgb, _Horizon.rgb, lower);
                sky = lerp(sky, _Zenith.rgb, upper);
                float haze = exp(-abs(y - 0.01) * 18.0);
                sky += haze * fixed3(0.025, 0.018, 0.012);
                return fixed4(sky, 1);
            }
            ENDCG
        }
    }
}
