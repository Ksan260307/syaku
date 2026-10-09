using System;
using System.IO;
using System.Linq;
using Shakutori;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Shakutori.EditorTools
{
    /// <summary>
    /// プロジェクトを一発で組み立てるセットアップ。
    /// URP の設定・マテリアル・テクスチャ・音・UI・シーン（Forest.unity）を作る。
    ///   メニュー: Shakutori/Setup Project
    ///   バッチ  : Unity -batchmode -executeMethod Shakutori.EditorTools.ProjectSetup.RunBatch
    /// 何度実行しても同じ結果になる（既存のアセットは GUID を保ったまま更新）。
    /// </summary>
    public static class ProjectSetup
    {
        const string Settings = "Assets/Settings";
        const string Materials = "Assets/Art/Materials";
        const string Textures = "Assets/Art/Textures";
        const string Models = "Assets/Art/Models";
        const string ScenePath = "Assets/Scenes/Forest.unity";

        [MenuItem("Shakutori/Setup Project", priority = 0)]
        public static void Run()
        {
            EnsureFolder(Settings);
            EnsureFolder(Materials);
            EnsureFolder(Textures);
            EnsureFolder("Assets/Scenes");
            SetupLayers();
            SetupPlayerSettings();
            var urp = SetupURP();
            var tex = SetupTextures();
            SetupAudioImport();
            SetupFonts();
            ReimportModels();
            var assets = SetupMaterialsAndAssets(tex);
            var profile = SetupVolumeProfile();
            var panel = SetupPanelSettings();
            BuildScene(assets, profile, panel, tex);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Setup] 完了しました: " + ScenePath);
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T LoadOrCreate<T>(string path, Func<T> create) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = create();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ------------------------------------------------------------------
        static void SetupLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            layers.GetArrayElementAtIndex(ShakuConst.SurfaceLayer).stringValue = "Surface";
            layers.GetArrayElementAtIndex(ShakuConst.PlayerLayer).stringValue = "Player";
            layers.GetArrayElementAtIndex(ShakuConst.CreatureLayer).stringValue = "Creature";
            layers.GetArrayElementAtIndex(ShakuConst.RollingLayer).stringValue = "Rolling";
            layers.GetArrayElementAtIndex(ShakuConst.WormBodyLayer).stringValue = "WormBody";
            tm.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "Ksan";
            PlayerSettings.productName = "Shakutori no Mori";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:Shakutori";
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);

            // 新しい Input System のみを使う
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = ps.FindProperty("activeInputHandler");
            if (input != null) input.intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        static UniversalRenderPipelineAsset SetupURP()
        {
            var postData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            var rd = LoadOrCreate($"{Settings}/ForestRenderer.asset", () =>
            {
                var r = ScriptableObject.CreateInstance<UniversalRendererData>();
                r.postProcessData = postData;
                return r;
            });
            if (rd.postProcessData == null) rd.postProcessData = postData;
            rd.renderingMode = RenderingMode.Forward;
            EditorUtility.SetDirty(rd);

            var urp = LoadOrCreate($"{Settings}/ForestURP.asset", () => UniversalRenderPipelineAsset.Create(rd));
            var so = new SerializedObject(urp);
            void B(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; else Debug.LogWarning("URP field not found: " + n); }
            void I(string n, int v) { var p = so.FindProperty(n); if (p != null) { if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v; else p.intValue = v; } else Debug.LogWarning("URP field not found: " + n); }
            void F(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; else Debug.LogWarning("URP field not found: " + n); }
            B("m_RequireDepthTexture", true);
            B("m_RequireOpaqueTexture", true);
            I("m_OpaqueDownsampling", 1);
            B("m_SupportsHDR", true);
            so.FindProperty("m_MSAA").intValue = 2;
            F("m_RenderScale", 1f);
            B("m_MainLightShadowsSupported", true);
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0;
            B("m_AdditionalLightShadowsSupported", false);
            F("m_ShadowDistance", 48f);
            so.FindProperty("m_ShadowCascadeCount").intValue = 2;
            F("m_Cascade2Split", 0.28f);
            F("m_ShadowDepthBias", 1.0f);
            F("m_ShadowNormalBias", 0.8f);
            B("m_SoftShadowsSupported", true);
            B("m_SupportsLightCookies", true);
            B("m_UseSRPBatcher", true);
            B("m_SupportsDynamicBatching", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
            return urp;
        }

        // ------------------------------------------------------------------
        public struct Tex
        {
            public Texture2D detail;
            public Texture2D cookie;
        }

        static float TileNoise(float u, float v, float scale, float ox, float oy)
        {
            float a = Mathf.PerlinNoise(u * scale + ox, v * scale + oy);
            float b = Mathf.PerlinNoise((u - 1f) * scale + ox, v * scale + oy);
            float c = Mathf.PerlinNoise(u * scale + ox, (v - 1f) * scale + oy);
            float d = Mathf.PerlinNoise((u - 1f) * scale + ox, (v - 1f) * scale + oy);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static float TileFbm(float u, float v, float scale, int oct, float seed)
        {
            float s = 0, amp = 1, norm = 0;
            for (int i = 0; i < oct; i++)
            {
                s += TileNoise(u, v, scale, seed + i * 7.1f, seed * 1.3f + i * 3.7f) * amp;
                norm += amp;
                amp *= 0.5f;
                scale *= 2f;
            }
            return s / norm;
        }

        static Texture2D WriteTexture(string path, int size, Func<float, float, Color> fn, bool srgb, TextureWrapMode wrap)
        {
            if (!File.Exists(path))
            {
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = fn(x / (float)size, y / (float)size);
                t.SetPixels(px);
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Tex SetupTextures()
        {
            var tex = new Tex();
            tex.detail = WriteTexture($"{Textures}/DetailNoise.png", 256, (u, v) =>
            {
                float a = TileFbm(u, v, 8f, 4, 1.7f);
                float b = TileFbm(u, v, 4f, 3, 9.3f);
                return new Color(a, b, 0.5f, 1f);
            }, false, TextureWrapMode.Repeat);
            tex.cookie = WriteTexture($"{Textures}/SunCookie.png", 256, (u, v) =>
            {
                float n = TileFbm(u, v, 3f, 4, 4.2f);
                float leaf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.47f, 0.58f, n));
                float val = Mathf.Lerp(1f, 0.22f, leaf);
                return new Color(val, val, val, 1f);
            }, false, TextureWrapMode.Repeat);

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/UI/Icons", "Assets/UI/Creatures" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return tex;
        }

        static void SetupAudioImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (AudioImporter)AssetImporter.GetAtPath(p);
                var s = imp.defaultSampleSettings;
                bool longClip = p.Contains("music") || p.Contains("ambience") || p.Contains("loop_");
                s.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = longClip ? 0.45f : 0.6f;
                imp.defaultSampleSettings = s;
                imp.loadInBackground = longClip;
                imp.SaveAndReimport();
            }
        }

        static void SetupFonts()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets/UI/Fonts" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is TrueTypeFontImporter imp)
                {
                    imp.fontTextureCase = FontTextureCase.Dynamic;
                    imp.includeFontData = true;
                    imp.SaveAndReimport();
                }
            }
        }

        static void ReimportModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }

        // ------------------------------------------------------------------
        static Material Mat(string name, string shader, Action<Material> setup)
        {
            string path = $"{Materials}/{name}.mat";
            var sh = Shader.Find(shader);
            if (sh == null) throw new Exception("Shader not found: " + shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            setup(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Keyword(Material m, string kw, string prop, bool on)
        {
            m.SetFloat(prop, on ? 1f : 0f);
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        static WorldAssets SetupMaterialsAndAssets(Tex tex)
        {
            var assets = LoadOrCreate($"{Settings}/WorldAssets.asset", () => ScriptableObject.CreateInstance<WorldAssets>());
            assets.prop = Mat("M_Prop", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.45f);
                m.SetFloat("_RimStrength", 0.3f);
                m.SetFloat("_SpecularStrength", 0f);
            });
            assets.propGlossy = Mat("M_PropGlossy", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.45f);
                m.SetFloat("_RimStrength", 0.35f);
                m.SetFloat("_SpecularStrength", 0.7f);
                m.SetFloat("_SpecularSize", 0.035f);
            });
            assets.bark = Mat("M_Bark", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.22f);
                m.SetFloat("_RimStrength", 0.22f);
                m.SetFloat("_ShadowBrightness", 1.05f);
            });
            assets.glow = Mat("M_GlowMushroom", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.35f);
                m.SetColor("_EmissionColor", new Color(0.25f, 0.9f, 1.3f) * 1.4f);
                m.SetFloat("_RimStrength", 0.6f);
                m.SetColor("_RimColor", new Color(0.7f, 1f, 1f));
                m.SetFloat("_ShadowBrightness", 1.25f);
            });
            assets.terrain = Mat("M_Terrain", "Shakutori/ToonLit", m =>
            {
                Keyword(m, "_TERRAIN", "_Terrain", true);
                m.SetTexture("_DetailMap", tex.detail);
                m.SetFloat("_DetailScale", 0.11f);
                m.SetFloat("_DetailStrength", 0.32f);
                m.SetFloat("_RimStrength", 0.05f);
                m.SetFloat("_OutlineWidth", 0f);
                m.SetFloat("_ShadowSoftness", 0.12f);
                m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            });
            assets.foliage = Mat("M_Grass", "Shakutori/ToonFoliage", m =>
            {
                m.SetFloat("_WindStrength", 0.22f);
                m.SetFloat("_NearFadeDistance", 1.1f);
                m.SetFloat("_FarFadeStart", 38f);
                m.SetFloat("_FarFadeEnd", 52f);
                m.SetFloat("_NormalUp", 0.6f);
            });
            assets.flowers = Mat("M_Flowers", "Shakutori/ToonFoliage", m =>
            {
                m.SetFloat("_WindStrength", 0.45f);
                m.SetFloat("_NearFadeDistance", 1.6f);
                m.SetFloat("_FarFadeStart", 150f);
                m.SetFloat("_FarFadeEnd", 190f);
                m.SetFloat("_NormalUp", 0.35f);
                m.SetFloat("_Variation", 0.08f);
            });
            assets.water = Mat("M_Water", "Shakutori/ToonWater", m => { });
            assets.lightShaft = Mat("M_LightShaft", "Shakutori/LightShaft", m => { m.SetFloat("_Intensity", 0.22f); });
            assets.dewdrop = Mat("M_Dewdrop", "Shakutori/Dewdrop", m => { });
            assets.particle = Mat("M_Particle", "Shakutori/ParticleGlow", m => { m.SetFloat("_Shape", 0f); });
            assets.silk = Mat("M_Silk", "Shakutori/ParticleGlow", m =>
            {
                m.SetFloat("_Shape", 1f);
                m.SetColor("_TintColor", new Color(1.4f, 1.4f, 1.3f, 0.9f));
            });
            assets.worm = Mat("M_Inchworm", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.55f);
                m.SetFloat("_OutlineSmoothNormals", 0f);
                m.SetColor("_OutlineColor", new Color(0.3f, 0.36f, 0.22f));
                m.SetFloat("_RimStrength", 0.5f);
                m.SetFloat("_RimWidth", 0.32f);
                m.SetFloat("_SpecularStrength", 0.45f);
                m.SetFloat("_SpecularSize", 0.045f);
                m.SetColor("_ShadowTint", new Color(0.7f, 0.78f, 0.95f));
                m.SetFloat("_ShadowBrightness", 1.1f);
                Keyword(m, "_HUE_SHIFT", "_HueShiftOn", true);   // きせかえ
                m.SetFloat("_HueShift", 0f);
                m.SetFloat("_SatMul", 1f);
                m.SetFloat("_ValMul", 1f);
            });
            // 川：深い所は深い青緑、浅い所は川底が緑がかって見える。泡は岸ぎわ・石のまわり・滝の下だけ
            // 森の水たまり：川と同じ水の描き方で、流れはほとんどなく、空がよく映る（鏡の水たまり）
            assets.pond = Mat("M_Pond", "Shakutori/ToonRiver", m =>
            {
                m.SetColor("_ShallowColor", new Color(0.46f, 0.72f, 0.56f, 1f));
                m.SetColor("_DeepColor", new Color(0.05f, 0.24f, 0.26f, 1f));
                m.SetVector("_Absorb", new Vector4(2.2f, 0.95f, 0.7f, 0f));
                m.SetFloat("_Clarity", 1.6f);
                m.SetColor("_ReflectColor", new Color(0.62f, 0.78f, 0.86f, 1f));
                // 低い所から見ると、まわりの木々や草が映る（空の色だけだと、白っぽい円盤に見える）
                m.SetColor("_HorizonReflect", new Color(0.24f, 0.38f, 0.34f, 1f));
                m.SetFloat("_FresnelStrength", 0.55f);
                m.SetColor("_FoamColor", new Color(0.93f, 0.98f, 1f, 1f));
                m.SetFloat("_FoamDepth", 0.1f);
                m.SetColor("_StreakColor", new Color(0.72f, 0.9f, 0.9f, 1f));
                m.SetFloat("_FlowSpeed", 0.06f);
                m.SetFloat("_StreakScale", 6f);
                m.SetFloat("_StreakStrength", 0f);
                m.SetFloat("_RippleStrength", 0.7f);
                m.SetFloat("_Refraction", 0.02f);
                m.SetFloat("_SpecStrength", 1.8f);
                m.SetFloat("_Glint", 0.9f);
            });
            assets.river = Mat("M_River", "Shakutori/ToonRiver", m =>
            {
                m.SetColor("_ShallowColor", new Color(0.42f, 0.70f, 0.58f, 1f));
                m.SetColor("_DeepColor", new Color(0.05f, 0.26f, 0.30f, 1f));
                m.SetVector("_Absorb", new Vector4(2.4f, 1.0f, 0.75f, 0f));
                m.SetFloat("_Clarity", 1.8f);
                m.SetColor("_ReflectColor", new Color(0.58f, 0.74f, 0.82f, 1f));
                m.SetFloat("_FresnelStrength", 0.38f);
                m.SetColor("_FoamColor", new Color(0.93f, 0.98f, 1f, 1f));
                m.SetFloat("_FoamDepth", 0.12f);
                m.SetColor("_StreakColor", new Color(0.72f, 0.9f, 0.9f, 1f));
                m.SetFloat("_FlowSpeed", 0.55f);
                m.SetFloat("_StreakScale", 6f);
                m.SetFloat("_RippleStrength", 0.6f);
                m.SetFloat("_Refraction", 0.025f);
                m.SetFloat("_SpecStrength", 1.6f);
            });
            // 滝：まっ白ではなく、水の色の中に白い筋。滝つぼは白くあわ立つ
            assets.waterfall = Mat("M_Waterfall", "Shakutori/Waterfall", m =>
            {
                m.SetColor("_Color", new Color(0.36f, 0.62f, 0.66f, 0.82f));
                m.SetColor("_FoamColor", new Color(0.94f, 0.98f, 1f, 1f));
                m.SetFloat("_Speed", 1.6f);
                m.SetFloat("_StreakScale", 11f);
            });
            assets.creature = Mat("M_Creature", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0.4f);
                m.SetFloat("_OutlineSmoothNormals", 0f);
                m.SetFloat("_RimStrength", 0.45f);
                m.SetFloat("_SpecularStrength", 0.5f);
                m.SetFloat("_SpecularSize", 0.04f);
                m.SetFloat("_ShadowBrightness", 1.1f);
            });
            assets.creatureWing = Mat("M_CreatureWing", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_Cull", 0f);   // 羽は両面
                m.SetFloat("_OutlineWidth", 0f);
                m.SetFloat("_RimStrength", 0.6f);
                m.SetFloat("_ShadowBrightness", 1.2f);
                m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            });
            assets.creatureGlow = Mat("M_CreatureGlow", "Shakutori/ToonLit", m =>
            {
                m.SetFloat("_OutlineWidth", 0f);
                m.SetColor("_EmissionColor", new Color(1.2f, 1.5f, 0.35f) * 2.2f);
                m.SetFloat("_RimStrength", 0f);
                m.SetFloat("_ShadowBrightness", 1.4f);
                m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            });
            Mat("M_Sky", "Shakutori/ForestSky", m => { });
            assets.sunCookie = tex.cookie;

            assets.meshes.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var mesh = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().FirstOrDefault();
                if (mesh == null) continue;
                assets.meshes.Add(new WorldAssets.NamedMesh { name = Path.GetFileNameWithoutExtension(p), mesh = mesh });
                var b = mesh.bounds;
                Debug.Log($"[Setup] mesh {Path.GetFileNameWithoutExtension(p)} verts={mesh.vertexCount} bounds center={b.center} size={b.size}");
            }
            // いきもの図鑑の絵（Blender で描き出したもの）
            assets.portraits.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/UI/Creatures" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) assets.portraits.Add(new WorldAssets.NamedTexture { name = Path.GetFileNameWithoutExtension(p), texture = t });
            }
            EditorUtility.SetDirty(assets);
            return assets;
        }

        // ------------------------------------------------------------------
        static VolumeProfile SetupVolumeProfile()
        {
            string path = $"{Settings}/ForestPost.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.96f, 0.88f));

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(8f);
            ca.saturation.Override(14f);

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(5f);
            wb.tint.Override(2f);

            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(0.98f, 1.0f, 1.04f, 0.02f));
            lgg.gain.Override(new Vector4(1.03f, 1.0f, 0.97f, 0.0f));

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.55f);
            vig.color.Override(new Color(0.1f, 0.18f, 0.12f));

            foreach (var c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static PanelSettings SetupPanelSettings()
        {
            string tssPath = "Assets/UI/ShakutoriTheme.tss";
            if (!File.Exists(tssPath))
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(tssPath);
            }
            var panel = LoadOrCreate("Assets/UI/GamePanel.asset", () => ScriptableObject.CreateInstance<PanelSettings>());
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 0;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        // ------------------------------------------------------------------
        static void BuildScene(WorldAssets assets, VolumeProfile profile, PanelSettings panel, Tex tex)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 太陽
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.cookie = tex.cookie;
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            var ald = sun.GetUniversalAdditionalLightData();
            ald.lightCookieSize = new Vector2(34f, 34f);

            // 空と環境光・霧
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/M_Sky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.8f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.7f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.33f, 0.38f, 0.27f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.8f, 0.76f);
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 165f;

            // カメラ
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 650f;
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<FollowCamera>();
            camGo.transform.position = new Vector3(0f, 3f, -6f);

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // しゃくとりむし
            var wormGo = new GameObject("Inchworm");
            var ctrl = wormGo.AddComponent<InchwormController>();
            var bodyGo = new GameObject("InchwormBody");
            bodyGo.layer = ShakuConst.PlayerLayer;
            bodyGo.AddComponent<MeshFilter>().sharedMesh = assets.Get("Inchworm");
            var bodyR = bodyGo.AddComponent<MeshRenderer>();
            bodyR.sharedMaterial = assets.worm;
            var body = bodyGo.AddComponent<InchwormBody>();
            var silkGo = new GameObject("Silk");
            silkGo.transform.SetParent(wormGo.transform, false);
            var lr = silkGo.AddComponent<LineRenderer>();
            lr.sharedMaterial = assets.silk;
            lr.widthMultiplier = 0.025f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            ctrl.body = body;
            ctrl.silk = lr;
            ctrl.cameraTransform = camGo.transform;
            follow.target = ctrl;

            // ゲームのしくみ
            var game = new GameObject("Game");
            game.AddComponent<GameInput>();
            var world = game.AddComponent<WorldGenerator>();
            var inst = game.AddComponent<InstancedRenderer>();
            world.assets = assets;
            world.instanced = inst;
            world.sun = sun;
            var col = game.AddComponent<Collectibles>();
            col.assets = assets;
            var fx = game.AddComponent<AmbientFX>();
            fx.particleMaterial = assets.particle;
            var audio = game.AddComponent<AudioManager>();
            AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{n}.wav");
            audio.music = Clip("music_forest");
            audio.musicRiver = Clip("music_river");
            audio.musicPark = Clip("music_park");
            audio.ambience = Clip("ambience_forest");
            audio.steps = new[] { Clip("step_1"), Clip("step_2"), Clip("step_3") };
            audio.collect = Clip("collect");
            audio.discover = Clip("discover");
            audio.click = Clip("click");
            audio.silk = Clip("silk");
            audio.land = Clip("land");
            audio.complete = Clip("complete");
            audio.riverAmbience = Clip("ambience_river");
            audio.creature = Clip("creature");
            audio.travel = Clip("travel");
            audio.unlock = Clip("unlock");
            audio.caw = Clip("caw");
            audio.fall = Clip("fall");
            audio.splash = Clip("splash");
            audio.rare = Clip("rare");
            audio.chirp = Clip("chirp");
            audio.croak = Clip("croak");
            audio.parkAmbience = Clip("ambience_park");
            audio.musicMountain = Clip("music_mountain");
            audio.mountainAmbience = Clip("ambience_mountain");
            audio.areaClips = new[]
            {
                Clip("loop_waterfall"), Clip("loop_shallows"), Clip("loop_frogs"), Clip("loop_cave_drip"), Clip("loop_canopy"),
                Clip("woodpecker"), Clip("knock_acorn"), Clip("puff"), Clip("creak"), Clip("clunk"), Clip("plink"), Clip("bell"),
                Clip("fish_jump"), Clip("ting"), Clip("sand_step"), Clip("fall"),
                Clip("loop_spring"), Clip("loop_ridge_wind"), Clip("higurashi"), Clip("pika"),
            };
            var creatures = game.AddComponent<Creatures>();
            creatures.assets = assets;

            // UI
            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameUI.uxml");
            var gui = uiGo.AddComponent<GameUI>();
            gui.bodyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/ShakutoriSans-Bold.ttf");
            gui.titleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/ShakutoriSans-Black.ttf");

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            var gm = game.AddComponent<GameManager>();
            gm.world = world;
            gm.worm = ctrl;
            gm.followCamera = follow;
            gm.ui = gui;
            gm.collectibles = col;
            gm.creatures = creatures;
            gm.fx = fx;
            gm.postVolume = vol;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
