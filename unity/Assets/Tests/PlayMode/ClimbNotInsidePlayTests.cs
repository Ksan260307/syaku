using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 物へ、まわりのいろいろな向きからまっすぐ歩きつづけると、登る（物の中へは入らない）。
    /// 見た目のとおりの当たり判定では、根もとのえぐれ（下ほど細い玉や筒）の下や、とじていない形（口のあいた取っ手の筒）のせいで、
    /// しゃくとりむしが物の中へ入りこんでいた物：雪だるま・山小屋のバケツと長ぐつ・海辺の浮き玉と岬の大岩・山の山小屋（どれも登りやすい _Col を使う）。
    /// </summary>
    public class ClimbNotInsidePlayTests
    {
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
            Time.captureDeltaTime = 0f;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Snowman_IsClimbedNotWalkedInto() => WalkAtFromAround(Areas.Xmas, "Xmas_Snowman", 5f);

        [UnityTest, Timeout(900000)]
        public IEnumerator HutBucket_IsClimbedNotWalkedInto() => WalkAtFromAround(Areas.Hut, "Hut_Bucket", 5f);

        [UnityTest, Timeout(900000)]
        public IEnumerator HutBoots_AreClimbedNotWalkedInto() => WalkAtFromAround(Areas.Hut, "Hut_Boots", 2f);

        [UnityTest, Timeout(900000)]
        public IEnumerator CoastFloat_IsClimbedNotWalkedInto() => WalkAtFromAround(Areas.Coast, "Coast_Float", 2f);

        // 山の山小屋：土台の石とかべの下の方へは入らず、土台（高さ 0.8）の上の、かべを登れる向きがある
        // （前は、丸太のかべは登れなかった。土台のまわりの石の足場で止まる向きもある。かべの上の、のきの屋根のうらとの角では、休むと頭が少しふれる）
        [UnityTest, Timeout(900000)]
        public IEnumerator MountainHut_IsClimbedNotWalkedInto() => WalkAtFromAround(Areas.Mountain, "Mtn_Hut", 1.5f, insideBelow: 2f, climbAll: false);

        // 岬の大岩：海に面していて、陸からは 3 つの向きだけ
        [UnityTest, Timeout(900000)]
        public IEnumerator CoastCape_IsClimbedNotWalkedInto() => WalkAtFromAround(Areas.Coast, "Coast_Cape", 3f, dirs: 8, minTried: 3);

        IEnumerator GoTo(AreaLayout area)
        {
            SaveSystem.Data.visited.Add(area.Id);
            GM.TravelTo(area.Id);
            yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, area.Id + "へ");
            yield return Seconds(0.5f);
        }

        /// <summary>物のまわりの dirs の向きから、物のまん中へ 20 秒歩く。どの向きでも中へ入らず、半分より多くの向き（climbAll でなければ、どれかの向き）で
        /// climb より高く登る。insideBelow：地面からこの高さより下の体の点だけを見る。</summary>
        IEnumerator WalkAtFromAround(AreaLayout area, string mesh, float climb, float insideBelow = float.PositiveInfinity, int dirs = 6, int minTried = 4,
                                     bool climbAll = true)
        {
            yield return GoTo(area);
            MeshCollider Find() => Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c => c.name == mesh).OrderBy(c => c.bounds.center.magnitude).First();
            var col = Find();
            Assert.AreEqual(mesh + "_Col", col.sharedMesh.name, "登りやすい当たり判定");
            Vector3 cen = col.bounds.center;
            float rad = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
            int tried = 0, climbed = 0;
            string ups = "";
            Time.captureDeltaTime = 1f / 30f;
            for (int k = 0; k < dirs; k++)
            {
                float a = (k + 0.25f) / dirs * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 s = cen + dir * (rad + 2.5f);
                // 出発点：地面の上で、水の中でも、ほかの物のそばでもない所
                if (!Physics.Raycast(new Vector3(s.x, area.Height(s.x, s.z) + 3f, s.z), Vector3.down, out var g, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (g.collider == col || area.IsUnderwater(g.point + Vector3.up * 0.05f) || !area.InPlayArea(g.point)) continue;
                if (Physics.CheckSphere(g.point + Vector3.up * 0.6f, 0.45f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                Worm.Spawn(g.point + Vector3.up * 0.05f, -dir);
                yield return FaceCamera(-dir);
                float insideSecs = 0f, maxUp = 0f;
                bool insideNow = false;
                yield return Hold(Vector2.up, 20f, () =>
                {
                    if (col == null) return;   // とびらから、ほかのエリアへ行った
                    maxUp = Mathf.Max(maxUp, Worm.CenterPosition.y - area.Height(Worm.CenterPosition.x, Worm.CenterPosition.z));
                    insideNow = false;
                    foreach (var p in new[] { Worm.CenterPosition, Worm.HeadPosition, Worm.TailPoint })
                        if (p.y - area.Height(p.x, p.z) < insideBelow && Inside(col, p)) insideNow = true;
                    if (insideNow) insideSecs += Time.deltaTime;
                });
                ResetInput();
                if (Areas.Current != area || col == null)
                {
                    // 山小屋のとびらなどから、ほかのエリアへ行った：この向きは数えない
                    yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, "着く");
                    yield return GoTo(area);
                    col = Find();
                    continue;
                }
                tried++;
                // 物をまたぐ一瞬の、少しのめりこみではなく、中にとどまる（合わせて 1 秒より長い、または最後に中にいる）
                Assert.IsFalse(insideSecs > 1f || insideNow, $"{mesh} 向き {k}：物の中へ入らない（中にいた時間 {insideSecs:F1} 秒）");
                if (maxUp > climb) climbed++;
                ups += $" {maxUp:F1}";
            }
            Time.captureDeltaTime = 0f;
            Assert.GreaterOrEqual(tried, minTried, $"{mesh}：まわりから歩きだせる");
            if (climbAll) Assert.Greater(climbed * 2, tried, $"{mesh}：半分より多くの向きから、{climb} より高く登る（{climbed}/{tried}：{ups}）");
            else Assert.Greater(climbed, 0, $"{mesh}：どれかの向きから、{climb} より高く登る（{ups}）");
        }

        static readonly Vector3[] Dirs =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
            new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized, new Vector3(1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized,
        };

        /// <summary>
        /// p が物の中か：少し上と少し下の点が、どちらも中（平らな面の上にのった点は、横へのレイが面の中を走って、中に見えることがある）。
        /// </summary>
        static bool Inside(Collider c, Vector3 p) => InsideAt(c, p + Vector3.up * 0.02f) && InsideAt(c, p - Vector3.up * 0.02f);

        /// <summary>
        /// 上下・横・ななめの 10 の向きへのレイが、どれもその物の面に当たり、8 つ以上が面のうら（内がわ）に当たる
        /// （面の上にのった点は、その面にきょり 0 で当たるので、出発点を向きの方へ少しずらす）。
        /// </summary>
        static bool InsideAt(Collider c, Vector3 p)
        {
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                int back = 0;
                foreach (var d in Dirs)
                {
                    if (!c.Raycast(new Ray(p + d * 0.04f, d), out var h, 60f)) return false;
                    if (Vector3.Dot(h.normal, d) > 0f) back++;
                }
                return back >= 8;
            }
            finally { Physics.queriesHitBackfaces = prev; }
        }
    }
}
