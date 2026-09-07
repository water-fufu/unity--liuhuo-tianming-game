// AudioController.cs —— 音频框架（对位 Web Howler，阶段3 交付物）
// 已知坑复刻（v105 治本，勿在 Unity 重犯）：
//   1. BGM 叠音 = stopBGM 立即 stop + playBGM 开头 stop（防 BGM 叠音）
//   2. 警报/音效 100ms 节流（同一音效防连发）
// P6(F5) 扩展：PlaySFX 单发音效（s3 契约签名）+ 4 接线点（战斗开始 BGM/士兵受击/基地摧毁/GameOver 停 BGM）+ 程序化占位音。
//   占位音无外部音频文件，AudioClip.Create 生成，仅验证接线链路，正式音频后续替换，不阻塞交付。
using UnityEngine;

namespace Liuhuo.Audio
{
    public class AudioController : MonoBehaviour
    {
        public static AudioController Instance;

        [Header("BGM 引用")] public AudioSource bgmSource;
        public AudioClip bgmMain;       // 对位 bgm_sinos_de_natal.mp3
        public AudioClip alarmSiren;    // 对位 alarm_siren.mp3

        [Header("SFX 占位音（P6 无外部文件，程序化生成）")]
        public AudioClip sfxHit;         // 士兵受击占位
        public AudioClip sfxExplosion;   // 基地摧毁占位
        public AudioClip sfxDeath;       // 士兵死亡占位（对位 Web death.wav）

        private AudioSource _sfxSource;  // 惰性建，供无显式 src 的接线点（士兵/基地）用
        private AudioClip _battleBgm;    // 战斗开始 BGM 占位（懒生成缓存）
        private float _lastAlarm = -1f;
        private bool _generated;

        void Awake() { Instance = this; DontDestroyOnLoad(gameObject); }

        void Start()
        {
            // B3-T7：接入真实 Web 音频（已复制进 Assets/Resources/Audio/），Resources.Load 优先；缺失时回退合成占位（防 null 破坏接线链路，不崩溃）
            if (!_generated)
            {
                _generated = true;
                if (bgmMain == null) bgmMain = LoadReal("bgm_sinos_de_natal");   // 对位 Web bgm_sinos_de_natal.mp3
                if (alarmSiren == null) alarmSiren = LoadReal("alarm_siren");      // 对位 Web alarm_siren.mp3
                if (sfxHit == null) sfxHit = LoadReal("bullet_hit");               // 对位 Web bullet_hit.wav（士兵受击）
                if (sfxExplosion == null) sfxExplosion = LoadReal("explosion");    // 对位 Web explosion.wav（基地摧毁）
                if (sfxDeath == null) sfxDeath = LoadReal("death");                // 对位 Web death.wav（士兵死亡）
                // 兜底：Resources 未导入/无真实音频时，回退合成占位，仅验证接线链路（正式音频经导入后 autoLoad 覆盖）
                if (sfxHit == null) sfxHit = GenerateTone(880f, 0.10f, 0.4f);
                if (sfxExplosion == null) sfxExplosion = GenerateTone(120f, 0.50f, 0.6f);
                if (sfxDeath == null) sfxDeath = GenerateTone(180f, 0.40f, 0.5f);
                if (alarmSiren == null) alarmSiren = GenerateTone(600f, 0.30f, 0.5f);
            }
        }

        // B3-T7：从 Resources/Audio 懒加载真实音频，缺则返回 null 走合成兜底
        static AudioClip LoadReal(string key)
        {
            try { return Resources.Load<AudioClip>("Audio/" + key); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AudioController] 加载真实音频 " + key + " 失败，回退占位: " + e.Message);
                return null;
            }
        }

        // 对位 Web Howler playBGM —— 开头先 stop 再 play（治本叠音）
        public void PlayBGM(AudioClip clip)
        {
            if (bgmSource == null || clip == null) return;
            bgmSource.Stop();          // 治本叠音：先强停
            bgmSource.clip = clip;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        // 对位 Web stopBGM —— 立即 stop（非 fade，防叠音）
        public void StopBGM()
        {
            if (bgmSource == null) return;
            bgmSource.Stop();
        }

        // 警报/音效 100ms 节流（防重复触发爆炸声）
        public void PlayAlarm(AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            if (Time.time - _lastAlarm < 0.1f) return;  // 100ms 节流
            _lastAlarm = Time.time;
            src.PlayOneShot(clip);
        }

        // P1-4 便捷重载：无参调（基地受击/警报状态用），自解析 alarmSiren（带自加载兜底）+ 内部源；PlayAlarm 内置 100ms 节流防连发
        public void PlayAlarm()
        {
            if (alarmSiren == null) alarmSiren = LoadReal("alarm_siren");   // 兜底再加载（防 Start 未及加载的时序竞态）
            if (alarmSiren == null) alarmSiren = GenerateTone(600f, 0.30f, 0.5f);
            PlayAlarm(EnsureSfxSource(), alarmSiren);
        }

        public void SetVolume(float v)
        {
            if (bgmSource != null) bgmSource.volume = v;
            Core.SaveSystem.SetVolume("bgm_volume", v); // 持久化
        }

        // ===== P6(F5) 音频框架扩展：单发音效 + 管线接线 =====

        // s3 契约签名：外部传 AudioSource 播单发音效，支持可选 3D 位置（对位 Web play 特效音）
        public void PlaySFX(AudioSource src, AudioClip clip, Vector3? pos = null)
        {
            if (src == null || clip == null) return;
            if (pos.HasValue) src.transform.position = pos.Value;
            src.PlayOneShot(clip);
        }

        // 便捷重载：不传 src（走内部源），供士兵受击/基地摧毁等无独立 AudioSource 的接线点直接调
        public void PlaySFX(AudioClip clip, Vector3? pos = null)
        {
            PlaySFX(EnsureSfxSource(), clip, pos);
        }

        // 战斗开始：播 BGM 占位（复用 PlayBGM 抗叠音逻辑；无外部 BGM 时用低频循环占位音）
        public void PlayBattleBgm()
        {
            if (bgmSource == null) return;
            PlayBGM(bgmMain != null ? bgmMain : (_battleBgm != null ? _battleBgm : (_battleBgm = GenerateTone(180f, 0.8f, 0.35f))));
        }

        // 士兵受击接线（Soldier.TakeDamage 调）
        public void PlayHitSfx(Vector3 pos)
        {
            PlaySFX(sfxHit, pos);
        }

        // 基地摧毁接线（GameBootstrap.OnBaseDestroyed 调）
        public void PlayExplosionSfx(Vector3 pos)
        {
            PlaySFX(sfxExplosion, pos);
        }

        // P1-4 士兵死亡接线（Soldier.Die 调，对位 Web death.wav）
        public void PlayDeathSfx(Vector3 pos)
        {
            PlaySFX(sfxDeath, pos);
        }

        // 圣裁大招音效（UltController.HolyFlow 调，team=0 天庭/蓝）：上升音阶 C4-E4-G4-C5（琶音，对位 S2 TC-D4）
        public void PlayUltimateHeaven()
        {
            // R6：单音1200Hz → 上升音阶 C4(261.63)-E4(329.63)-G4(392)-C5(523.25) 琶音，三角波，0.8s，渐强
            PlaySFX(GenerateArpeggio(new float[] { 261.63f, 329.63f, 392f, 523.25f }, 0.12f, 0.45f, 0.8f));
        }

        // 同死大招音效（UltController.SameFlow 调，team=1 玄朝/红）：下降不协和半音阶 C5-B4-A#4-A4（锯齿+失真感）
        public void PlayUltimateXuan()
        {
            // R6：单音80Hz → 下降半音阶 C5(523.25)-B4(493.88)-A#4(466.16)-A4(440) 下行，锯齿波，1.0s
            PlaySFX(GenerateDescendingChord(new float[] { 523.25f, 493.88f, 466.16f, 440f }, 0.15f, 0.5f, 1.0f));
        }

        // 音阶琶音（多频依次叠加，三角波近似，对位 TC-D4 上升音阶）
        static AudioClip GenerateArpeggio(float[] freqs, float noteDur, float amp, float totalDur)
        {
            int sampleRate = 44100;
            int n = Mathf.Max(1, (int)(sampleRate * totalDur));
            var data = new float[n];
            int notes = freqs.Length;
            float perNote = totalDur / notes;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                int idx = Mathf.Clamp((int)(t / perNote), 0, notes - 1);
                float local = t - idx * perNote;
                float env = Mathf.Clamp01(1f - local / perNote);
                float tri = Triangle(freqs[idx] * t);
                data[i] = tri * env * amp;
            }
            return MakeClip("Arpeggio_" + notes, data, sampleRate);
        }

        // 下行半音阶（对位 TC-D4 下降不协和），末尾叠加低频嗡鸣增强不协和感
        static AudioClip GenerateDescendingChord(float[] freqs, float noteDur, float amp, float totalDur)
        {
            int sampleRate = 44100;
            int n = Mathf.Max(1, (int)(sampleRate * totalDur));
            var data = new float[n];
            int notes = freqs.Length;
            float perNote = totalDur / notes;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                int idx = Mathf.Clamp((int)(t / perNote), 0, notes - 1);
                float local = t - idx * perNote;
                float env = Mathf.Clamp01(1f - local / perNote);
                float saw = Saw(freqs[idx] * t);
                float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.3f; // 低频轰鸣
                data[i] = (saw * 0.8f + rumble) * env * amp;
            }
            return MakeClip("DescChord_" + notes, data, sampleRate);
        }

        static float Triangle(float ph) { float p = ph - Mathf.Floor(ph); return 4f * Mathf.Abs(p - 0.5f) - 1f; }
        static float Saw(float ph) { float p = ph - Mathf.Floor(ph); return 2f * p - 1f; }
        static AudioClip MakeClip(string name, float[] data, int sr)
        {
            var clip = AudioClip.Create(name, data.Length, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // GameOver：停 BGM + 停 SFX（GameBootstrap.OnBaseDestroyed 判胜负后调）
        public void StopAll()
        {
            StopBGM();
            if (_sfxSource != null) _sfxSource.Stop();
        }

        // 惰性建内部 SFX 源（士兵/基地无独立 AudioSource 也能播）；spatialBlend=0 走 2D，免 3D 衰减疑阵（F4 禁距离衰减同理）
        private AudioSource EnsureSfxSource()
        {
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
                _sfxSource.spatialBlend = 0f;
                _sfxSource.playOnAwake = false;
            }
            return _sfxSource;
        }

        // 程序化生成占位音：短促正弦波 + 线性衰减包络（防爆音），F5 无需外部音频文件
        public static AudioClip GenerateTone(float freq, float dur, float amp)
        {
            int sampleRate = 44100;
            int n = Mathf.Max(1, (int)(sampleRate * dur));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - (float)i / n;              // 线性衰减，头部无爆音
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * amp;
            }
            var clip = AudioClip.Create("Generated_Tone_" + freq, n, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
