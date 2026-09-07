// TmpReimportUi.cs —— 临时工具：强制 reimport 7 个 UI PNG，让打包纹理变 Readable
// 背景：复刻任务 S5 块A 用 python 直接改 .png.meta（isReadable:1），但 AssetDatabase 已导入进 Library 的
//       纹理不会因外部改 .meta 自动 reimport；BuildWin 打包读的是 Library 旧的非 readable 纹理 → Sprite.Create 失败 → 白块。
//       本脚本显式设 ti.isReadable=true + SaveAndReimport，把新设置落到打包纹理，含读回验证（规则14 先检测再改）。
using UnityEditor;
using UnityEngine;

namespace Liuhuo.EditorTools
{
    public static class TmpReimportUi
    {
        static readonly string[] UiPaths = {
            "Assets/Resources/ui/btn_shengcai_ult.png",
            "Assets/Resources/ui/btn_tianting_summon.png",
            "Assets/Resources/ui/btn_tongsi_ult.png",
            "Assets/Resources/ui/btn_xuanchao_summon.png",
            "Assets/Resources/ui/panel_center_info.png",
            "Assets/Resources/ui/panel_tianting_hp.png",
            "Assets/Resources/ui/panel_xuanchao_hp.png",
        };

        public static void ReimportUi()
        {
            var sb = new System.Text.StringBuilder();
            int ok = 0, fail = 0;
            foreach (var path in UiPaths)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { sb.AppendLine($"[ReimportUi] 跳过(非TextureImporter): {path}"); fail++; continue; }
                bool before = ti.isReadable;
                ti.isReadable = true;
                if (ti.textureType != TextureImporterType.Sprite) ti.textureType = TextureImporterType.Sprite;
                if (ti.spriteImportMode != SpriteImportMode.Single) ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                // 读回验证（reimport 后重新 GetAtPath）
                var ti2 = AssetImporter.GetAtPath(path) as TextureImporter;
                bool after = ti2 != null && ti2.isReadable;
                sb.AppendLine($"[ReimportUi] {path} before={before} after={after} type={ti2?.textureType} mode={ti2?.spriteImportMode}");
                if (after) ok++; else fail++;
            }
            sb.AppendLine($"[ReimportUi] 完成 成功={ok} 失败={fail}");
            Debug.Log(sb.ToString());
        }

        // ★ImportBake：烘焙中文文字 Sprite（S5 复刻战，debugger 实证 UGUI 动态字体在打包运行时对 CJK 栅格化失败）。
        //   6 张 PIL 预栅格化的中文 PNG 设 textureType=Sprite + isReadable:1，使 UILoader.LoadSprite(Resources.Load<Texture2D>+Sprite.Create) 可用。
        //   新文件首次导入需先 Refresh 生成 meta，再 GetAtPath 才能拿到 TextureImporter。
        public static void ImportBake()
        {
            AssetDatabase.Refresh();
            string[] tracks = {
                "Assets/Resources/ui_text/text_tianting.png",
                "Assets/Resources/ui_text/text_xuanchao.png",
                "Assets/Resources/ui_text/text_btn_summon.png",
                "Assets/Resources/ui_text/text_btn_ult.png",
                "Assets/Resources/ui_text/text_btn_ultenemy.png",
                "Assets/Resources/ui_text/text_btn_summonred.png",
            };
            var sb = new System.Text.StringBuilder();
            int ok = 0, fail = 0;
            foreach (var path in tracks)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { sb.AppendLine($"[ImportBake] 跳过(非TextureImporter): {path}"); fail++; continue; }
                bool before = ti.isReadable;
                ti.isReadable = true;
                if (ti.textureType != TextureImporterType.Sprite) ti.textureType = TextureImporterType.Sprite;
                if (ti.spriteImportMode != SpriteImportMode.Single) ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                var ti2 = AssetImporter.GetAtPath(path) as TextureImporter;
                bool after = ti2 != null && ti2.isReadable;
                sb.AppendLine($"[ImportBake] {path} before={before} after={after} type={ti2?.textureType}");
                if (after) ok++; else fail++;
            }
            sb.AppendLine($"[ImportBake] 完成 成功={ok} 失败={fail}");
            Debug.Log(sb.ToString());
        }
    }
}
