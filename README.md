# Liuhuo — Tianming Battle Arena (流火 · 天命战场)

A real-time **3D battle arena** (Heavenly Court `TianTing` vs `XuanChao`) rebuilt from a
Three.js web original into a **fully-runable Unity URP project** — procedural VFX,
headless build pipelines, and runtime probe-based self-verification. Open `src/` and
run, or just unzip the release and play.

---

## ✨ Highlights

| Capability | Why it matters |
|---|---|
| **Fully runable Unity project** | `src/Assets + ProjectSettings + Packages` opens directly in Unity 2022.3; no external build keys needed |
| **Procedural VFX** | Holy-Judgment dome & Same-Death laser rain built entirely from `ParticleSystem` + a custom URP Unlit shader — no baked textures, no external model assets for the effects |
| **Custom URP shader (`HolyDome`)** | Replicates a three.js `ShaderMaterial` live-time formula (dual-frequency sinusoidal flow + glowing golden rim) that a static 1D texture could never reproduce |
| **Headless batch build chain** | `BatchPhase3.BuildWin` runs via `Unity.exe -batchmode -quit` — a one-command reproducible build (`error CS=0`, `BuildWin result=Succeeded`) |
| **Runtime probe-based self-verification** | In-game probes (`UltProbe`, `RangeProbe`, `SameProbe`, `SparkProbe`, `BF-T1`) print `Debug.Log` evidence to prove damage, AOE coverage, particle density, and soldier facing — replacing "I think it works" |
| **Geometric-consistency validation** | e.g. radius ×2 → area ×4 → emission rate ×4 keeps per-area density exact (`576 / 100800 == 144 / 25200 == 0.00571`) |
| **GPU instancing + URP quality toggles** | instanced buildings/particles; anti-alias & bloom are independent runtime switches (not a locked pipeline) |

---

## 🧱 Project Structure

```
liuhuo-tianming-game/
├─ README.md           ← you are here (English + 中文速览)
├─ LICENSE             ← MIT (code)
├─ src/                ← ★ the Unity engine project
│  ├─ Assets/
│  │  ├─ Scripts/
│  │  │  ├─ Core/      ← combat, soldier, base, camera, bootstrap
│  │  │  ├─ FX/        ← procedural VFX, ults, particle pool
│  │  │  ├─ UI/        ← HUD, settlement, front-end, settings
│  │  │  ├─ PoseAnim/  ← pose-animation baking
│  │  │  ├─ Model/     ← base & zone-ring factory
│  │  │  └─ Audio/     ← audio controller
│  │  ├─ Editor/       ← 10 build/packaging/optimization tools
│  │  ├─ Scenes/       ← gameplay & UI scenes
│  │  └─ Models/Fx/    ← art & VFX assets
│  ├─ ProjectSettings/ ← 21 Unity configs (URP, tags, quality…)
│  └─ Packages/        ← manifest (URP, Burst, …)
└─ release/
   └─ liuhuo_tianming_game.zip  ← play in one double-click (no Unity/.NET needed)
```

---

## 🛠 Tech Stack

- **Unity 2022.3.62f3c1** + **URP (Universal Render Pipeline)**
- C# gameplay code, Editor extension scripts, batch-mode build tooling
- Custom shaders (URP Unlit) for the Holy-Dome replica
- `ParticleSystem` procedural VFX + a shared material pool (`ParticleFxPool`) to avoid per-frame allocs

---

## 🧩 Module Map

**Core** — `CombatSystem`, `Soldier`, `SoldierAI`, `BaseSystem`, `BattleCamera`,
`GameBootstrap`, `BattleStage`, `WeaponConfig`, `Bullet`, `BuildingManager`,
`FloatingText`, `HitFXManager`, `DeathFXManager`, `StageQuotes`, `MapLoader`,
`SoldierFactory`, `SoldierHalo`, `SoldierTag`

**FX** — `UltController`, `HolyJudgment`, `TongsiUltimate`, `HolyJudgmentFx`,
`SameDeathFx`, `AmbientFx`, `HitFx`, `ParticleFxPool`, `VFXConfig`

**UI** — `UILoader`, `HUDController`, `SettlementManager`, `FrontendUI`,
`SettingsPanel`, `EntryFlow`

**PoseAnim** — `PoseBaker`, `PoseAnimSystem`
**Model** — `BaseModelFactory`, `ZoneRing`
**Audio** — `AudioController`

**Editor** — `BatchPhase3`, `S5_Builder`, `ImportSettingsOptimizer`, `URPSetup`,
`GpuInstancingEnabler`, `SceneAudit`, `VerifyHitFX`, `VerifyP6`, `HitFXBuilder`,
`TmpReimportUi`

---

## ▶️ Build & Run

**Play instantly (bundled)**
```
unzip release/liuhuo_tianming_game.zip
double-click Liuhuo.exe          # Mono runtime is embedded — no Unity/.NET needed
```

**From source**
```
# In Unity Hub, add src/ and open with Unity 2022.3.62f3c1
# Build via batchmode:
Unity.exe -batchmode -quit -projectPath src \
  -executeMethod Liuhuo.EditorTools.BatchPhase3.BuildWin
```

---

## 🧪 Verification Evidence

Everything below is machine-checked, not asserted:

- **Build**: `error CS=0`, `BuildWin result=Succeeded`, `Assembly-CSharp.dll` mtime bumped
  (the exe is a launcher container, so the authoritative signal is the `.dll`).
- **AOE center / radius** — `[UltProbe] AOE中心(4.70, 0.00, 71.40) sameRadius=100 holyRadius=60`
- **Range coverage** — `[RangeProbe] 圣裁 新半径(60)内活敌=20 旧半径(30)内=7 额外覆盖=13`
  (proves the expansion reaches far-side soldiers — the exact complaint it fixed)
- **Density preserved** — `[SameProbe] 发射率rate=576.00 发射面scale=(360.00, 1.00, 280.00) maxParticles=4000`
- **Soldier facing** — `[BF-T1]` probes the visual **front axis** (`+X`, `aimOffset=-90`)
  rather than `transform.forward`, which would be a circular-logic false positive.

---

## 中文速览 (Quick Chinese Overview)

**这是什么**：一个天庭 vs 玄朝的 3D 实时战场（流火·天命战场），把原本的 Three.js Web 版完整复刻成可运行的 Unity URP 工程。

**技术亮点**：
- **纯程序化特效**——圣裁光幕、同死激光雨全部用 `ParticleSystem` + 自定义 URP Unlit shader 构建，无烘焙贴图、无外置特效模型。
- **自定义 URP shader 复刻 three.js ShaderMaterial**——圣裁穹顶的双频正弦流动 + 金色亮环公式，静态纹理做不到的实时感。
- **无头批量构建链**——`BatchPhase3.BuildWin` 一键 `-batchmode` 出包（`error CS=0`、`Succeeded`）。
- **运行时探针自证**——`UltProbe / RangeProbe / SameProbe / SparkProbe / BF-T1` 用 `Debug.Log` 打印铁证日志，实证伤害、AOE 覆盖、粒子密度、士兵朝向，替代"我觉得没问题"。
- **几何一致性校验**——半径×2 → 面积×4 → 发射率×4，单位面积粒子密度精确不变（`0.00571`）。
- **GPU instancing + URP 质量开关**——建筑与粒子实例化；抗锯齿 / Bloom 独立运行时控制。

**怎么跑**：解压 `release/liuhuo_tianming_game.zip` 双击 `Liuhuo.exe` 即玩（内嵌 Mono 运行时，免装 Unity/.NET）；或用 Unity 2022.3 打开 `src/` 构建。

---

## 📄 License & Asset-Source Disclaimer

- **Code** is released under the **MIT License** (see `LICENSE`).
- **Art / models / VFX assets** in this repo are **replicated from an original Web (Three.js)
  version** for **technical learning & capability showcase only**. All such assets and their
  visual design remain the **copyright of the original rights holder**.
  If you are the owner and wish this material removed, open an issue and it will be taken down.
