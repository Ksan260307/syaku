using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Shakutori.Tests
{
    /// <summary>セットアップで作られるプロジェクト設定・シーン・アセットの検証。</summary>
    public class ProjectConfigTests
    {
        [Test]
        public void Layers_AreNamed()
        {
            Assert.AreEqual("Surface", LayerMask.LayerToName(ShakuConst.SurfaceLayer));
            Assert.AreEqual("Player", LayerMask.LayerToName(ShakuConst.PlayerLayer));
            Assert.AreEqual("Water", LayerMask.LayerToName(ShakuConst.WaterLayer));
        }

        [Test]
        public void URP_IsConfiguredForToonRendering()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(urp, "URP が既定のレンダーパイプライン");
            Assert.IsTrue(urp.supportsCameraDepthTexture, "水の深さ表現に深度テクスチャが必要");
            Assert.IsTrue(urp.supportsCameraOpaqueTexture, "水の屈折に不透明テクスチャが必要");
            Assert.IsTrue(urp.supportsHDR, "ブルームのため HDR");
            Assert.IsTrue(urp.supportsMainLightShadows);
            Assert.IsTrue(urp.useSRPBatcher);
            Assert.Greater(urp.shadowDistance, 20f);
        }

        [Test]
        public void PlayerSettings_AreReadyForGitHubPages()
        {
            Assert.AreEqual(WebGLCompressionFormat.Gzip, PlayerSettings.WebGL.compressionFormat);
            Assert.IsTrue(PlayerSettings.WebGL.decompressionFallback, "GitHub Pages は Content-Encoding を付けないので展開フォールバックが必要");
            Assert.AreEqual("PROJECT:Shakutori", PlayerSettings.WebGL.template);
            Assert.AreEqual(ColorSpace.Linear, PlayerSettings.colorSpace);
            Assert.IsTrue(File.Exists("Assets/WebGLTemplates/Shakutori/index.html"));
            Assert.IsTrue(File.Exists("Assets/WebGLTemplates/Shakutori/icon.png"));
        }

        [Test]
        public void WebGLTemplate_UsesUnityPlaceholders()
        {
            string html = File.ReadAllText("Assets/WebGLTemplates/Shakutori/index.html");
            StringAssert.Contains("{{{ LOADER_FILENAME }}}", html);
            StringAssert.Contains("{{{ DATA_FILENAME }}}", html);
            StringAssert.Contains("{{{ FRAMEWORK_FILENAME }}}", html);
            StringAssert.Contains("createUnityInstance", html);
        }

        [Test]
        public void Scene_IsInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.IsTrue(scenes.Any(s => s.enabled && s.path == "Assets/Scenes/Forest.unity"));
        }

        [Test]
        public void Scene_HasAllSystemsWired()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Forest.unity", OpenSceneMode.Single);
            var gm = Object.FindAnyObjectByType<GameManager>();
            Assert.IsNotNull(gm);
            Assert.IsNotNull(gm.world);
            Assert.IsNotNull(gm.worm);
            Assert.IsNotNull(gm.followCamera);
            Assert.IsNotNull(gm.ui);
            Assert.IsNotNull(gm.collectibles);
            Assert.IsNotNull(gm.fx);
            Assert.IsNotNull(gm.creatures, "いきもの");
            Assert.IsNotNull(gm.creatures.assets);
            Assert.IsNotNull(gm.postVolume);
            Assert.IsNotNull(gm.postVolume.sharedProfile);

            Assert.IsNotNull(gm.world.assets);
            Assert.IsNotNull(gm.world.instanced);
            Assert.IsNotNull(gm.world.sun);
            Assert.IsNotNull(gm.world.sun.cookie, "木漏れ日のクッキー");

            var worm = gm.worm;
            Assert.IsNotNull(worm.body);
            Assert.IsNotNull(worm.silk);
            Assert.IsNotNull(worm.cameraTransform);
            Assert.AreEqual(ShakuConst.PlayerLayer, worm.body.gameObject.layer);
            Assert.AreSame(worm, gm.followCamera.target);

            Assert.IsNotNull(Object.FindAnyObjectByType<GameInput>());
            Assert.IsNotNull(Object.FindAnyObjectByType<AudioManager>());
            Assert.IsNotNull(Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>());
            var doc = gm.ui.GetComponent<UIDocument>();
            Assert.IsNotNull(doc.panelSettings);
            Assert.IsNotNull(doc.visualTreeAsset);
            Assert.IsNotNull(gm.ui.bodyFont);
            Assert.IsNotNull(gm.ui.titleFont);
            Assert.IsNotNull(RenderSettings.skybox);
            Assert.IsTrue(RenderSettings.fog);
            Assert.IsNotNull(Camera.main);
            Assert.Less(Camera.main.nearClipPlane, 0.1f, "小さな世界なのでニアクリップは小さく");
        }

        [Test]
        public void Audio_AllClipsAssigned()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Forest.unity", OpenSceneMode.Single);
            var a = Object.FindAnyObjectByType<AudioManager>();
            Assert.IsNotNull(a.music);
            Assert.IsNotNull(a.ambience);
            Assert.AreEqual(3, a.steps.Length);
            foreach (var s in a.steps) Assert.IsNotNull(s);
            Assert.IsNotNull(a.collect);
            Assert.IsNotNull(a.discover);
            Assert.IsNotNull(a.click);
            Assert.IsNotNull(a.silk);
            Assert.IsNotNull(a.land);
            Assert.IsNotNull(a.complete);
            Assert.Greater(a.music.length, 30f);
            Assert.Greater(a.ambience.length, 20f);
            Assert.IsNotNull(a.riverAmbience);
            Assert.Greater(a.riverAmbience.length, 20f);
            Assert.IsNotNull(a.creature);
            Assert.IsNotNull(a.travel);
            Assert.IsNotNull(a.unlock);
            Assert.IsNotNull(a.caw);
        }

        [TestCase("M_Prop", "Shakutori/ToonLit")]
        [TestCase("M_PropGlossy", "Shakutori/ToonLit")]
        [TestCase("M_Bark", "Shakutori/ToonLit")]
        [TestCase("M_GlowMushroom", "Shakutori/ToonLit")]
        [TestCase("M_Terrain", "Shakutori/ToonLit")]
        [TestCase("M_Inchworm", "Shakutori/ToonLit")]
        [TestCase("M_Grass", "Shakutori/ToonFoliage")]
        [TestCase("M_Flowers", "Shakutori/ToonFoliage")]
        [TestCase("M_Water", "Shakutori/ToonWater")]
        [TestCase("M_Sky", "Shakutori/ForestSky")]
        [TestCase("M_LightShaft", "Shakutori/LightShaft")]
        [TestCase("M_Dewdrop", "Shakutori/Dewdrop")]
        [TestCase("M_Particle", "Shakutori/ParticleGlow")]
        [TestCase("M_Silk", "Shakutori/ParticleGlow")]
        public void Materials_UseExpectedShaders(string mat, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/{mat}.mat");
            Assert.IsNotNull(m, mat);
            Assert.AreEqual(shader, m.shader.name);
            Assert.IsTrue(m.enableInstancing, "インスタンシング描画に対応");
        }

        [Test]
        public void Terrain_HasDetailAndNoOutline()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Terrain.mat");
            Assert.IsTrue(m.IsKeywordEnabled("_TERRAIN"));
            Assert.IsFalse(m.GetShaderPassEnabled("SRPDefaultUnlit"), "地面に輪郭線は不要");
            Assert.IsNotNull(m.GetTexture("_DetailMap"));
        }

        [Test]
        public void Inchworm_OutlineDoesNotUseDeformUVs()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Inchworm.mat");
            Assert.AreEqual(0f, m.GetFloat("_OutlineSmoothNormals"), "UV3 は変形用データなので輪郭に使わない");
        }

        [TestCase("Shakutori/ToonLit")]
        [TestCase("Shakutori/ToonFoliage")]
        [TestCase("Shakutori/ToonWater")]
        [TestCase("Shakutori/ForestSky")]
        [TestCase("Shakutori/LightShaft")]
        [TestCase("Shakutori/Dewdrop")]
        [TestCase("Shakutori/ParticleGlow")]
        public void Shaders_CompileWithoutErrors(string name)
        {
            var s = Shader.Find(name);
            Assert.IsNotNull(s, name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(s), $"{name} にコンパイルエラー");
            Assert.IsTrue(s.isSupported, $"{name} がこの環境で使えない");
        }

        [Test]
        public void ToonLit_HasAllPasses()
        {
            var s = Shader.Find("Shakutori/ToonLit");
            var passes = new HashSet<string>();
            var data = ShaderUtil.GetShaderData(s);
            var sub = data.GetSubshader(0);
            for (int i = 0; i < sub.PassCount; i++) passes.Add(sub.GetPass(i).Name);
            CollectionAssert.IsSubsetOf(new[] { "ForwardToon", "Outline", "ShadowCaster", "DepthOnly" }, passes);
        }

        [Test]
        public void PropMeshes_HaveSmoothNormalsForOutline()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var n in new[] { "Rock_A", "Mushroom_Red", "Stump", "Leaf_Oak_Orange" })
            {
                var uv = new List<Vector3>();
                a.Get(n).GetUVs(3, uv);
                Assert.AreEqual(a.Get(n).vertexCount, uv.Count, n);
                foreach (var v in uv) Assert.AreEqual(1f, v.magnitude, 1e-2f, n);
            }
        }

        [Test]
        public void Models_AreYUpAndRightScale()
        {
            var a = TestUtil.LoadWorldAssets();
            var mush = a.Get("Mushroom_Red").bounds;
            Assert.Greater(mush.size.y, 5f, "赤キノコは高さ約7（Y が上）");
            var worm = a.Get("Inchworm").bounds;
            Assert.AreEqual(1f, Mathf.Max(worm.size.x, worm.size.y, worm.size.z), 0.05f, "しゃくとりむしの体長は 1");
            var tree = a.Get("GreatTree").bounds;
            Assert.Greater(tree.size.y, 150f);
        }

        [Test]
        public void Models_HaveVertexColors()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var nm in a.meshes)
                Assert.AreEqual(nm.mesh.vertexCount, nm.mesh.colors32.Length, $"{nm.name} に頂点カラーがない");
        }

        [Test]
        public void UI_ContainsEveryElementTheCodeUses()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameUI.uxml");
            Assert.IsNotNull(vta);
            var root = vta.Instantiate();
            string code = File.ReadAllText("Assets/Scripts/UI/GameUI.cs");
            var names = new HashSet<string>();
            foreach (Match m in Regex.Matches(code, @"Q<\w+>\(""([\w-]+)""\)")) names.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(code, @"(?:Click|HoldButton)\(""([\w-]+)""")) names.Add(m.Groups[1].Value);
            Assert.Greater(names.Count, 30);
            foreach (var n in names)
                Assert.IsNotNull(root.Q(n), $"UXML に '{n}' がありません");
        }

        [Test]
        public void UI_StyleSheetDefinesStateClasses()
        {
            string uss = File.ReadAllText("Assets/UI/GameUI.uss");
            string code = File.ReadAllText("Assets/Scripts/UI/GameUI.cs");
            foreach (Match m in Regex.Matches(code, @"""([a-z]+(?:-[a-z]+)*--[a-z]+)"""))
                StringAssert.Contains("." + m.Groups[1].Value, uss, $"USS に状態クラス {m.Groups[1].Value} がない");
            StringAssert.Contains(".hidden", uss);
            foreach (Match m in Regex.Matches(uss, @"url\(""([^""]+)""\)"))
                Assert.IsTrue(File.Exists(Path.Combine("Assets/UI", m.Groups[1].Value)), $"USS が参照する {m.Groups[1].Value} がない");
        }

        [Test]
        public void Fonts_CoverAllJapaneseText()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/ShakutoriSans-Bold.ttf");
            Assert.IsNotNull(font);
            var text = new System.Text.StringBuilder();
            text.Append(File.ReadAllText("Assets/UI/GameUI.uxml"));
            foreach (var lm in ForestLayout.Landmarks) text.Append(lm.name).Append(lm.description);
            foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"([^\"]*)\""))
                    text.Append(m.Groups[1].Value);
            var missing = new HashSet<char>();
            foreach (char ch in text.ToString())
            {
                if (ch < 0x3000) continue;   // 日本語（かな・漢字・全角記号）だけ確認
                if (!font.HasCharacter(ch)) missing.Add(ch);
            }
            Assert.IsEmpty(missing, "フォントにない文字: " + new string(missing.ToArray()) + "（tools/make_fonts.py を再実行）");
        }
    }
}
