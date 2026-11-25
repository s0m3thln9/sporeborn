Shader "Sporeborn/Workshop Sign"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            struct VertexInput
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct PixelInput
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;

            PixelInput Vertex(VertexInput input)
            {
                PixelInput output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 Fragment(PixelInput input) : SV_Target
            {
                fixed4 color = input.color;
                color.a *= tex2D(_MainTex, input.uv).a;
                return color;
            }
            ENDCG
        }
    }
}
