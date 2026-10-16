using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：エリアの改善 300（森）。ホコリタケ・落ちてくるどんぐり・空気と光・場所の音・ただよう物。</summary>
    public class ForestAreaPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator Forest_HasItsOwnAir_Sounds_AndFloatingThings()
        {
            yield return null;
            Assert.AreEqual(AreaAtmosphere.For("forest").fog, RenderSettings.fogColor, "森の霧の色");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_cave_drip"), "ほら穴のしずくの音");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_frogs"), "水たまりのカエル");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_canopy"), "森の葉ずれ");
            foreach (var fx in new[] { "GroveSpores", "MeadowSeeds", "MapleSeeds", "HollowMotes", "Midges", "RootFireflies" })
                Assert.IsTrue(GM.fx.HasEffect(fx), fx);
        }

        [UnityTest]
        public IEnumerator Puffball_PuffsWhenStepped_AndAcornsFallInThePlaza()
        {
            var puffs = GM.world.ExtraSpots("puffball");
            Assert.Greater(puffs.Count, 0);
            Vector3 top = puffs[0];
            int before = AreaProps.Instance.Puffs;
            Worm.Spawn(top + Vector3.up * 0.3f, Vector3.forward);
            yield return WaitUntil(() => AreaProps.Instance.Puffs > before, 3f, "ホコリタケをふむと、ぽふっ");
            Assert.Greater(AreaSounds.Instance.CountOf("puff"), 0, "ぽふっと音");
            // どんぐり広場：ときどき上からどんぐりが落ちてきて、ころんと音がする
            yield return WaitUntil(() => AreaProps.Instance.AcornsDropped > 0, 12f, "どんぐりが落ちてくる");
            yield return WaitUntil(() => AreaSounds.Instance.CountOf("knock_acorn") > 0, 6f, "地面に当たって、ころん");
        }

        [UnityTest]
        public IEnumerator HollowLog_CanBeClimbedFromTheSide_UpTheMossyBerm()
        {
            // 丸太の横腹の下半分はせり出しているので、両がわに苔の土手を盛ってある：まん中・はしの近く・ななめから歩いても、上へ登れる
            bool autoCam = SaveSystem.Settings.autoCamera;
            SaveSystem.Settings.autoCamera = false;
            try
            {
                Vector2 ax = ForestLayout.LogCenter, dir = ForestLayout.LogDir, perp = new Vector2(dir.y, -dir.x);
                foreach (var (side, along, slant) in new[] { (1f, 0f, 0f), (-1f, 0f, 0f), (1f, -12f, 35f), (-1f, 12f, -35f), (-1f, -8f, 30f) })
                {
                    Vector2 onAxis = ax + dir * along;
                    Vector2 outward = (Vector2)(Quaternion.Euler(0f, 0f, slant) * (perp * side));
                    Vector2 from = onAxis + outward * 13f;
                    Vector3 face = new Vector3(-outward.x, 0f, -outward.y);
                    Place(from, face);
                    float best = 0f;
                    bool onLog = false;
                    float t = 0f;
                    while (t < 30f && !onLog)
                    {
                        Vector3 c = Worm.CenterPosition;
                        Vector2 to = onAxis - new Vector2(c.x, c.z);
                        Cam.yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                        Cam.pitch = 22f;
                        GameInput.VirtualMove = Vector2.up;
                        yield return null;
                        t += Time.deltaTime;
                        c = Worm.CenterPosition;
                        best = Mathf.Max(best, c.y - ForestLayout.Height(c.x, c.z));
                        onLog = c.y - ForestLayout.Height(c.x, c.z) > 6.5f
                                && Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, out var hit, 2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                                && hit.collider.name.StartsWith("HollowLog");
                    }
                    ResetInput();
                    Assert.IsTrue(onLog, $"土手を登って、丸太の上へ（{(side > 0 ? "東" : "西")}がわ・まん中から {along}・ななめ {slant} 度。いちばん高く {best:0.0}）");
                }
            }
            finally
            {
                SaveSystem.Settings.autoCamera = autoCam;
            }
        }

        [UnityTest]
        public IEnumerator BigLeaf_CurledRim_StepsOffInsteadOfWrigglingInPlace()
        {
            // 草原の西の大きな葉（ふちが反り返っている）：北へ歩くと、ふちで葉の裏へ回り込もうとして、その場で頭をのばしつづけていた
            Vector2 from = new Vector2(11.0f, 35.3f);
            Assert.IsTrue(Physics.Raycast(new Vector3(from.x, ForestLayout.Height(from.x, from.y) + 3f, from.y), Vector3.down, out var top, 6f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore));
            Assert.IsNotNull(top.collider.GetComponentInParent<BigLeaf>(), "大きな葉の上から歩きだす：" + top.collider.name);
            Worm.Spawn(top.point, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Vector3 start = Worm.CenterPosition;
            yield return Hold(Vector2.up, 5f);
            Vector3 end = Worm.CenterPosition;
            Assert.Greater(end.z - start.z, 2f, $"葉のふちからおりて、先へ進む（{start} → {end}）");
            bool onLeaf = Physics.Raycast(end + Vector3.up * 0.5f, Vector3.down, out var under, 1.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                          && under.collider.GetComponentInParent<BigLeaf>() != null;
            Assert.IsFalse(onLeaf, "葉からおりた");
        }

        [UnityTest]
        public IEnumerator StumpRoot_ClimbsOntoTheRootInsteadOfWrigglingAtItsFoot()
        {
            // 切り株の西の張り出した根（下が少しえぐれている）と切り株の壁のあいだの角へ、北北東へ歩いていく。
            // ・頭を根にかけても、尾を引き寄せる一歩が根のふくらみから地面へ飛び下りて元の所にもどり、その場で頭をつきなおしつづけていた
            // ・頭が根に正面から当たると、体の向きが横向きになって、根のつけ根や切り株の壁にそって歩いていった
            //   （カメラが根の向こうにあると、壁を上る向きが床の上では後ろ向きになり、はね返されることもあった）
            bool autoCam = SaveSystem.Settings.autoCamera;
            SaveSystem.Settings.autoCamera = false;
            try
            {
                Vector3 want = new Vector3(0.5f, 0f, 0.866f);
                Vector2 corner = ForestLayout.Stump + new Vector2(-8.7f, 1.0f);
                Vector2 from = corner - new Vector2(want.x, want.z) * 1.6f;
                foreach (bool camBehindRoot in new[] { false, true })
                {
                    Vector3 start = Place(from, want);
                    Assert.IsTrue(Physics.Raycast(start + Vector3.up * 0.25f, want, out var ahead, 3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                                  && ahead.collider.name.StartsWith("Stump"), "前に切り株の根がある");
                    // カメラが根の向こうにあるときは、手前（下）に倒して根へ向かう
                    yield return FaceCamera(camBehindRoot ? -want : want);
                    yield return Seconds(0.3f);
                    bool fell = false;
                    System.Action onFell = () => fell = true;
                    Worm.Fell += onFell;
                    GameInput.VirtualMove = camBehindRoot ? Vector2.down : Vector2.up;
                    float t = 0f, onRootAt = -1f, gone = 0f;
                    while (t < 7f && !fell)
                    {
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 tail = Worm.TailPoint;
                        if (onRootAt < 0f && tail.y - ForestLayout.Height(tail.x, tail.z) > 0.5f
                            && Physics.OverlapSphere(tail, 0.06f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore).Any(c => c.name.StartsWith("Stump")))
                            onRootAt = t;
                        gone = Vector3.Dot(Vector3.ProjectOnPlane(Worm.CenterPosition - start, Vector3.up), want);
                    }
                    ResetInput();
                    Worm.Fell -= onFell;
                    string where = $"（カメラが根の{(camBehindRoot ? "向こう" : "手前")}）";
                    Assert.IsFalse(fell, "落ちない" + where);
                    Assert.Greater(onRootAt, 0f, "根に乗る" + where);
                    Assert.Less(onRootAt, 4f, "根のつけ根でくねくねせず、すぐに根に乗る" + where);
                    Assert.Greater(gone, 2.5f, $"根をこえて、倒した向きへ進んでいく{where} {start} → {Worm.CenterPosition}");
                }
            }
            finally
            {
                SaveSystem.Settings.autoCamera = autoCam;
            }
        }

        [UnityTest]
        public IEnumerator LogTunnel_EchoesFootsteps_AndMossIsSoft()
        {
            var log = ForestLayout.Landmarks.Find(l => l.useCapsule);
            Vector3 inside = Vector3.Lerp(log.capsuleA, log.capsuleB, 0.5f);
            Assert.IsTrue(AreaProps.Echo(inside), "丸太の中は、足音がひびく");
            Assert.IsFalse(AreaProps.Echo(ForestLayout.Ground(ForestLayout.Spawn.x, ForestLayout.Spawn.y)));
            Vector3 moss = ForestLayout.Ground(ForestLayout.MossHill.x, ForestLayout.MossHill.y);
            Assert.Greater(AreaProps.SoftGround(moss), 0.5f, "苔の丘はふわっと着地");
            yield return null;
        }
    }

    /// <summary>総合テスト：エリアの改善 300（川辺）。流れていく落ち葉と笹舟・滝の虹・場所の音・空気と光。</summary>
    public class RiverAreaPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 120f, "川辺へ");
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator River_HasItsOwnAir_Sounds_Rainbow_AndDrifters()
        {
            Assert.AreEqual(AreaAtmosphere.For("river").fog, RenderSettings.fogColor, "川辺の霧の色");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_waterfall"), "滝の音");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_shallows"), "浅瀬の音");
            Assert.IsTrue(GM.fx.HasEffect("Rainbow"), "滝の虹");
            Assert.IsTrue(GM.fx.HasEffect("PoolBubbles"), "よどみのあわ");
            var drift = AreaProps.Instance.Drifters;
            Assert.GreaterOrEqual(drift.Count(d => !d.boat), 6, "流れていく落ち葉");
            Assert.GreaterOrEqual(drift.Count(d => d.boat), 1, "笹舟");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Leaves_DriftDownstream_AndFallOverTheFalls()
        {
            var leaf = AreaProps.Instance.Drifters.First(d => !d.boat);
            float fz = RiverLayout.FallZ;
            // 滝の少し上の、川のまん中に置く
            leaf.pos = new Vector3(RiverLayout.CenterX(fz + 3f), RiverLayout.UpperLevel + 0.04f, fz + 3f);
            leaf.fallT = -1f;
            float startZ = leaf.pos.z;
            yield return Seconds(0.5f);
            Assert.Less(leaf.pos.z, startZ, "下流へ流れる");
            yield return WaitUntil(() => leaf.pos.z < fz - 1f && leaf.pos.y < RiverLayout.UpperLevel - 1.5f && leaf.fallT < 0f, 20f, "滝を落ちて、滝つぼへ");
            Assert.Greater(Mathf.Abs(leaf.spinVel), 50f, "滝つぼで、くるりと回る");
            Assert.IsTrue(RiverLayout.InChannel(leaf.pos.x, leaf.pos.z, 0f), "川の中");
        }

        [UnityTest]
        public IEnumerator SasaBune_WaitsWhenTheWormIsNear()
        {
            var boat = AreaProps.Instance.Drifters.First(d => d.boat);
            boat.wait = 0f;
            float z = -10f;
            boat.pos = new Vector3(RiverLayout.CenterX(z), RiverLayout.WaterLevel(z) + 0.04f, z);
            float bank = WorldGenerator.RiverBankEdgeX(z, -1f);
            Worm.Spawn(RiverLayout.Ground(bank - 1.8f, z), Vector3.right);
            yield return Seconds(0.2f);
            // 岸のすぐそばへ
            boat.pos = new Vector3(bank + 1.2f, RiverLayout.WaterLevel(z) + 0.04f, z);
            yield return null;
            Vector3 p0 = boat.pos;
            yield return Seconds(0.8f);
            Assert.Less(Vector3.Distance(p0, boat.pos), 0.3f, "近づくと、少しのあいだ止まる（のぞいて見られる）");
        }
    }

    /// <summary>総合テスト：エリアの改善 300（公園）。シーソーの音と土けむり・水飲み場のしずく・空気と光・シャボン玉。</summary>
    public class ParkAreaPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
            SaveSystem.Data.visited.Add("park");
            GM.TravelTo("park");
            yield return WaitUntil(() => Areas.Current == Areas.Park && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, "公園へ");
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator Park_HasItsOwnAir_Bubbles_Clunks_AndDrips()
        {
            Assert.AreEqual(AreaAtmosphere.For("park").fog, RenderSettings.fogColor, "公園の霧の色");
            Assert.IsTrue(GM.fx.HasEffect("SoapBubbles"), "シャボン玉");
            Assert.IsTrue(GM.fx.HasEffect("LampMoths"), "街灯の虫");
            Assert.IsNotNull(AudioManager.Instance.parkAmbience, "公園の環境音");
            // シーソーが反対がわへかたむいて、地面に当たると「ごとん」と土けむり
            int bumps = AreaProps.Instance.SeesawBumps;
            GM.world.Seesaw.Flip();
            yield return WaitUntil(() => AreaProps.Instance.SeesawBumps > bumps, 4f, "シーソーが地面に当たる");
            Assert.Greater(AreaSounds.Instance.CountOf("clunk"), 0, "ごとん");
            // 水飲み場のしずく（水たまりの波紋と同じリズム）
            int drips = AreaSounds.Instance.CountOf("plink");
            yield return Seconds(2.5f);
            int n = AreaSounds.Instance.CountOf("plink") - drips;
            Assert.GreaterOrEqual(n, 2, "ぽちゃ、ぽちゃ");
            Assert.LessOrEqual(n, 3, "波紋と同じ、1 秒に 0.9 回");
        }

        [UnityTest]
        public IEnumerator FlowerBed_CanBeClimbedOnto_AndWalkedOff()
        {
            // 外かられんがを登って土の上へ。土の上から、れんがのふちをこえて外へ（がけで止まらない）
            Vector2 c = ParkLayout.FlowerBed;
            bool OnSoil() => Mathf.Abs(Worm.CenterPosition.x - c.x) < ParkLayout.BedSize.x * 0.5f - 0.6f
                && Mathf.Abs(Worm.CenterPosition.z - c.y) < ParkLayout.BedSize.y * 0.5f - 0.6f && Worm.CenterPosition.y > ParkLayout.BedSoilY - 0.4f;
            bool Outside() => (Mathf.Abs(Worm.CenterPosition.x - c.x) > ParkLayout.BedSize.x * 0.5f + 0.3f
                || Mathf.Abs(Worm.CenterPosition.z - c.y) > ParkLayout.BedSize.y * 0.5f + 0.3f) && Worm.CenterPosition.y < ParkLayout.BedBase + 0.5f;
            Place(c + new Vector2(2f, -7f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(OnSoil, 14f, "外から登って、花だんの土の上へ");
            ResetInput();
            Place(c + new Vector2(6f, 0.5f), Vector3.right);
            yield return FaceCamera(Vector3.right);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(Outside, 14f, "土の上から、れんがのふちをこえて外へ");
            ResetInput();
        }

        [UnityTest]
        public IEnumerator AimButton_DragTurnsTheCameraWhileAiming()
        {
            // スマホ：「ねらう」を押したまま指をすべらせると、ねらいながら見まわせる
            Place(ParkLayout.Spawn + new Vector2(-6f, 0f), Vector3.left);
            yield return FaceCamera(Vector3.left);
            GameInput.VirtualAim = true;
            yield return WaitUntil(() => Worm.IsAiming, 3f, "ねらう");
            float yaw0 = Cam.yaw;
            for (int i = 0; i < 12; i++)
            {
                UI.AimButtonDrag(new Vector2(30f, 0f));
                yield return null;
            }
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw0, Cam.yaw)), 20f, "ねらいながら、カメラが回る");
            Assert.IsTrue(Worm.IsAiming, "ねらったまま");
            GameInput.VirtualAim = false;
            UI.AimButtonDrag(new Vector2(30f, 0f));   // ねらっていないときは、何もしない
            yield return null;
        }

        [UnityTest]
        public IEnumerator SandCastle_And_Bucket_CanBeClimbed()
        {
            var castle = GM.world.ExtraSpots("castle");
            Assert.AreEqual(1, castle.Count);
            Vector3 c = castle[0];
            Assert.IsTrue(Physics.Raycast(c + Vector3.up * 20f, Vector3.down, out var hit, 30f, ShakuConst.SurfaceMask), "城に当たり判定");
            Assert.Greater(hit.point.y, c.y + 2f, "城の上に乗れる高さ");
            var bucket = GM.world.ExtraSpots("bucket");
            Assert.AreEqual(1, bucket.Count);
            Assert.IsTrue(Physics.Raycast(bucket[0] + Vector3.up * 20f, Vector3.down, out var bh, 30f, ShakuConst.SurfaceMask), "バケツに当たり判定");
            Assert.Greater(bh.point.y, bucket[0].y + 0.5f, "バケツの上に乗れる");
            yield return null;
        }
    }
}
