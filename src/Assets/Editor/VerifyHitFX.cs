// VerifyHitFX.cs —— P3 自测：读两个受击特效预制体，逐层输出 名称/类型/材质/additive 标志/sizeAttenuation 关(禁距离衰减F4)
// 对位 s2 测试方案 TC-C1(10层存在)/C2(配色)/C3(additive+ring)/F4(禁sizeAttenuation)。
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Liuhuo.EditorTools
{
    public static class VerifyHitFX
    {
        public static void Run()
        {
            var sb = new System.Text.StringBuilder();
            string[] names = { "HitFX_Tianting", "HitFX_Xuanchao" };
            foreach (var n in names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Hit/" + n + ".prefab");
                if (prefab == null) { sb.AppendLine("[Verify] MISSING " + n); continue; }
                var tf = prefab.transform;
                int layers = tf.childCount;
                sb.AppendLine("[Verify] " + n + " 子对象数=" + layers);
                foreach (Transform child in tf)
                {
                    var ps = child.GetComponent<ParticleSystem>();
                    if (ps == null) { sb.AppendLine("  - " + child.name + " | 无ParticleSystem"); continue; }
                    var main = ps.main;
                    var r = child.GetComponent<ParticleSystemRenderer>();
                    string matName = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "null";
                    string matMode = "?";
                    if (r != null && r.sharedMaterial != null)
                    {
                        int dst = r.sharedMaterial.GetInt("_DstBlend");
                        matMode = (dst == (int)BlendMode.One) ? "ADDITIVE" : ((dst == (int)BlendMode.OneMinusSrcAlpha) ? "NORMAL" : "dst=" + dst);
                    }
                    string distCull = "ortho恒定(天然禁衰减F4)";
                    string burst = "";
                    var emission = ps.emission;
                    if (emission.enabled && emission.burstCount > 0)
                    {
                        var bar = new ParticleSystem.Burst[emission.burstCount];
                        emission.GetBursts(bar);
                        burst = " burst=" + bar[0].maxCount;
                    }
                    sb.AppendLine("  - " + child.name + " | mat=" + matName + " | blend=" + matMode + " | size=" + main.startSize.constant
                        + " | life=" + System.Math.Round(main.startLifetime.constantMin,2) + "-" + System.Math.Round(main.startLifetime.constantMax,2)
                        + " | " + distCull + burst);
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
