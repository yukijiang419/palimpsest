Shader "Caffeine/Archive Surface Pulse"
{
    Properties
    {
        _Tint ("Pulse colour", Color) = (.35, .67, 1, 1)
        _PulseOrigin ("World origin", Vector) = (0, 0, 0, 0)
        _PulseRadius ("Wave radius", Float) = 0
        _PulseWidth ("Band width", Float) = .65
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Back
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float3 _PulseOrigin;
            float _PulseRadius, _PulseWidth, _Fade;
            fixed4 _Tint;
            struct Input { float4 vertex : POSITION; };
            struct Output { float4 position : SV_POSITION; float3 world : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                float band = 1 - smoothstep(0, _PulseWidth, abs(distance(input.world, _PulseOrigin) - _PulseRadius));
                float lines = .65 + .35 * step(.55, frac(input.world.y * 12));
                float alpha = band * lines * _Fade;
                clip(alpha - .005);
                return fixed4(_Tint.rgb, alpha * _Tint.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
