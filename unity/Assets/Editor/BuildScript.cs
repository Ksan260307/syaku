using System;
using System.IO;
using Shakutori;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Shakutori.EditorTools
{
    /// <summary>
    /// WebGL ビルド。ローカルでも CI（game-ci/unity-builder）でも同じメソッドを使う。
    ///   Unity -batchmode -quit -projectPath unity -executeMethod Shakutori.EditorTools.BuildScript.BuildWebGL -customBuildPath ../WebGLBuild
    /// </summary>
    public static class BuildScript
    {
        const string DefaultOutput = "../WebGLBuild";

        [MenuItem("Shakutori/Build WebGL", priority = 20)]
        public static void BuildFromMenu()
        {
            var report = Build(DefaultOutput);
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.GetFullPath(DefaultOutput));
        }

        public static void BuildWebGL()
        {
            string output = Arg("-customBuildPath") ?? Arg("-buildPath") ?? DefaultOutput;
            BuildReport report;
            try
            {
                report = Build(output);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static BuildReport Build(string output)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Forest.unity" },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[Build] {s.result}  size={s.totalSize / (1024 * 1024f):0.0}MB  time={s.totalTime}  errors={s.totalErrors}  -> {Path.GetFullPath(output)}");
            return report;
        }
    }

    /// <summary>エディタ上で森を確認するためのメニュー（シーンには保存されない）。</summary>
    public static class WorldPreview
    {
        [MenuItem("Shakutori/Preview World In Scene", priority = 40)]
        public static void Preview()
        {
            var gen = UnityEngine.Object.FindAnyObjectByType<WorldGenerator>();
            if (gen == null)
            {
                Debug.LogWarning("WorldGenerator が見つかりません。Assets/Scenes/Forest.unity を開いてください。");
                return;
            }
            gen.GenerateNow();
            Debug.Log($"[Preview] instances={gen.instanced.InstanceCount}, dewdrops={gen.DewdropPoints.Count}");
        }

        [MenuItem("Shakutori/Clear World Preview", priority = 41)]
        public static void Clear()
        {
            var gen = UnityEngine.Object.FindAnyObjectByType<WorldGenerator>();
            if (gen != null) gen.Clear();
        }
    }
}
