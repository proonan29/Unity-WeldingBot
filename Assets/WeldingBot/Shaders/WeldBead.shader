// Weld bead: grows along the seam and cools down, driven by the global simulation time.
// Per vertex: TEXCOORD1 = (depositTime, previousRingDepositTime, typeIndex, 0)
//             TEXCOORD2 = (offset to the previous ring centre, 0)   COLOR = welding-position colour
Shader "WeldingBot/WeldBead"
{
    Properties
    {
        _DoneColor ("Welded colour (heat mode)", Color) = (0.10, 0.90, 0.75, 1)
        _FilletColor ("Fillet colour", Color) = (0.20, 0.80, 1.00, 1)
        _ButtColor ("Butt colour", Color) = (1.00, 0.80, 0.15, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _WB_SimTime;
            float _WB_ColorMode;   // 0 heat, 1 position, 2 joint type

            CBUFFER_START(UnityPerMaterial)
                float4 _DoneColor;
                float4 _FilletColor;
                float4 _ButtColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 info : TEXCOORD1;
                float4 offs : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR;
                float3 data : TEXCOORD1;   // age, visible, type
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float now = _WB_SimTime;
                float dep = v.info.x, prev = v.info.y;
                float3 p = v.positionOS.xyz;
                float vis = 1;
                if (now < prev) { vis = 0; p += v.offs.xyz; }
                else if (now < dep)
                {
                    float f = saturate((now - prev) / max(dep - prev, 1e-4));
                    p += v.offs.xyz * (1 - f);
                }
                o.positionCS = TransformObjectToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.data = float3(max(0, now - dep), vis, v.info.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (i.data.y < 0.999) discard;
                float age = i.data.x;
                float3 baseCol = _DoneColor.rgb;
                if (_WB_ColorMode > 0.5 && _WB_ColorMode < 1.5) baseCol = i.color.rgb;
                else if (_WB_ColorMode >= 1.5) baseCol = i.data.z > 0.5 ? _ButtColor.rgb : _FilletColor.rgb;

                // fresh metal: dark oxide that fades into the marking colour
                float3 steel = float3(0.25, 0.22, 0.20);
                baseCol = lerp(steel, baseCol, saturate((age - 6.0) / 30.0));

                Light l = GetMainLight();
                float3 n = normalize(i.normalWS);
                float ndl = abs(dot(n, l.direction));
                float3 lit = baseCol * (0.40 + 0.75 * ndl * l.color);

                // incandescence: white-yellow -> orange -> dull red
                float3 hot = lerp(float3(1.0, 0.95, 0.75), float3(1.0, 0.45, 0.05), saturate(age / 1.5));
                hot = lerp(hot, float3(0.6, 0.06, 0.02), saturate((age - 1.5) / 10.0));
                float glow = 5.0 * exp(-age / 6.0);
                return half4(lit + hot * glow, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
