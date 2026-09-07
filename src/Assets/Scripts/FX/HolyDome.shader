// HolyDome.shader —— 圣裁半球冲击波（对位原版 Web HolyJudgment.js 的 ShaderMaterial _makeDome）
// ---------------------------------------------------------------------
// 原版是 three.js ShaderMaterial，片元用局部坐标实时计算：
//   d = length(vPos.xz)                     -- 半球局部水平半径（0=顶 .. 1=缘，随 localScale 放大到 250 → 直径 500m）
//   ring = 1 - smoothstep(0,0.03,abs(d-0.5))            -- d=0.5 暗亮环（随 scale 放大即波前边缘）
//   ripple = 0.5 + 0.5*sin(d*30 - t*10)*0.6 + 0.5*sin(d*55 - t*16)*0.4   -- 双频正弦，随时间流动
//   center = smoothstep(0.6,0,d)*0.35                     -- 中心光斑
//   edge = smoothstep(0,0.02,d)*(1-smoothstep(0.85,1,d)) -- 边缘遮罩
//   a = (ring*1.8 + ripple*0.8 + center)*edge*(1 - uFade*0.9)
//   gl_FragColor = vec4(uColor*(1+ring*1.6), a)          -- 亮环处金色增亮
// 本 shader 把该公式复刻为 URP Unlit。dome mesh 半径 1，positionOS.xz 即局部半径 d；
// 缩放由 transform.localScale(0.1→250) 控制，故这里直接用 object-space position，不随世界偏移。
// 属性 _Elapsed 由 HolyJudgmentFx.cs 每帧 SetFloat 驱动波纹流动；_Fade 做渐隐。
Shader "LiuhuoFX/HolyDome"
{
    Properties
    {
        _BaseColor("Color", Color) = (1, 0.84, 0, 1)
        _Elapsed("Elapsed (s)", Float) = 0.0
        _Fade("Fade 0..1", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }
        LOD 100

        // 对位原版 THREE.AdditiveBlending = blendFunc(SRC_ALPHA, ONE) = src.rgb*srcAlpha + dst.rgb
        //   ★关键：必须 SrcAlpha One（读 alpha），不能 One One——否则 alpha 里的波纹/光斑/边缘遮罩全被丢弃
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "HolyDome"

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;   // object-space 位置（局部半径 d=length(xz)）
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Elapsed;
                float  _Fade;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionOS = input.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float d = length(input.positionOS.xz);            // 0顶..1缘
                float ring   = 1.0 - smoothstep(0.0, 0.03, abs(d - 0.5)); // d=0.5 亮环
                float ripple = 0.5
                             + 0.5 * sin(d * 30.0 - _Elapsed * 10.0) * 0.6
                             + 0.5 * sin(d * 55.0 - _Elapsed * 16.0) * 0.4;   // 双频动态波纹
                float center = smoothstep(0.6, 0.0, d) * 0.35;          // 中心光斑
                float edge   = smoothstep(0.0, 0.02, d) * (1.0 - smoothstep(0.85, 1.0, d));  // 边缘遮罩
                float a = (ring * 1.8 + ripple * 0.8 + center) * edge * (1.0 - _Fade * 0.9);
                // 亮环处金色增亮：uColor*(1+ring*1.6)
                float3 col = _BaseColor.rgb * (1.0 + ring * 1.6);
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
