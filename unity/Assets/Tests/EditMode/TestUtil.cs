using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>EditMode テスト共通のヘルパー。</summary>
    public static class TestUtil
    {
        public const string WorldAssetsPath = "Assets/Settings/WorldAssets.asset";

        public static WorldAssets LoadWorldAssets()
        {
            var a = AssetDatabase.LoadAssetAtPath<WorldAssets>(WorldAssetsPath);
            Assert.IsNotNull(a, "WorldAssets がありません。Shakutori/Setup Project を実行してください。");
            return a;
        }

        /// <summary>他のテストやシーンの影響を受けない空のシーンを開く。</summary>
        public static void NewEmptyScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static GameObject Box(Vector3 center, Vector3 size, int layer = ShakuConst.SurfaceLayer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = center;
            go.transform.localScale = size;
            go.layer = layer;
            Physics.SyncTransforms();
            return go;
        }

        public static float ArcLength(BodyCurve c)
        {
            float s = 0f;
            for (int i = 1; i < c.Count; i++) s += Vector3.Distance(c.pos[i - 1], c.pos[i]);
            return s;
        }

        public static void AssertVector(Vector3 expected, Vector3 actual, float tol, string msg = "")
        {
            Assert.LessOrEqual(Vector3.Distance(expected, actual), tol, $"{msg} expected {expected} but was {actual}");
        }
    }
}
