using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using Liuhuo.Core;
using Liuhuo.UI;

namespace Liuhuo.EditorTools
{
    public static class S5_Builder
    {
        const string SoldierDir = "Assets/Art/Models/Soldiers";
        const string MatDir = "Assets/Art/Models/Materials";
        const string PrefabDir = "Assets/Art/Prefabs";
        const string CtrlDir = "Assets/Art/Animators";

        static string ModelFbx(int t) => t == 0 ? "tianting_soldier_q.fbx" : "xuanchao_soldier_q.fbx";
        static string WalkFbx(int t) => t == 0 ? "tianting_walk.fbx" : "xuanchao_walk.fbx";
        static string MatPath(int t) => $"{MatDir}/Mat_{(t==0?"TianTing":"XuanChao")}_Soldier.mat";
        static string CtrlPath(int t) => $"{CtrlDir}/Soldier_{(t==0?"TianTing":"XuanChao")}.controller";
        static string PrefabPath(int t) => $"{PrefabDir}/Soldier_{(t==0?"TianTing":"XuanChao")}.prefab";

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Animators");
            EnsureFolder("Assets/Art/Prefabs");
        }
        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // 诊断：打印 4 个 fbX 的全部 clip（判断 idle/walk 是否缺失）
        public static void DumpClips()
        {
            string[] fbx = new string[] {
                $"{SoldierDir}/tianting_soldier_q.fbx",
                $"{SoldierDir}/tianting_walk.fbx",
                $"{SoldierDir}/xuanchao_soldier_q.fbx",
                $"{SoldierDir}/xuanchao_walk.fbx",
            };
            foreach (var p in fbx)
            {
                var all = AssetDatabase.LoadAllAssetsAtPath(p);
                int clipCount = 0;
                foreach (var o in all)
                    if (o is AnimationClip c) { clipCount++; Debug.Log($"[Diag]{System.IO.Path.GetFileName(p)} clip='{c.name}'"); }
                Debug.Log($"[Diag]{System.IO.Path.GetFileName(p)} totalAssets={all.Length} clips={clipCount}");
            }
        }

        public static void BuildAll()
        {
            EnsureFolders();
            Run("BuildSoldiers", BuildSoldiers);
            Run("BuildBullet", BuildBullet);
            Run("BuildBases", BuildBases);
            Run("BuildScene", BuildScene);
        }

        static void Run(string name, System.Action a)
        {
            try { Debug.Log($"[S5_Builder] -- {name} BEGIN"); a(); Debug.Log($"[S5_Builder] -- {name} DONE"); }
            catch (System.Exception e) { Debug.LogError($"[S5_Builder] -- {name} FAILED: {e.Message}\n{e.StackTrace}"); }
        }

        // ============ G4: 两态 AnimatorController + 士兵预制体 ============
        static void BuildSoldiers() { BuildSoldier(0); BuildSoldier(1); }

        static void BuildSoldier(int team)
        {
            AnimationClip idle = FindClip($"{SoldierDir}/{ModelFbx(team)}", wantWalk: false);
            AnimationClip walk = FindClip($"{SoldierDir}/{WalkFbx(team)}", wantWalk: true);

            string ctrlPath = CtrlPath(team);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null)
            {
                AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            }
            ConfigureAnimator(ctrl, idle, walk);

            string modelPath = $"{SoldierDir}/{ModelFbx(team)}";
            var modelGo = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            // ★ 用 Object.Instantiate 独立克隆而非 InstantiatePrefab(嵌套 prefab 实例)：
            //   FBX 士兵为 3 子网格(3 材质槽)，InstantiatePrefab 后在 SaveAsPrefabAsset 只写 slot0 override(Material.Array.data[0])，
            //   slot1/2 保持 FBX 内嵌素色 → 士兵白/灰(debugger 确诊)。Object.Instantiate 产无 prefab 关联的独立克隆，
            //   SaveAsPrefabAsset 存全新 prefab，m_Materials.Array 全 3 槽完整写入 = Mat_XX(金/红)。
            var root = (GameObject)Object.Instantiate(modelGo);
            root.name = team == 0 ? "Soldier_TianTing" : "Soldier_XuanChao";
            // 对位 dist10 士兵视觉 scale(0.5*SOLDIER_SCALE_FACTOR=0.5*7.0=3.5)：u3d 相机(0,150,220)FOV45+地图150x200 与 dist10 一致，
            // 士兵须同 scale 3.5 才达同视觉占比（否则 PopOrInstantiate 锁 scale=1 → 士兵成视觉小黑点）。
            root.transform.localScale = new Vector3(3.5f, 3.5f, 3.5f);

            // ★士兵用 Mat_XX(现 _BaseColor=白) 全 slot 覆盖：FBX 内嵌材质(Import via MaterialDescription)绑不上同目录散放 *_basecolor.jpg
            //   (Unity FBX 导入找贴图只认 Textures 子文件夹/.fbm，同目录散放 jpg 不被材质引用 → _BaseMap=null → 素模白/红，用户截图实证)。
            //   Mat_XX 手工 _BaseMap 绑了正确贴图 + tint 改白(1,1,1) → 显示贴图本色：既不出素模，也不额外金红。恢复覆盖以抹掉 FBX 素模槽。
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath(team));
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                {
                    int n = r.sharedMaterials.Length;
                    if (n <= 1) r.sharedMaterial = mat;
                    else
                    {
                        var arr = new Material[n];
                        for (int i = 0; i < n; i++) arr[i] = mat;
                        r.sharedMaterials = arr;
                    }
                }
            }

            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;

            if (root.GetComponent<SoldierTag>() == null) root.AddComponent<SoldierTag>();
            if (root.GetComponent<Soldier>() == null) root.AddComponent<Soldier>();
            if (root.GetComponent<SoldierAI>() == null) root.AddComponent<SoldierAI>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(team));
            Object.DestroyImmediate(root);
            Debug.Log($"[S5_Builder] 士兵预制体已建 team={team} {PrefabPath(team)} idle={(idle!=null?idle.name:"null")} walk={(walk!=null?walk.name:"null")}");
        }

        static AnimationClip FindClip(string fbxPath, bool wantWalk)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var clips = all.OfType<AnimationClip>().ToList();
            foreach (var c in clips) Debug.Log($"[S5_Builder][{System.IO.Path.GetFileName(fbxPath)}] clip='{c.name}'");
            if (clips.Count == 0) return null;
            if (wantWalk)
            {
                var w = clips.FirstOrDefault(c => c.name.ToLower().Contains("walk") || c.name.ToLower().Contains("take") || c.name.ToLower().Contains("anim"));
                if (w != null) return w;
            }
            else
            {
                var i = clips.FirstOrDefault(c => !c.name.ToLower().Contains("walk"));
                if (i != null) return i;
            }
            return clips[0];
        }

        static void ConfigureAnimator(AnimatorController ctrl, AnimationClip idle, AnimationClip walk)
        {
            if (ctrl == null) return;
            int pidx = -1;
            for (int i = 0; i < ctrl.parameters.Length; i++)
                if (ctrl.parameters[i].name == "Moving") { pidx = i; break; }
            if (pidx < 0) ctrl.AddParameter("Moving", AnimatorControllerParameterType.Bool);

            var sm = ctrl.layers[0].stateMachine;
            if (sm.defaultState != null) sm.RemoveState(sm.defaultState);
            var idleState = sm.AddState("Idle");
            if (idle != null) idleState.motion = idle;
            var walkState = sm.AddState("Walk");
            if (walk != null) walkState.motion = walk;
            sm.defaultState = idleState;

            var t1 = idleState.AddTransition(walkState);
            t1.hasExitTime = false; t1.hasFixedDuration = false; t1.duration = 0.15f;
            t1.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            var t2 = walkState.AddTransition(idleState);
            t2.hasExitTime = false; t2.hasFixedDuration = false; t2.duration = 0.15f;
            t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
        }

        // ============ G8: 子弹预制体 ============
        static void BuildBullet()
        {
            string path = $"{PrefabDir}/Bullet.prefab";
            var go = new GameObject("Bullet");
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.12f;
            go.AddComponent<Bullet>();
            // R3-E3 子弹拖尾（文部 TC-E3：Bullet 预制体加 TrailRenderer，additive）：
            // 对象池复用（静态挂组件非运行时 Add）；阵营色/渐变由 Bullet.Launch 运行时设 startColor/endColor。
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.2f;
            trail.minVertexDistance = 0.05f;
            trail.startWidth = 0.1f;
            trail.endWidth = 0f;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // additive 材质：优先 Particles/Additive，Shader.Find 不可用则回退 Sprites/Default（运行时 Bullet 补色）
            var trailMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
            if (trailMat != null && trailMat.shader != null) trail.material = trailMat;
            else { var smat = new Material(Shader.Find("Sprites/Default")); if (smat != null && smat.shader != null) trail.material = smat; }
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[S5_Builder] 子弹预制体已建 {path} (含TrailRenderer)");
        }

        // ============ G5: 基地占位 Cube 阵营色 ============
        static void BuildBases()
        {
            BuildBase(0, "Base_TianTing", new Color(1f, 0.85f, 0.42f));
            BuildBase(1, "Base_XuanChao", new Color(0.52f, 0.05f, 0.05f));
        }

        static void BuildBase(int team, string baseName, Color color)
        {
            string matPath = $"{MatDir}/{baseName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", color);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = baseName;
            go.transform.localScale = new Vector3(22f, 6f, 14f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            go.AddComponent<BaseSystem>().SetTeam(team);
            string prefabPath = $"{PrefabDir}/{baseName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"[S5_Builder] 基地占位已建 team={team} {prefabPath}");
        }

        // ============ G9: Phase1 场景 ============
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            var light = new GameObject("Directional Light");
            var ld = light.AddComponent<Light>();
            ld.type = LightType.Directional; ld.intensity = 1.05f; ld.color = Color.white;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var cam = new GameObject("Main Camera");
            var cc = cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cc.orthographic = false; cc.fieldOfView = 45f; cc.nearClipPlane = 1f; cc.farClipPlane = 600f; cc.clearFlags = CameraClearFlags.SolidColor;
            cc.backgroundColor = new Color(0.35f, 0.42f, 0.5f);
            // ★T8（用户十项任务 第8项"相机位置被修改了，角度不对，还太贴地了，需要调回"）：对位原版 dist10 为 Perspective，取中远景全景。
            //   教训复盘两次误判：initial (0,150,220) 用户判"太贴地/角度不对"(远景斜俯视致战场贴地平)；(0,8,20) 用户判"太近看不清"(近景漏全局，
            //   且近景被圣裁500m/同死激光雨淹没→大招糊屏副作用)。取折中 (0,75,115) lookAt 战场中央：纵览双方基地±40+中间士兵，俯视角≈33° 有立体感不贴地。
            //   同时呼应 T1 士兵贴图(中景下士兵 scale3.5 清晰可辨阵营色)。
            // ★用户手动改相机参数（图四截图 Input：Position (0,143.8,220.4) / Rotation Euler (33.111,180,0)）：
            //   按用户给的精确数值直接设 position + eulerAngles（不再 LookAt 原点），FOV45 透视已在上方 cc.fieldOfView=45f。
            cam.transform.position = new Vector3(0f, 143.8f, 220.4f);
            cam.transform.rotation = Quaternion.Euler(33.111f, 180f, 0f);

            var ttBasePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Base_TianTing.prefab");
            var xcBasePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Base_XuanChao.prefab");
            var ttBase = (GameObject)PrefabUtility.InstantiatePrefab(ttBasePrefab);
            ttBase.transform.SetPositionAndRotation(new Vector3(40f, 0f, 0f), Quaternion.identity);
            var xcBase = (GameObject)PrefabUtility.InstantiatePrefab(xcBasePrefab);
            xcBase.transform.SetPositionAndRotation(new Vector3(-40f, 0f, 0f), Quaternion.identity);
            var ttSys = ttBase.GetComponent<BaseSystem>(); if (ttSys != null) ttSys.SetTeam(0);
            var xcSys = xcBase.GetComponent<BaseSystem>(); if (xcSys != null) xcSys.SetTeam(1);

            var facGo = new GameObject("SoldierFactory");
            var fac = facGo.AddComponent<SoldierFactory>();
            fac.bluePrefab = AssetDatabase.LoadAssetAtPath<Soldier>($"{PrefabDir}/Soldier_TianTing.prefab");
            fac.redPrefab = AssetDatabase.LoadAssetAtPath<Soldier>($"{PrefabDir}/Soldier_XuanChao.prefab");
            fac.bulletPrefab = AssetDatabase.LoadAssetAtPath<Bullet>($"{PrefabDir}/Bullet.prefab");
            fac.blueBase = ttSys; fac.redBase = xcSys;

            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.factory = fac; gm.blueBase = ttSys; gm.redBase = xcSys;
            gm.autoTest = false; gm.autoSummon = 50;   // ★T10 生产先出开始页(FrontendUI)；验证走命令行 -autoTest 直入战斗（原 R4 自走改由 -autoTest 驱动）

            // Phase-B P0-4：场景不再建 HUD/UI（避免与 UILoader 运行时新建的 Canvas 双叠加），
            // 对位 Web 四按钮三面板由 UILoader.Create() 在 GameBootstrap.Start 全权实现。
            // 注：BuildHud() 方法保留定义不调用（防其它引用；若未来要回退可改回 BuildHud();）

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Phase1.unity");
            Debug.Log("[S5_Builder] Phase1 场景已保存 Assets/Scenes/Phase1.unity（含HUD UI）");
        }

        // ============ R2: HUD 场景 UI 构建（对位 Web UIManager，绑 HUDController 公开字段） ============
        static void BuildHud()
        {
            // 字体（2022.3 内置 LegacyRuntime，旧版回退 Arial）
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }

            // 静态 Canvas（背景/标签，TC-C5 分 Canvas）
            var sCanvas = new GameObject("HUD_Static").AddComponent<Canvas>();
            sCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sCanvas.gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            MakeText(sCanvas.transform, "HUDTitle", "流火 · 天庭 vs 玄朝", font, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), Color.white, 30);

            // 动态 Canvas（血条/数量/冷却）
            var dCanvas = new GameObject("HUD_Dynamic").AddComponent<Canvas>();
            dCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dCanvas.gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dCanvas.gameObject.AddComponent<GraphicRaycaster>();
            var hud = dCanvas.gameObject.AddComponent<HUDController>();

            // 血条×2（天庭左/玄朝右）
            hud.baseHpTianting = MakeSlider(dCanvas.transform, "BaseHP_Tianting", font, new Vector2(0.5f, 1f), new Vector2(-430f, -80f), new Color(1f, 0.84f, 0f), new Color(0.1f, 0.1f, 0.1f, 0.7f));
            hud.baseHpXuanchao = MakeSlider(dCanvas.transform, "BaseHP_Xuanchao", font, new Vector2(0.5f, 1f), new Vector2(430f, -80f), new Color(0.86f, 0.08f, 0.23f), new Color(0.1f, 0.1f, 0.1f, 0.7f));

            // 数量+击杀 Text×4（天庭左/玄朝右）
            hud.soldierCountTianting = MakeText(dCanvas.transform, "SoldierCount_T", "0", font, new Vector2(0f, 1f), new Vector2(0.2f, 0.88f), new Vector2(20f, -64f), new Color(1f, 0.84f, 0f), 26);
            hud.killCountTianting = MakeText(dCanvas.transform, "KillCount_T", "击杀 0", font, new Vector2(0f, 1f), new Vector2(0.2f, 0.82f), new Vector2(20f, -44f), new Color(1f, 0.84f, 0f), 22);
            hud.soldierCountXuanchao = MakeText(dCanvas.transform, "SoldierCount_X", "0", font, new Vector2(1f, 1f), new Vector2(1f, 0.88f), new Vector2(-20f, -64f), new Color(0.86f, 0.08f, 0.23f), 26);
            hud.killCountXuanchao = MakeText(dCanvas.transform, "KillCount_X", "击杀 0", font, new Vector2(1f, 1f), new Vector2(1f, 0.82f), new Vector2(-20f, -44f), new Color(0.86f, 0.08f, 0.23f), 22);

            // 大招 Button×2（btnUlt 圣裁/btnUltEnemy 同死，HUDController.Start 自动接 UltController）
            hud.btnUlt = MakeButton(dCanvas.transform, "BtnUlt", "圣裁", font, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-90f, 70f), new Color(1f, 0.84f, 0f));
            hud.btnUltEnemy = MakeButton(dCanvas.transform, "BtnUltEnemy", "同死", font, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(90f, 70f), new Color(0.86f, 0.08f, 0.23f));

            Debug.Log("[S5_Builder] HUD 场景 UI 已建（Canvas×2/Slider×2/Text×4/Button×2/HUDController 绑定非null）");
        }

        static Text MakeText(Transform parent, string name, string content, Font font, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Color color, int size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = anchorMin;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(220f, 40f);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = content; t.color = color; t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        static Slider MakeSlider(Transform parent, string name, Font font, Vector2 anchor, Vector2 anchoredPos, Color fillColor, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(400f, 22f);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgImg = bgGo.AddComponent<Image>(); bgImg.color = bgColor;
            var bgRt = bgImg.rectTransform; bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var faGo = new GameObject("FillArea");
            faGo.transform.SetParent(go.transform, false);
            var faRt = faGo.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one; faRt.offsetMin = new Vector2(2f, 2f); faRt.offsetMax = new Vector2(-2f, -2f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(faGo.transform, false);
            var fill = fillGo.AddComponent<Image>(); fill.color = fillColor;
            var fRt = fill.rectTransform; fRt.anchorMin = Vector2.zero; fRt.anchorMax = new Vector2(0.0001f, 1f); fRt.pivot = new Vector2(0f, 0.5f); fRt.sizeDelta = Vector2.zero;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.targetGraphic = fill;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;
            return slider;
        }

        static Button MakeButton(Transform parent, string name, string label, Font font, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = anchorMin;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(140f, 56f);
            var img = go.AddComponent<Image>(); img.color = color;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var tRt = txtGo.AddComponent<RectTransform>(); tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one; tRt.offsetMin = Vector2.zero; tRt.offsetMax = Vector2.zero;
            var t = txtGo.AddComponent<Text>(); t.font = font; t.text = label; t.color = Color.black; t.fontSize = 22; t.alignment = TextAnchor.MiddleCenter;
            return btn;
        }
    }
}
