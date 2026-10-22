using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：面に合わせた体の使い方：壁に頭をつけてひと呼吸・ぬれた所ですべりかける・坂で頭を水平に・
    /// 苔の上で沈む・頭の所でも草をかき分ける・砂の上の足あと。
    /// </summary>
    public class WormSurfaceTests
    {
        readonly List<GameObject> _temp = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            ResetInput();
            InchwormController.SlipChanceOverride = -1f;
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
        }

        GameObject Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestBox";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        Vector3 SkyTop => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.5f, 0f);

        [UnityTest, Timeout(120000)]
        public IEnumerator FloorToWall_TouchesTheWallAndPauses()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 2f), new Vector3(4f, 0.4f, 8f), Quaternion.identity);
            Box(top + new Vector3(0f, 1.5f, 3.2f), new Vector3(4f, 3.4f, 0.4f), Quaternion.identity);
            Worm.Spawn(top, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
            bool paused = false;
            yield return Hold(Vector2.up, 8f, () => paused |= Worm.IsWallPause);
            Assert.IsTrue(paused, "壁に頭をつけて、体を起こしてひと呼吸");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WetFooting_SometimesSlips()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 6f), new Vector3(4f, 0.4f, 16f), Quaternion.identity);
            Worm.Spawn(top, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
            InchwormController.SlipChanceOverride = 1f;
            Worm.DebugSetWet(20f);
            int before = Worm.SlipCount;
            bool slipping = false;
            yield return Hold(Vector2.up, 4f, () => slipping |= Worm.IsSlipping);
            Assert.Greater(Worm.SlipCount, before, "ぬれていると、足をすべらせかける");
            Assert.IsTrue(slipping);
            // かわいた所では、すべらない
            InchwormController.SlipChanceOverride = -1f;
            Worm.DebugSetWet(0f);
            before = Worm.SlipCount;
            yield return Hold(Vector2.up, 2.5f);
            Assert.AreEqual(before, Worm.SlipCount, "かわいた所では、すべらない");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator OnASlope_KeepsTheHeadLevel()
        {
            Vector3 top = SkyTop;
            var rot = Quaternion.Euler(-20f, 0f, 0f);   // 奥（+Z）へ上る坂
            var go = Box(top + new Vector3(0f, -0.2f, 0f), new Vector3(4f, 0.4f, 8f), rot);
            Vector3 on = go.transform.position + rot * new Vector3(0f, 0.2f, 0f);
            Worm.Spawn(on, rot * Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(3f);
            Assert.Less(Worm.HeadLevelTilt, -0.08f, "上り坂では、頭を少し下げて水平に");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Moss_SinksALittle_AndTheHeadPartsTheGrass()
        {
            Place(ForestLayout.MossHill + new Vector2(2f, 0f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(0.8f);
            Vector4 head = Vector4.zero;
            yield return Hold(Vector2.up, 1.5f, () => head = Shader.GetGlobalVector("_ShakuPlayerHead"));
            Assert.Greater(head.w, 0.3f, "伸ばした頭の所でも、草をかき分ける");
            Assert.Less(Vector3.Distance(head, Worm.HeadPosition), 0.5f);
            yield return Seconds(1f);
            Assert.IsTrue(Worm.OnSoftFooting, "苔の上");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Sand_KeepsFootprints()
        {
            SaveSystem.Data.visited.Add("park");
            GM.TravelTo("park");
            yield return WaitUntil(() => Areas.Current == Areas.Park && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, "公園へ");
            Assert.AreEqual(0, GM.Prints.Count, "エリアをかえると、足あとは消える");
            Place(ParkLayout.Sandbox + new Vector2(6f, 3f), Vector3.right);
            yield return FaceCamera(Vector3.right);
            yield return Seconds(0.8f);
            yield return Hold(Vector2.up, 3f);
            Assert.Greater(GM.Prints.Count, 2, "砂の上に、足あとが残る");
        }
    }
}
