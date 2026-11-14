Shader "Caffeine/Judgement Cut"
{
    Properties
    {
        _MainTex ("Slash mask or refraction field", 2D) = "white" {}
        [HDR] _Tint ("Slash tint", Color) = (1,1,1,1)
        _DistortionPixels ("Refraction pixels", Range(0,12)) = 5
        _Fade ("Fade", Range(0,1)) = 1
        [Toggle] _Refraction ("Refraction mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        // One uniform material mode selects shading and destination blending.
        // Refraction renders first; the named capture is shared across casts.
        GrabPass { "_JudgementSceneColor" }
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _JudgementSceneColor;
        half4 _Tint;
        half _Fade;
        float _DistortionPixels;
        half _Refraction;
        struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
        struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 grab:TEXCOORD1; fixed4 color:COLOR; };
        v2f vert(appdata v)
        {
            v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
            o.grab=ComputeGrabScreenPos(o.vertex); o.color=v.color; return o;
        }
        ENDCG
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i):SV_Target
            {
                if (_Refraction > 0.5)
                {
                    half4 field=tex2D(_MainTex,i.uv);
                    half mask=field.a*i.color.a*_Fade;
                    float pulse=0.85+0.15*sin(_Time.y*22);
                    float2 offset=(field.rg*2-1)*_DistortionPixels/_ScreenParams.xy;
                    float4 grab=i.grab;
                    grab.xy+=offset*mask*pulse*grab.w;
                    half3 scene=tex2Dproj(_JudgementSceneColor,UNITY_PROJ_COORD(grab)).rgb;
                    return half4(scene*(1-mask*0.035),mask*0.8);
                }
                half4 shape=tex2D(_MainTex,i.uv.yx);
                half3 tint=i.color.rgb*_Tint.rgb;
                half3 hot=lerp(tint,half3(1.8,1.95,2.2),shape.r*0.5);
                return half4(hot,shape.a*i.color.a*_Tint.a*_Fade);
            }
            ENDCG
        }
    }
    FallBack Off
}
