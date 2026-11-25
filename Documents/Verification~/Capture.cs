using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// ProSharpen の実描画検証: シーン生成、AA/DLSS 別キャプチャ、GPU 負荷計測
public static class Capture
{
    const int W = 1920, H = 1080;
    static string outDir, tag;
    static readonly StringBuilder log = new StringBuilder();
    static readonly StringBuilder csv = new StringBuilder();

    // ------------------------------------------------------------ Setup
    public static void Setup()
    {
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>("Assets/HDRP.asset");
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, "Assets/HDRP.asset");
            }
            var so = new SerializedObject(asset);
            string dr = "m_RenderPipelineSettings.dynamicResolutionSettings.";
            so.FindProperty(dr + "enabled").boolValue = true;
            so.FindProperty(dr + "DLSSUseOptimalSettings").boolValue = false;
            so.FindProperty(dr + "minPercentage").floatValue = 50f;
            so.FindProperty(dr + "maxPercentage").floatValue = 100f;
            so.FindProperty(dr + "forceResolution").boolValue = false;
            var names = so.FindProperty(dr + "advancedUpscalerNames");
            names.ClearArray();
            names.InsertArrayElementAtIndex(0);
            names.GetArrayElementAtIndex(0).stringValue = "DLSS";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;

            // カスタムポストプロセスを After Post Process に登録
            var psType = FindType("LLCheesell.ProSharpen.ProSharpenVolume");
            var hdAsm = typeof(HDRenderPipelineAsset).Assembly;
            var t = hdAsm.GetType("UnityEngine.Rendering.HighDefinition.CustomPostProcessOrdersSettings");
            var get = typeof(GraphicsSettings).GetMethods().First(m => m.Name == "GetRenderPipelineSettings" && m.IsGenericMethod).MakeGenericMethod(t);
            var s = get.Invoke(null, null);
            string msg = "settings=" + (s != null);
            if (s != null)
            {
                var bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var list = t.GetField("m_AfterPostProcessCustomPostProcesses", bf).GetValue(s);
                var strs = (List<string>)list.GetType().GetField("m_CustomPostProcessTypesAsString", bf).GetValue(list);
                if (!strs.Contains(psType.AssemblyQualifiedName)) strs.Add(psType.AssemblyQualifiedName);
                list.GetType().GetField("m_CustomPostProcessTypes", bf).SetValue(list, null);
                msg += " registered=" + strs.Count;
                foreach (var o in Resources.FindObjectsOfTypeAll(hdAsm.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings"))) EditorUtility.SetDirty(o);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("setup_result.txt", msg + " OK");
        }
        catch (Exception e)
        {
            File.WriteAllText("setup_result.txt", "ERROR " + e);
            EditorApplication.Exit(1);
        }
    }

    static Type FindType(string name)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(name);
            if (t != null) return t;
        }
        return null;
    }

    // ------------------------------------------------------------ Run
    public static void Run()
    {
        outDir = Environment.GetEnvironmentVariable("PS_OUT") ?? "out";
        tag = Environment.GetEnvironmentVariable("PS_TAG") ?? "new";
        string only = Environment.GetEnvironmentVariable("PS_ONLY") ?? "";
        Directory.CreateDirectory(outDir);
        try
        {
            log.AppendLine("pipeline=" + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "null"));
            log.AppendLine("device=" + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType);
            log.AppendLine("DLSSDetected=" + HDDynamicResolutionPlatformCapabilities.DLSSDetected);
            csv.AppendLine("tag,scene,aa,sharpen,kind,value");

            Texture2D benchSource = null;
            foreach (var scene in new[] { "photo", "anime", "stage" })
            {
                if (only.Length > 0 && !only.Contains(scene) && !only.Contains("bench")) continue;
                if (only.Contains("bench") && !only.Contains(scene) && scene != "photo") continue;
                var last = CaptureScene(scene, only.Contains("bench") && !only.Contains(scene));
                if (scene == "photo") { benchSource = last; benchSource.hideFlags = HideFlags.HideAndDontSave; }
            }

            if (tag == "new" && (only.Length == 0 || only.Contains("bench")) && benchSource != null)
                Bench(benchSource);

            log.AppendLine("DONE");
        }
        catch (Exception e)
        {
            log.AppendLine("ERROR " + e);
        }
        File.WriteAllText(Path.Combine(outDir, "log_" + tag + ".txt"), log.ToString());
        File.WriteAllText(Path.Combine(outDir, "metrics_" + tag + ".csv"), csv.ToString());
    }

    struct Sharp
    {
        public string name; public int mode, quality; public float sharpness;
        public Sharp(string n, int m, int q, float s) { name = n; mode = m; quality = q; sharpness = s; }
    }

    static Texture2D CaptureScene(string scene, bool minimal)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Camera");
        var cam = camGo.AddComponent<Camera>();
        var hd = camGo.AddComponent<HDAdditionalCameraData>();
        cam.fieldOfView = 45f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.Create();
        cam.targetTexture = rt;

        var volGo = new GameObject("Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        vol.sharedProfile = profile;

        var env = profile.Add<VisualEnvironment>();
        env.skyType.overrideState = true; env.skyType.value = (int)SkyType.Gradient;
        var sky = profile.Add<GradientSky>();
        var exposure = profile.Add<Exposure>();
        exposure.mode.overrideState = true; exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true; exposure.fixedExposure.value = -0.263f;
        var tone = profile.Add<Tonemapping>();
        tone.mode.overrideState = true;
        var bloom = profile.Add<Bloom>();
        bloom.intensity.overrideState = true; bloom.intensity.value = 0f;
        var grain = profile.Add<FilmGrain>();
        grain.intensity.overrideState = true; grain.intensity.value = 0f;

        var psType = FindType("LLCheesell.ProSharpen.ProSharpenVolume");
        var ps = profile.Add(psType);

        if (scene == "photo") { BuildPhoto(cam, sky, tone); exposure.fixedExposure.value = -0.5f; }
        else if (scene == "anime") BuildAnime(cam, sky, tone);
        else { BuildStage(cam, sky, tone, bloom, grain); exposure.fixedExposure.value = -1.0f; }

        var sharps = new List<Sharp>
        {
            new Sharp("off", 0, 0, 0f),
            new Sharp("cas", 0, 0, 0.5f), new Sharp("cas_hq", 0, 1, 0.5f),
            new Sharp("rcas", 3, 0, 0.5f),
            new Sharp("usm", 1, 0, 0.5f), new Sharp("usm_hq", 1, 1, 0.5f),
            new Sharp("adaptive", 2, 0, 0.5f),
        };
        var strong = new List<Sharp>
        {
            new Sharp("cas_s1", 0, 0, 1f), new Sharp("rcas_s1", 3, 0, 1f),
            new Sharp("usm_s1", 1, 0, 1f), new Sharp("adaptive_s1", 2, 0, 1f),
        };
        string[] aas = tag == "new"
            ? new[] { "none", "fxaa", "smaa", "taa0", "taa05", "dlssq", "dlaa" }
            : new[] { "none", "taa05", "dlssq" };
        if (minimal) { aas = new[] { "none" }; sharps = sharps.GetRange(0, 1); strong.Clear(); }

        // 空の環境光・反射の更新が落ち着くまで回す
        ApplySharp(ps, sharps[0]);
        ApplyAA(hd, "none");
        for (int i = 0; i < 60; i++) cam.Render();
        log.AppendLine("colorSpace=" + QualitySettings.activeColorSpace);

        Texture2D last = null;
        var basePos = cam.transform.position;
        foreach (var aa in aas)
        {
            bool temporal = aa.StartsWith("taa") || aa.StartsWith("dl");
            ApplyAA(hd, aa);
            ApplySharp(ps, sharps[0]);
            cam.Render(); cam.Render();
            { var hc = HDCamera.GetOrCreate(cam); log.AppendLine($"{scene} {aa}: actual={hc.actualWidth}x{hc.actualHeight}"); }
            if (temporal) for (int c = 0; c < 6; c++) { cam.Render(); ResetJitter(cam); for (int i = 0; i < 33; i++) cam.Render(); }   // 本番と同じ周期で履歴を収束させる
            var list = new List<Sharp>(sharps);
            if (aa == "none" || aa == "taa05") list.AddRange(strong);

            foreach (var sh in list)
            {
                ApplySharp(ps, sh);
                cam.transform.position = basePos;
                // ジッター列を毎回先頭から始め、設定間で同じ位相のフレームを比較できるようにする
                if (temporal) { cam.Render(); ResetJitter(cam); }
                for (int i = 0; i < (temporal ? 32 : 3); i++) cam.Render();
                var a = Read(rt);
                File.WriteAllBytes(Path.Combine(outDir, $"{tag}_{scene}_{aa}_{sh.name}.png"), a.EncodeToPNG());

                if (temporal || scene == "stage")
                {
                    // 静止カメラでの連続フレーム差 (ちらつき)
                    cam.Render();
                    var b = Read(rt);
                    csv.AppendLine($"{tag},{scene},{aa},{sh.name},flicker,{MeanAbsDiff(a, b):F4}");
                    Object.DestroyImmediate(b);
                }

                if (last != null) Object.DestroyImmediate(last);
                last = a;
            }

            // カメラ移動中のフレーム (静止キャプチャの履歴を乱さないよう最後にまとめて撮る)
            if (tag == "new" && (aa == "taa05" || aa == "dlssq"))
            {
                foreach (var sh in sharps)
                {
                    if (sh.name != "off" && sh.name != "cas" && sh.name != "adaptive" && sh.name != "usm") continue;
                    ApplySharp(ps, sh);
                    cam.transform.position = basePos;
                    cam.Render(); ResetJitter(cam);
                    for (int i = 0; i < 64; i++) cam.Render();
                    for (int i = 1; i <= 10; i++)
                    {
                        cam.transform.position = basePos + cam.transform.right * (0.03f * i);
                        cam.Render();
                    }
                    var m = Read(rt);
                    File.WriteAllBytes(Path.Combine(outDir, $"{tag}_{scene}_{aa}_{sh.name}_pan.png"), m.EncodeToPNG());
                    Object.DestroyImmediate(m);
                }
            }
        }

        // ベンチ用に AA なし・シャープなしの画像を返す
        ApplyAA(hd, "none");
        ApplySharp(ps, sharps[0]);
        cam.transform.position = basePos;
        for (int i = 0; i < 3; i++) cam.Render();
        if (last != null) Object.DestroyImmediate(last);
        last = Read(rt);

        cam.targetTexture = null;
        rt.Release();
        log.AppendLine("scene " + scene + " done");
        return last;
    }

    static void ResetJitter(Camera cam)
    {
        var hdCam = HDCamera.GetOrCreate(cam);
        var f = typeof(HDCamera).GetField("taaFrameIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        f.SetValue(hdCam, 0);
    }

    static float forcedPercent = -1f;

    // 編集モードでは forceResolution が有効なときだけ動的解像度が働く
    static void SetForcedPercentage(float pct)
    {
        if (Mathf.Approximately(forcedPercent, pct)) return;
        forcedPercent = pct;
        var so = new SerializedObject(GraphicsSettings.currentRenderPipeline);
        string dr = "m_RenderPipelineSettings.dynamicResolutionSettings.";
        so.FindProperty(dr + "forceResolution").boolValue = true;
        so.FindProperty(dr + "forcedPercentage").floatValue = pct;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyAA(HDAdditionalCameraData hd, string aa)
    {

        hd.allowDynamicResolution = false;
        hd.allowDeepLearningSuperSampling = false;
        hd.taaSharpenStrength = 0f;
        switch (aa)
        {
            case "none": hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None; break;
            case "fxaa": hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing; break;
            case "smaa":
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                hd.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High; break;
            case "taa0":
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High; break;
            case "taa05":
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
                hd.taaSharpenStrength = 0.5f; break;
            case "dlssq":
            case "dlaa":
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                hd.allowDynamicResolution = true;
                hd.allowDeepLearningSuperSampling = true;
                hd.deepLearningSuperSamplingUseCustomQualitySettings = true;
                hd.deepLearningSuperSamplingUseOptimalSettings = false;
                SetForcedPercentage(aa == "dlaa" ? 100f : 66.7f);
                hd.deepLearningSuperSamplingQuality = DlssQuality(aa == "dlaa" ? "DLAA" : "MaximumQuality");
                break;
        }
    }

    static uint DlssQuality(string name)
    {
        var t = FindType("UnityEngine.NVIDIA.DLSSQuality");
        if (t == null) { log.AppendLine("DLSSQuality type not found"); return 0; }
        return Convert.ToUInt32(Enum.Parse(t, name));
    }

    static void ApplySharp(VolumeComponent ps, Sharp sh)
    {
        SetParam(ps, "enabled", sh.name != "off");
        SetParam(ps, "intensity", sh.name == "off" ? 0f : 1f);
        SetParam(ps, "mode", sh.mode);
        SetParam(ps, "quality", sh.quality);
        SetParam(ps, "sharpness", sh.sharpness);
    }

    static void SetParam(VolumeComponent c, string field, object v)
    {
        var f = c.GetType().GetField(field);
        if (f == null) { log.AppendLine("missing field " + field); return; }
        var p = (VolumeParameter)f.GetValue(c);
        p.overrideState = true;
        PropertyInfo vp = null;
        for (var t = p.GetType(); t != null && vp == null; t = t.BaseType)
            vp = t.GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var pt = vp.PropertyType;
        object val = pt.IsEnum ? Enum.ToObject(pt, Convert.ToInt32(v)) : Convert.ChangeType(v, pt);
        vp.SetValue(p, val);
    }

    static Texture2D Read(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        return tex;
    }

    static double MeanAbsDiff(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32();
        long sum = 0;
        for (int i = 0; i < pa.Length; i++)
            sum += Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b);
        return sum / (3.0 * pa.Length);
    }

    // ------------------------------------------------------------ helpers
    static GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material Lit(Color color, float metallic, float smoothness, Texture2D albedo = null, Texture2D normal = null, Vector2? tiling = null)
    {
        var m = new Material(Shader.Find("HDRP/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (albedo != null) m.SetTexture("_BaseColorMap", albedo);
        if (normal != null) { m.SetTexture("_NormalMap", normal); m.SetFloat("_NormalScale", 1f); }
        if (tiling.HasValue) m.SetTextureScale("_BaseColorMap", tiling.Value);
        HDMaterial.ValidateMaterial(m);
        return m;
    }

    static Material Unlit(Color color, Texture2D tex = null, Vector2? tiling = null)
    {
        var m = new Material(Shader.Find("HDRP/Unlit"));
        m.SetColor("_UnlitColor", color);
        if (tex != null) m.SetTexture("_UnlitColorMap", tex);
        if (tiling.HasValue) m.SetTextureScale("_UnlitColorMap", tiling.Value);
        HDMaterial.ValidateMaterial(m);
        return m;
    }

    static Material Toon(Color color, Color shade, float spec = 0f, float outline = 1.6f)
    {
        var m = new Material(Shader.Find("Test/Toon"));
        m.SetColor("_Color", color); m.SetColor("_Shade", shade);
        m.SetFloat("_Spec", spec); m.SetFloat("_OutlineWidth", outline);
        return m;
    }

    static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static float VNoise(float x, float y, int s, int period)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = ((xi % period) + period) % period, x1 = (x0 + 1) % period;
        int y0 = ((yi % period) + period) % period, y1 = (y0 + 1) % period;
        return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, s), Hash(x1, y0, s), fx), Mathf.Lerp(Hash(x0, y1, s), Hash(x1, y1, s), fx), fy);
    }

    static float Fbm(float u, float v, int s, int baseFreq, int octaves)
    {
        float sum = 0, amp = 0.5f, norm = 0; int f = baseFreq;
        for (int o = 0; o < octaves; o++) { sum += amp * VNoise(u * f, v * f, s + o, f); norm += amp; amp *= 0.5f; f *= 2; }
        return sum / norm;
    }

    static Texture2D Tex(int size, Func<float, float, Color> fn, bool linear = false, bool repeat = true)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = fn((x + 0.5f) / size, (y + 0.5f) / size);
        t.SetPixels(px);
        t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
        t.Apply(true);
        return t;
    }

    static Texture2D NormalFromHeight(int size, Func<float, float, float> height, float strength)
    {
        var h = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                h[y * size + x] = height((x + 0.5f) / size, (y + 0.5f) / size);
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = h[y * size + (x + size - 1) % size], r = h[y * size + (x + 1) % size];
                float d = h[((y + size - 1) % size) * size + x], u = h[((y + 1) % size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        t.SetPixels(px); t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
        t.Apply(true);
        return t;
    }

    static float BrickHeight(float u, float v)
    {
        float rows = 16, cols = 8;
        float y = v * rows; int row = Mathf.FloorToInt(y);
        float x = u * cols + (row % 2) * 0.5f;
        float fx = x - Mathf.Floor(x), fy = y - Mathf.Floor(y);
        float mortar = Mathf.Min(Mathf.Min(fx, 1 - fx) * 4f, Mathf.Min(fy, 1 - fy) * 2f);
        return Mathf.Clamp01(mortar * 12f);
    }

    // 解像度チャート: ジーメンススター、線対、斜めエッジ (高コントラスト / 低コントラスト)
    static Texture2D ChartTex()
    {
        return Tex(1024, (u, v) =>
        {
            float g = 0.5f;
            if (u < 0.5f && v > 0.5f)
            {
                float dx = u - 0.25f, dy = v - 0.75f;
                float a = Mathf.Atan2(dy, dx), r = Mathf.Sqrt(dx * dx + dy * dy);
                g = r < 0.23f ? (Mathf.Sin(a * 36f) > 0 ? 0.95f : 0.03f) : 0.5f;
            }
            else if (u >= 0.5f && v > 0.5f)
            {
                float lu = (u - 0.5f) * 2f, lv = (v - 0.5f) * 2f;
                float freq = Mathf.Lerp(12f, 160f, lu);
                bool on = Mathf.Sin(lu * freq * Mathf.PI) > 0;
                float hi = lv > 0.5f ? 0.95f : 0.56f, lo = lv > 0.5f ? 0.03f : 0.44f;
                g = on ? hi : lo;
            }
            else if (u < 0.5f)
            {
                float lu = u * 2f, lv = v * 2f;
                g = (lu - 0.5f) + (lv - 0.5f) * 0.18f > 0 ? 0.9f : 0.05f;
            }
            else
            {
                float lu = (u - 0.5f) * 2f, lv = v * 2f;
                g = (lu - 0.5f) + (lv - 0.5f) * 0.18f > 0 ? 0.58f : 0.42f;
            }
            return new Color(g, g, g, 1);
        }, false, false);
    }

    static Light Sun(Vector3 euler, float lux, Color color)
    {
        var go = new GameObject("Sun");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        go.AddComponent<HDAdditionalLightData>();
        l.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
        l.intensity = lux; l.color = color; l.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(euler);
        return l;
    }

    static void SetSky(GradientSky sky, Color top, Color mid, Color bottom, float mult)
    {
        sky.top.overrideState = true; sky.top.value = top;
        sky.middle.overrideState = true; sky.middle.value = mid;
        sky.bottom.overrideState = true; sky.bottom.value = bottom;
        sky.multiplier.overrideState = true; sky.multiplier.value = mult;
    }

    // ------------------------------------------------------------ 実写風
    static void BuildPhoto(Camera cam, GradientSky sky, Tonemapping tone)
    {
        tone.mode.value = TonemappingMode.ACES;
        SetSky(sky, new Color(0.25f, 0.45f, 0.9f), new Color(0.6f, 0.75f, 0.95f), new Color(0.35f, 0.33f, 0.3f), 1.6f);
        Sun(new Vector3(42, -38, 0), 9f, new Color(1f, 0.96f, 0.88f));

        var asphalt = Tex(1024, (u, v) =>
        {
            float n = Fbm(u, v, 1, 8, 7); float f = Hash((int)(u * 1024), (int)(v * 1024), 9);
            float g = 0.16f + 0.22f * n + 0.10f * (f - 0.5f);
            return new Color(g, g * 0.98f, g * 0.95f, 1);
        });
        var asphaltN = NormalFromHeight(1024, (u, v) => Fbm(u, v, 3, 32, 5) + 0.3f * Hash((int)(u * 1024), (int)(v * 1024), 4), 3f);
        var brick = Tex(1024, (u, v) =>
        {
            float h = BrickHeight(u, v); float n = Fbm(u, v, 5, 16, 6);
            int row = Mathf.FloorToInt(v * 16), col = Mathf.FloorToInt(u * 8 + (row % 2) * 0.5f);
            float tint = Hash(col, row, 7);
            var b = Color.Lerp(new Color(0.42f, 0.16f, 0.10f), new Color(0.62f, 0.30f, 0.20f), tint) * (0.7f + 0.6f * n);
            var c = Color.Lerp(new Color(0.55f, 0.53f, 0.5f) * (0.8f + 0.4f * n), b, h);
            c.a = 1; return c;
        });
        var brickN = NormalFromHeight(1024, (u, v) => BrickHeight(u, v) + 0.25f * Fbm(u, v, 6, 64, 4), 2.5f);
        var weave = Tex(512, (u, v) =>
        {
            float a = Mathf.Sin(u * 512 * 0.5f * Mathf.PI), b = Mathf.Sin(v * 512 * 0.5f * Mathf.PI);
            float w = 0.5f + 0.5f * a * b; float n = Fbm(u, v, 11, 8, 4);
            return new Color(0.15f + 0.5f * w * n, 0.25f + 0.35f * w, 0.5f + 0.3f * w, 1);
        });
        var weaveN = NormalFromHeight(512, (u, v) => Mathf.Sin(u * 512 * 0.5f * Mathf.PI) * Mathf.Sin(v * 512 * 0.5f * Mathf.PI), 1.5f);
        var rock = Tex(1024, (u, v) =>
        {
            float n = Fbm(u, v, 21, 6, 8); float g = 0.25f + 0.55f * n;
            return new Color(g, g * 0.95f, g * 0.85f, 1);
        });
        var rockN = NormalFromHeight(1024, (u, v) => Fbm(u, v, 21, 6, 8), 6f);

        Prim(PrimitiveType.Plane, Vector3.zero, new Vector3(6, 1, 6), Quaternion.identity, Lit(Color.white, 0f, 0.35f, asphalt, asphaltN, new Vector2(14, 14)));
        Prim(PrimitiveType.Cube, new Vector3(0, 2, 6), new Vector3(16, 4, 0.3f), Quaternion.identity, Lit(Color.white, 0f, 0.2f, brick, brickN, new Vector2(5, 1.25f)));

        float[] metal = { 0f, 0f, 1f, 1f, 0f };
        float[] smooth = { 0.2f, 0.85f, 0.9f, 0.5f, 0.6f };
        Color[] cols = { Color.white, new Color(0.8f, 0.1f, 0.1f), new Color(0.95f, 0.93f, 0.88f), new Color(1f, 0.77f, 0.34f), new Color(0.1f, 0.4f, 0.2f) };
        for (int i = 0; i < 5; i++)
            Prim(PrimitiveType.Sphere, new Vector3(-3f + i * 1.5f, 0.55f, 1.2f), Vector3.one * 1.1f, Quaternion.identity,
                Lit(cols[i], metal[i], smooth[i], i == 0 ? rock : null, i == 0 || i == 3 ? rockN : null, new Vector2(2, 1)));

        Prim(PrimitiveType.Cube, new Vector3(-2.6f, 0.5f, -1.2f), Vector3.one, Quaternion.Euler(0, 28, 0), Lit(Color.white, 0f, 0.25f, weave, weaveN, new Vector2(6, 6)));
        Prim(PrimitiveType.Cube, new Vector3(-0.9f, 0.35f, -1.6f), new Vector3(0.7f, 0.7f, 0.7f), Quaternion.Euler(0, -15, 0), Lit(Color.white, 0f, 0.3f, rock, rockN, new Vector2(1, 1)));

        // 細いワイヤー (空と壁を横切る)
        var wire = Lit(new Color(0.03f, 0.03f, 0.03f), 0f, 0.4f);
        for (int i = 0; i < 6; i++)
            Prim(PrimitiveType.Cylinder, new Vector3(0, 3.2f + i * 0.28f, 3.5f), new Vector3(0.006f + 0.003f * i, 9f, 0.006f + 0.003f * i), Quaternion.Euler(0, 0, 86 + i * 1.7f), wire);

        // フェンス
        var steel = Lit(new Color(0.75f, 0.76f, 0.78f), 1f, 0.7f);
        for (int i = 0; i < 44; i++)
            Prim(PrimitiveType.Cylinder, new Vector3(0.9f + i * 0.075f, 0.6f, -1.0f), new Vector3(0.016f, 0.6f, 0.016f), Quaternion.identity, steel);
        Prim(PrimitiveType.Cylinder, new Vector3(2.5f, 1.15f, -1.0f), new Vector3(0.03f, 1.7f, 0.03f), Quaternion.Euler(0, 0, 90), steel);
        Prim(PrimitiveType.Cylinder, new Vector3(2.5f, 0.1f, -1.0f), new Vector3(0.03f, 1.7f, 0.03f), Quaternion.Euler(0, 0, 90), steel);

        // 茂み
        var rnd = new System.Random(3);
        var leaves = new[] { Lit(new Color(0.10f, 0.32f, 0.06f), 0f, 0.45f), Lit(new Color(0.18f, 0.45f, 0.10f), 0f, 0.5f), Lit(new Color(0.06f, 0.22f, 0.05f), 0f, 0.4f) };
        var bush = new GameObject("Bush").transform;
        for (int i = 0; i < 600; i++)
        {
            var p = UnityEngine.Random.insideUnitSphere; p = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f);
            if (p.magnitude > 0.5f) continue;
            Prim(PrimitiveType.Sphere, new Vector3(4.2f, 0.9f, 2.6f) + p * 1.9f, new Vector3(0.16f, 0.012f, 0.07f),
                Quaternion.Euler((float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360), leaves[i % 3], bush);
        }

        Prim(PrimitiveType.Quad, new Vector3(-4.6f, 1.6f, 3.2f), new Vector3(2.0f, 2.0f, 1), Quaternion.Euler(0, -25, 0), Unlit(Color.white * 0.9f, ChartTex()));

        cam.transform.position = new Vector3(0, 1.7f, -7f);
        cam.transform.LookAt(new Vector3(0, 1.3f, 2f));
    }

    // ------------------------------------------------------------ アニメ風
    static void Character(Vector3 pos, float yaw, Color cloth, Color hairCol, float outline)
    {
        var root = new GameObject("Chara").transform;
        root.position = pos; root.rotation = Quaternion.Euler(0, yaw, 0);
        var skin = Toon(new Color(1f, 0.86f, 0.76f), new Color(0.92f, 0.7f, 0.72f), 0f, outline);
        var hair = Toon(hairCol, new Color(0.55f, 0.5f, 0.75f), 0.55f, outline);
        var clothM = Toon(cloth, new Color(0.6f, 0.6f, 0.8f), 0f, outline);
        var white = Toon(Color.white, new Color(0.8f, 0.85f, 0.95f), 0f, outline);
        var dark = Toon(new Color(0.08f, 0.07f, 0.12f), new Color(0.5f, 0.5f, 0.6f), 0f, 0f);
        var iris = Toon(new Color(0.15f, 0.5f, 0.85f), new Color(0.7f, 0.7f, 0.9f), 0f, 0.8f);
        var red = Toon(new Color(0.9f, 0.12f, 0.2f), new Color(0.65f, 0.5f, 0.6f), 0f, outline);

        Prim(PrimitiveType.Capsule, new Vector3(0, 0.95f, 0), new Vector3(0.42f, 0.38f, 0.3f), Quaternion.identity, clothM, root);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.62f, 0), new Vector3(0.62f, 0.14f, 0.62f), Quaternion.identity, white, root);
        Prim(PrimitiveType.Capsule, new Vector3(-0.11f, 0.3f, 0), new Vector3(0.13f, 0.32f, 0.13f), Quaternion.identity, skin, root);
        Prim(PrimitiveType.Capsule, new Vector3(0.11f, 0.3f, 0), new Vector3(0.13f, 0.32f, 0.13f), Quaternion.identity, skin, root);
        Prim(PrimitiveType.Capsule, new Vector3(-0.3f, 1.0f, 0), new Vector3(0.1f, 0.3f, 0.1f), Quaternion.Euler(0, 0, -22), skin, root);
        Prim(PrimitiveType.Capsule, new Vector3(0.3f, 1.05f, 0), new Vector3(0.1f, 0.3f, 0.1f), Quaternion.Euler(0, 0, 40), skin, root);
        Prim(PrimitiveType.Sphere, new Vector3(0, 1.52f, 0), new Vector3(0.42f, 0.44f, 0.42f), Quaternion.identity, skin, root);
        Prim(PrimitiveType.Sphere, new Vector3(0, 1.58f, 0.05f), new Vector3(0.48f, 0.44f, 0.46f), Quaternion.identity, hair, root);
        for (int i = 0; i < 7; i++)
        {
            float a = (i - 3) * 22f;
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.2f, 1.62f, -0.13f * Mathf.Cos(a * Mathf.Deg2Rad)), new Vector3(0.09f, 0.26f, 0.09f), Quaternion.Euler(18, 0, -a * 0.6f), hair, root);
        }
        Prim(PrimitiveType.Sphere, new Vector3(-0.2f, 1.3f, 0.12f), new Vector3(0.1f, 0.6f, 0.1f), Quaternion.Euler(0, 0, 6), hair, root);
        Prim(PrimitiveType.Sphere, new Vector3(0.2f, 1.3f, 0.12f), new Vector3(0.1f, 0.6f, 0.1f), Quaternion.Euler(0, 0, -6), hair, root);
        foreach (float sx in new[] { -0.085f, 0.085f })
        {
            Prim(PrimitiveType.Sphere, new Vector3(sx, 1.5f, -0.185f), new Vector3(0.1f, 0.12f, 0.03f), Quaternion.identity, white, root);
            Prim(PrimitiveType.Sphere, new Vector3(sx, 1.495f, -0.197f), new Vector3(0.065f, 0.09f, 0.02f), Quaternion.identity, iris, root);
            Prim(PrimitiveType.Sphere, new Vector3(sx, 1.495f, -0.205f), new Vector3(0.03f, 0.045f, 0.012f), Quaternion.identity, dark, root);
            Prim(PrimitiveType.Sphere, new Vector3(sx + 0.018f, 1.52f, -0.21f), new Vector3(0.016f, 0.016f, 0.008f), Quaternion.identity, Unlit(Color.white), root);
            Prim(PrimitiveType.Cube, new Vector3(sx, 1.565f, -0.19f), new Vector3(0.11f, 0.008f, 0.01f), Quaternion.Euler(0, 0, sx > 0 ? -8 : 8), dark, root);
        }
        Prim(PrimitiveType.Cube, new Vector3(0, 1.4f, -0.2f), new Vector3(0.05f, 0.006f, 0.01f), Quaternion.identity, dark, root);
        Prim(PrimitiveType.Cube, new Vector3(0, 1.2f, -0.16f), new Vector3(0.2f, 0.09f, 0.04f), Quaternion.Euler(0, 0, 0), red, root);
        Prim(PrimitiveType.Cube, new Vector3(0.12f, 1.8f, 0), new Vector3(0.22f, 0.1f, 0.04f), Quaternion.Euler(0, 0, 25), red, root);
    }

    static void BuildAnime(Camera cam, GradientSky sky, Tonemapping tone)
    {
        tone.mode.value = TonemappingMode.None;
        SetSky(sky, new Color(0.3f, 0.5f, 0.95f), new Color(0.6f, 0.8f, 1f), new Color(0.7f, 0.85f, 0.95f), 1f);

        var backdrop = Tex(2048, (u, v) =>
        {
            var c = Color.Lerp(new Color(0.75f, 0.9f, 1f), new Color(0.2f, 0.45f, 0.95f), v);
            float n = Fbm(u * 2f, v * 4f, 31, 3, 5);
            float edge = Mathf.Clamp01((n - 0.55f - (v - 0.5f) * 0.15f) * 400f);
            float inner = Mathf.Clamp01((n - 0.62f) * 400f);
            c = Color.Lerp(c, new Color(0.86f, 0.9f, 0.98f), edge);
            c = Color.Lerp(c, Color.white, inner);
            c.a = 1; return c;
        }, false, false);
        Prim(PrimitiveType.Quad, new Vector3(0, 6, 14), new Vector3(44, 16, 1), Quaternion.identity, Unlit(Color.white, backdrop));

        var ground = Tex(1024, (u, v) =>
        {
            float n = Fbm(u, v, 41, 6, 4);
            float t = Mathf.Clamp01((n - 0.5f) * 300f);
            var c = Color.Lerp(new Color(0.35f, 0.7f, 0.3f), new Color(0.25f, 0.58f, 0.28f), t);
            c.a = 1; return c;
        });
        Prim(PrimitiveType.Plane, Vector3.zero, new Vector3(6, 1, 6), Quaternion.identity, Unlit(Color.white, ground, new Vector2(4, 4)));

        // 線画パネル: 太さの違う線、ベタ塗り、ハーフトーン
        var lineArt = Tex(2048, (u, v) =>
        {
            float px = 1f / 2048f;
            var c = new Color(0.98f, 0.97f, 0.94f, 1);
            if (u > 0.55f && v < 0.45f)
            {
                float cell = 0.012f; float du = (u % cell) / cell - 0.5f, dv = (v % cell) / cell - 0.5f;
                float rad = Mathf.Lerp(0.1f, 0.45f, (u - 0.55f) / 0.45f);
                float d = (Mathf.Sqrt(du * du + dv * dv) - rad) * cell / px;
                c = Color.Lerp(new Color(0.1f, 0.1f, 0.15f, 1), c, Mathf.Clamp01(d + 0.5f));
            }
            if (u < 0.5f && v < 0.45f)
            {
                float t = Mathf.Clamp01((Mathf.Sin(u * 30f) * 0.08f + 0.25f - v) / px + 0.5f);
                c = Color.Lerp(c, new Color(0.95f, 0.45f, 0.55f, 1), t);
                float t2 = Mathf.Clamp01((Mathf.Sin(u * 22f + 1f) * 0.05f + 0.12f - v) / px + 0.5f);
                c = Color.Lerp(c, new Color(0.3f, 0.35f, 0.8f, 1), t2);
            }
            float ink = 0f;
            for (int i = 0; i < 9; i++)
            {
                float y0 = 0.52f + i * 0.05f;
                float curve = y0 + Mathf.Sin(u * (6f + i * 3f) + i) * 0.018f;
                float width = (0.5f + i * 0.45f) * px;
                ink = Mathf.Max(ink, Mathf.Clamp01((width - Mathf.Abs(v - curve)) / px + 0.5f));
            }
            for (int i = 0; i < 6; i++)
            {
                float x0 = 0.08f + i * 0.07f;
                float line = x0 + (v - 0.5f) * 0.25f;
                float width = (0.5f + i * 0.5f) * px;
                if (v < 0.5f) ink = Mathf.Max(ink, Mathf.Clamp01((width - Mathf.Abs(u - line)) / px + 0.5f) * (v < 0.45f ? 1 : 0));
            }
            float border = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            ink = Mathf.Max(ink, Mathf.Clamp01((0.012f - border) / px));
            c = Color.Lerp(c, new Color(0.04f, 0.03f, 0.05f, 1), ink);
            return c;
        }, false, false);
        Prim(PrimitiveType.Quad, new Vector3(-3.3f, 1.7f, 2.2f), new Vector3(3.0f, 3.0f, 1), Quaternion.Euler(0, -18, 0), Unlit(Color.white, lineArt));
        Prim(PrimitiveType.Quad, new Vector3(4.6f, 1.1f, 4.5f), new Vector3(1.6f, 1.6f, 1), Quaternion.Euler(0, 20, 0), Unlit(Color.white, ChartTex()));

        Character(new Vector3(1.7f, 0, -3.4f), -12, new Color(0.25f, 0.3f, 0.75f), new Color(0.95f, 0.8f, 0.3f), 1.8f);
        Character(new Vector3(0.2f, 0, -0.5f), 14, new Color(0.85f, 0.35f, 0.5f), new Color(0.3f, 0.2f, 0.5f), 1.4f);
        Character(new Vector3(-0.6f, 0, 5.5f), 0, new Color(0.3f, 0.7f, 0.6f), new Color(0.85f, 0.35f, 0.3f), 1.0f);
        Character(new Vector3(2.4f, 0, 9.5f), 5, new Color(0.9f, 0.8f, 0.3f), new Color(0.2f, 0.25f, 0.3f), 1.0f);

        var box = Toon(new Color(0.95f, 0.85f, 0.7f), new Color(0.7f, 0.65f, 0.8f), 0f, 1.6f);
        Prim(PrimitiveType.Cube, new Vector3(4.2f, 1.5f, 8f), new Vector3(2.5f, 3f, 2.5f), Quaternion.Euler(0, 30, 0), box);
        Prim(PrimitiveType.Cylinder, new Vector3(-4.6f, 1.2f, 7f), new Vector3(0.5f, 1.2f, 0.5f), Quaternion.identity, Toon(new Color(0.55f, 0.35f, 0.2f), new Color(0.6f, 0.55f, 0.7f), 0f, 1.6f));
        Prim(PrimitiveType.Sphere, new Vector3(-4.6f, 3.3f, 7f), new Vector3(3f, 2.6f, 3f), Quaternion.identity, Toon(new Color(0.3f, 0.7f, 0.35f), new Color(0.55f, 0.7f, 0.75f), 0f, 1.6f));

        cam.transform.position = new Vector3(0, 1.45f, -6f);
        cam.transform.LookAt(new Vector3(0.5f, 1.3f, 2f));
    }

    // ------------------------------------------------------------ ライブステージ風 (暗所 + LED + グレイン)
    static void BuildStage(Camera cam, GradientSky sky, Tonemapping tone, Bloom bloom, FilmGrain grain)
    {
        tone.mode.value = TonemappingMode.ACES;
        SetSky(sky, Color.black, new Color(0.01f, 0.01f, 0.02f), Color.black, 1f);
        bloom.intensity.value = 0.25f;
        grain.intensity.value = 0.35f;
        grain.type.overrideState = true; grain.type.value = FilmGrainLookup.Medium1;

        Prim(PrimitiveType.Plane, Vector3.zero, new Vector3(5, 1, 5), Quaternion.identity, Lit(new Color(0.03f, 0.03f, 0.035f), 0f, 0.82f));

        // LED ウォール: 画素格子つきの映像
        var led = Tex(2048, (u, v) =>
        {
            float cells = 256f;
            float cu = Mathf.Floor(u * cells) / cells, cv = Mathf.Floor(v * cells) / cells;
            float n = Fbm(cu * 2f, cv, 51, 3, 4);
            var c = Color.HSVToRGB(Mathf.Repeat(cu * 1.3f + n * 0.6f, 1f), 0.85f, Mathf.Clamp01(n * 1.6f));
            float stripe = Mathf.Abs(cv - 0.5f) < 0.12f && Mathf.Repeat(cu * 6f, 1f) < 0.5f ? 1f : 0f;
            c = Color.Lerp(c, Color.white, stripe);
            float fu = u * cells - Mathf.Floor(u * cells) - 0.5f, fv = v * cells - Mathf.Floor(v * cells) - 0.5f;
            float dot = Mathf.Clamp01((0.36f - Mathf.Sqrt(fu * fu + fv * fv)) * 8f);
            c *= dot; c.a = 1; return c;
        }, false, false);
        Prim(PrimitiveType.Quad, new Vector3(0, 2.6f, 7), new Vector3(13, 5, 1), Quaternion.identity, Unlit(Color.white * 2.2f, led, new Vector2(1f, 0.385f)));

        // 出演者
        Color[] cloth = { new Color(0.8f, 0.8f, 0.85f), new Color(0.7f, 0.15f, 0.2f), new Color(0.15f, 0.2f, 0.6f) };
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-2.2f + i * 2.2f, 0, 1.5f - Mathf.Abs(i - 1) * 0.8f);
            Prim(PrimitiveType.Capsule, p + new Vector3(0, 0.9f, 0), new Vector3(0.45f, 0.9f, 0.3f), Quaternion.identity, Lit(cloth[i], 0f, 0.35f));
            Prim(PrimitiveType.Sphere, p + new Vector3(0, 1.95f, 0), Vector3.one * 0.32f, Quaternion.identity, Lit(new Color(0.85f, 0.65f, 0.55f), 0f, 0.45f));
        }

        // トラスと灯体
        var truss = Lit(new Color(0.2f, 0.2f, 0.2f), 1f, 0.5f);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 5.0f, 3f), new Vector3(0.05f, 7f, 0.05f), Quaternion.Euler(0, 0, 90), truss);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 5.3f, 3f), new Vector3(0.05f, 7f, 0.05f), Quaternion.Euler(0, 0, 90), truss);
        for (int i = 0; i < 28; i++)
            Prim(PrimitiveType.Cylinder, new Vector3(-6.7f + i * 0.5f, 5.15f, 3f), new Vector3(0.02f, 0.22f, 0.02f), Quaternion.Euler(0, 0, i % 2 == 0 ? 55 : -55), truss);
        Color[] lc = { new Color(1f, 0.25f, 0.4f), new Color(0.3f, 0.5f, 1f), new Color(1f, 0.9f, 0.7f), new Color(0.3f, 1f, 0.6f), new Color(1f, 0.5f, 0.1f) };
        for (int i = 0; i < 5; i++)
        {
            var p = new Vector3(-5f + i * 2.5f, 4.8f, 3f);
            Prim(PrimitiveType.Sphere, p, Vector3.one * 0.14f, Quaternion.identity, Unlit(lc[i] * 14f));
            var go = new GameObject("Spot");
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 50f; l.innerSpotAngle = 30f;
            go.AddComponent<HDAdditionalLightData>();
            l.intensity = 420f; l.color = lc[i]; l.range = 30f; l.shadows = LightShadows.None;
            log.AppendLine("spot unit=" + l.lightUnit + " intensity=" + l.intensity);
            go.transform.position = new Vector3(p.x, 5.2f, -3.5f);
            go.transform.LookAt(new Vector3(-3.3f + i * 1.65f, 1.0f, 1.2f));
        }

        cam.transform.position = new Vector3(0, 1.8f, -7.5f);
        cam.transform.LookAt(new Vector3(0, 2.0f, 3f));
    }

    // ------------------------------------------------------------ シェーダー単体の検証
    // 保存済みの「シャープなし」画像を入力にして、URP 用 SubShader でシャープだけを実行する
    public static void Probe()
    {
        outDir = Environment.GetEnvironmentVariable("PS_OUT");
        var sb = new StringBuilder();
        try
        {
            // HDRP を 1 回描画して Blitter などを初期化する
            var camGo = new GameObject("Camera"); var cam = camGo.AddComponent<Camera>(); camGo.AddComponent<HDAdditionalCameraData>();
            var tmp = new RenderTexture(256, 256, 24); cam.targetTexture = tmp; cam.Render(); cam.targetTexture = null;

            string prevRp = Shader.globalRenderPipeline;
            Shader.globalRenderPipeline = "UniversalPipeline";
            var mat = new Material(Shader.Find("Hidden/ProSharpen/ProSharpen"));
            mat.SetFloat("_Intensity", 1f); mat.SetFloat("_Sharpness", 0.5f);
            mat.SetFloat("_EdgeProtection", 0.5f); mat.SetFloat("_DetailBoost", 0.3f);
            mat.SetFloat("_NoiseThreshold", 0.02f); mat.SetFloat("_ClampLimit", 0.15f);
            mat.SetFloat("_CornerWeight", 0.5f); mat.SetFloat("_Overshoot", 0.01f); mat.SetFloat("_Undershoot", 0.03f);
            var setup = FindType("LLCheesell.ProSharpen.ProSharpenMaterialSetup");
            var qualityType = FindType("LLCheesell.ProSharpen.SharpenQuality");
            mat.SetVector("_BlurParams", (Vector4)setup.GetMethod("ComputeBlurParams").Invoke(null, new object[] { 1f, Enum.ToObject(qualityType, 0) }));

            foreach (var scene0 in new[] { "photo", "anime", "stage" })
            foreach (var aa in new[] { "none", "smaa", "taa0", "taa05", "dlssq", "dlaa" })
            {
                string scene = scene0 + "_" + aa;
                var src2d = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                src2d.LoadImage(File.ReadAllBytes(Path.Combine(outDir, $"new_{scene}_off.png")));
                src2d.filterMode = FilterMode.Point;
                var src = new RenderTexture(src2d.width, src2d.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                src.Create();
                Graphics.Blit(src2d, src);
                var dst = new RenderTexture(src2d.width, src2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                dst.Create();
                var srcH = RTHandles.Alloc(src);
                foreach (var m in new[] { "CAS", "UNSHARP", "ADAPTIVE", "RCAS" })
                {
                    mat.shaderKeywords = new[] { "PROSHARPEN_" + m, "PROSHARPEN_LUMA_ONLY" };
                    var cmd = new CommandBuffer();
                    cmd.SetRenderTarget(dst);
                    Blitter.BlitTexture(cmd, srcH, new Vector4(1, 1, 0, 0), mat, 0);
                    Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
                    var t = Read(dst);
                    File.WriteAllBytes(Path.Combine(outDir, $"probe_{scene}_{m}.png"), t.EncodeToPNG());
                    Object.DestroyImmediate(t);
                }
                srcH.Release(); src.Release(); dst.Release(); Object.DestroyImmediate(src2d);
            }
            Shader.globalRenderPipeline = prevRp;
            sb.AppendLine("PROBE DONE");
        }
        catch (Exception e) { sb.AppendLine("ERROR " + e); }
        File.WriteAllText(Path.Combine(outDir, "probe_log.txt"), sb.ToString());
    }

    // 既知の float 入力をシェーダーへ通し、float のまま出力を保存する (数値ポートとの厳密比較用)
    public static void Exact()
    {
        string dir = Environment.GetEnvironmentVariable("PS_OUT");
        var sb = new StringBuilder();
        try
        {
            var camGo = new GameObject("Camera"); var cam = camGo.AddComponent<Camera>(); camGo.AddComponent<HDAdditionalCameraData>();
            var tmp = new RenderTexture(256, 256, 24); cam.targetTexture = tmp; cam.Render(); cam.targetTexture = null;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
            tex.LoadRawTextureData(File.ReadAllBytes(Path.Combine(dir, "input.raw"))); tex.Apply(false);
            var src = new RenderTexture(N, N, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            src.Create(); Graphics.Blit(tex, src);
            var dst = new RenderTexture(N, N, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); dst.Create();
            var srcH = RTHandles.Alloc(src);
            string prevRp = Shader.globalRenderPipeline;
            Shader.globalRenderPipeline = "UniversalPipeline";
            var mat = new Material(Shader.Find("Hidden/ProSharpen/ProSharpen"));
            mat.SetFloat("_Intensity", 1f); mat.SetFloat("_Sharpness", 0.5f);
            mat.SetFloat("_EdgeProtection", 0.5f); mat.SetFloat("_DetailBoost", 0.3f);
            mat.SetFloat("_NoiseThreshold", 0.02f); mat.SetFloat("_ClampLimit", 0.15f);
            mat.SetFloat("_CornerWeight", 0.5f); mat.SetFloat("_Overshoot", 0.01f); mat.SetFloat("_Undershoot", 0.03f);
            var setup = FindType("LLCheesell.ProSharpen.ProSharpenMaterialSetup");
            var qualityType = FindType("LLCheesell.ProSharpen.SharpenQuality");
            var compute = setup.GetMethod("ComputeBlurParams");
            foreach (var m in new[] { "CAS", "UNSHARP", "ADAPTIVE", "RCAS" })
                foreach (bool hq in new[] { false, true })
                    foreach (bool luma in new[] { true, false })
                    {
                        var kw = new List<string> { "PROSHARPEN_" + m };
                        if (hq) kw.Add("PROSHARPEN_HIGH_QUALITY");
                        if (luma) kw.Add("PROSHARPEN_LUMA_ONLY");
                        mat.shaderKeywords = kw.ToArray();
                        var bp = (Vector4)compute.Invoke(null, new object[] { 1f, Enum.ToObject(qualityType, hq ? 1 : 0) });
                        mat.SetVector("_BlurParams", bp);
                        var cmd = new CommandBuffer(); cmd.SetRenderTarget(dst);
                        Blitter.BlitTexture(cmd, srcH, new Vector4(1, 1, 0, 0), mat, 0);
                        Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
                        var prev = RenderTexture.active; RenderTexture.active = dst;
                        var o = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
                        o.ReadPixels(new Rect(0, 0, N, N), 0, 0); RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(dir, $"out_{m}{(hq ? "_HQ" : "")}{(luma ? "_luma" : "_rgb")}.raw"), o.GetRawTextureData());
                        sb.AppendLine($"{m} hq={hq} luma={luma} blur={bp.x:F5},{bp.y:F5},{bp.z:F5},{bp.w:F5}");
                    }
            Shader.globalRenderPipeline = prevRp;
            sb.AppendLine("EXACT DONE");
        }
        catch (Exception e) { sb.AppendLine("ERROR " + e); }
        File.WriteAllText(Path.Combine(dir, "exact_log.txt"), sb.ToString());
    }

    // ------------------------------------------------------------ GPU 負荷計測
    static void Bench(Texture2D source)
    {
        const int BW = 3840, BH = 2160, N = 1500;
        var fmt = GraphicsFormat.B10G11R11_UFloatPack32;
        var src = new RenderTexture(BW, BH, 0, fmt) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var dst = new RenderTexture(BW, BH, 0, fmt);
        src.Create(); dst.Create();
        Graphics.Blit(source, src);
        var srcH = RTHandles.Alloc(src); var dstH = RTHandles.Alloc(dst);

        string prevRp = Shader.globalRenderPipeline;
        Shader.globalRenderPipeline = "UniversalPipeline";
        var shader = Shader.Find("Hidden/ProSharpen/ProSharpen");
        var mat = new Material(shader);
        mat.SetFloat("_Intensity", 1f); mat.SetFloat("_Sharpness", 0.5f);
        mat.SetFloat("_EdgeProtection", 0.5f); mat.SetFloat("_DetailBoost", 0.3f);
        mat.SetFloat("_NoiseThreshold", 0.02f); mat.SetFloat("_ClampLimit", 0.15f);
        mat.SetFloat("_CornerWeight", 0.5f); mat.SetFloat("_Overshoot", 0.01f); mat.SetFloat("_Undershoot", 0.03f);

        var setup = FindType("LLCheesell.ProSharpen.ProSharpenMaterialSetup");
        var qualityType = FindType("LLCheesell.ProSharpen.SharpenQuality");
        var compute = setup.GetMethod("ComputeBlurParams");

        var cases = new List<(string name, string[] kw)>();
        cases.Add(("copy", null));
        string[] modes = { "CAS", "RCAS", "UNSHARP", "ADAPTIVE" };
        foreach (var m in modes)
            foreach (bool hq in new[] { false, true })
                foreach (bool luma in new[] { true, false })
                {
                    if (m == "ADAPTIVE" && hq) continue;
                    var kw = new List<string> { "PROSHARPEN_" + m };
                    if (hq) kw.Add("PROSHARPEN_HIGH_QUALITY");
                    if (luma) kw.Add("PROSHARPEN_LUMA_ONLY");
                    cases.Add(($"{m}{(hq ? "_HQ" : "")}{(luma ? "_luma" : "_rgb")}", kw.ToArray()));
                }

        var scaleBias = new Vector4(1, 1, 0, 0);
        var results = new Dictionary<string, double>();
        for (int pass = 0; pass < 4; pass++)
        {
            foreach (var c in cases)
            {
                if (c.kw != null)
                {
                    mat.shaderKeywords = c.kw;
                    bool hq = c.kw.Contains("PROSHARPEN_HIGH_QUALITY");
                    mat.SetVector("_BlurParams", (Vector4)compute.Invoke(null, new object[] { 1f, Enum.ToObject(qualityType, hq ? 1 : 0) }));
                }
                var cmd = new CommandBuffer();
                cmd.SetRenderTarget(dst);
                for (int i = 0; i < N; i++)
                {
                    if (c.kw == null) Blitter.BlitTexture(cmd, srcH, scaleBias, 0f, false);
                    else Blitter.BlitTexture(cmd, srcH, scaleBias, mat, 0);
                }
                // ウォームアップ (シェーダーコンパイルを計測から外す)
                var warm = new CommandBuffer();
                warm.SetRenderTarget(dst);
                if (c.kw == null) Blitter.BlitTexture(warm, srcH, scaleBias, 0f, false);
                else Blitter.BlitTexture(warm, srcH, scaleBias, mat, 0);
                Graphics.ExecuteCommandBuffer(warm);
                AsyncGPUReadback.Request(dst, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();

                var sw = Stopwatch.StartNew();
                Graphics.ExecuteCommandBuffer(cmd);
                AsyncGPUReadback.Request(dst, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / N;
                if (!results.ContainsKey(c.name) || ms < results[c.name]) results[c.name] = ms;
                cmd.Release(); warm.Release();

                if (pass == 0 && c.kw != null)
                {
                    // 出力の保存 (最適化の前後で一致を確認するため)
                    var one = new CommandBuffer();
                    var outRt = new RenderTexture(BW, BH, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    outRt.Create();
                    var outH = RTHandles.Alloc(outRt);
                    one.SetRenderTarget(outRt);
                    Blitter.BlitTexture(one, srcH, scaleBias, mat, 0);
                    Graphics.ExecuteCommandBuffer(one);
                    one.Release();
                    var prev = RenderTexture.active; RenderTexture.active = outRt;
                    var tex = new Texture2D(1024, 1024, TextureFormat.RGBAFloat, false, true);
                    tex.ReadPixels(new Rect(1400, 600, 1024, 1024), 0, 0);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(outDir, "bench_" + c.name + ".raw"), tex.GetRawTextureData());
                    Object.DestroyImmediate(tex);
                    outH.Release(); outRt.Release();
                }
            }
        }
        foreach (var c in cases)
            csv.AppendLine($"{tag},bench4k,-,{c.name},ms_per_pass,{results[c.name]:F5}");

        // シャープ量 0 (マスク外に相当) の早期リターンの効果
        {
            mat.shaderKeywords = new[] { "PROSHARPEN_ADAPTIVE", "PROSHARPEN_LUMA_ONLY" };
            mat.SetFloat("_Intensity", 0f);
            double best = 1e9;
            for (int pass = 0; pass < 4; pass++)
            {
                var cmd = new CommandBuffer(); cmd.SetRenderTarget(dst);
                for (int i = 0; i < N; i++) Blitter.BlitTexture(cmd, srcH, scaleBias, mat, 0);
                var warm = new CommandBuffer(); warm.SetRenderTarget(dst);
                Blitter.BlitTexture(warm, srcH, scaleBias, mat, 0);
                Graphics.ExecuteCommandBuffer(warm);
                AsyncGPUReadback.Request(dst, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                var sw = Stopwatch.StartNew();
                Graphics.ExecuteCommandBuffer(cmd);
                AsyncGPUReadback.Request(dst, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds / N);
                cmd.Release(); warm.Release();
            }
            csv.AppendLine($"{tag},bench4k,-,ADAPTIVE_luma_strength0,ms_per_pass,{best:F5}");
        }

        Shader.globalRenderPipeline = prevRp;
        Object.DestroyImmediate(mat);
        srcH.Release(); dstH.Release();
        log.AppendLine("bench done");
    }
}
