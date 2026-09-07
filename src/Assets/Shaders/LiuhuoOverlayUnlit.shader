// LiuhuoOverlayUnlit.shader —— ZTest Always 置顶无光照叠底（对位 Web gBase）
// 用途：横向 gBase 叠底必须"深度穿透、恒置顶层"。URP 标准 Lit/Unlit 不暴露材质级 ZTest 开关，
//       因此自定义一个近乎 Unlit 的 shader，Pass 内显式 ZTest Always + ZWrite Off（栈顶绘制）。
// 由 MapLoader 通过 Shader.Find("Liuhuo/OverlayUnlit") 加载：
//   - Editor / 场景验证：本 shader 已导入即可命中。
//   - Player 打包：若因"无场景资产引用"而被 Unity strips，Shader.Find 会返回 null →
//     MapLoader 已兜底降级为 URP/Unlit + 置顶 renderQueue，并打 Warning 如实说明（详见 MapLoader.BuildGBase）。
// 兼容：同时暴露 _MainTex / _BaseMap 与 _Color / _BaseColor，避免贴图/颜色名不一致取不到。
Shader "Liuhuo/OverlayUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _BaseMap ("BaseMap", 2D) = "white" {}
        [HideInInspector] _BaseColor ("BaseColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "OverlayPass"
            ZTest Always        // 关键：无视深度恒通过 → 叠底恒置顶
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex); float4 _MainTex_ST;
            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            half4 _Color; half4 _BaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // _MainTex 为空(white)时回退 _BaseMap，保证 MapLoader 设到哪个键都出图
                half4 c = tex * _Color * _BaseColor;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
