using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// エリアの改善 300 のただよう物：光の筋のちり、胞子、綿毛、花粉、もや、羽虫、カエデの種、ほら穴のしずく、クモの巣のつゆ、
    /// 川面のきらめき、滝の虹、泡、あわ、ガマの綿毛、中州の砂、シャボン玉、花びら、街灯の虫など。
    /// 粒は加算でかさねるので、水の上の粒は少なく小さく（水面が白っぽく見えないように）。
    /// </summary>
    public partial class AmbientFX
    {
        WorldGenerator _world;
        ParticleSystem _spores, _glints, _splashlets, _slideSparks, _tapDrips, _hollowMotes, _sandBlow, _islandSand, _wake, _bubbles, _lilyHops;
        readonly List<Renderer> _portals = new List<Renderer>();
        MaterialPropertyBlock _portalMpb;
        Material _lineMat;
        float _stoneSplashNext, _marbleGlintNext, _slideGlintNext, _lilyHopNext, _rockGlintNext, _puddleGlintNext;
        Vector3 _ferryPrev;
        readonly System.Random _fxr = new System.Random(5);
        float FR(float a, float b) => a + (b - a) * (float)_fxr.NextDouble();

        /// <summary>このエリアで作った、ただよう物の名前（テスト用）。</summary>
        public readonly List<string> AreaEffects = new List<string>();
        public bool HasEffect(string name) => AreaEffects.Contains(name);

        // ------------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------------
        /// <summary>箱の中に、ふわふわただよう粒（ちり・胞子・花粉など）。</summary>
        ParticleSystem Motes(string name, Vector3 center, Vector3 box, int max, float rate, Color color, Vector2 size, Vector2 life,
            Vector2 speed, float gravity, float noise, float windK, bool horizontal = false)
        {
            var ps = Create(name, _areaRoot, max, color);
            AreaEffects.Add(name);
            ps.transform.position = center;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            if (noise > 0f)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = noise;
                n.frequency = 0.3f;
            }
            Drag(ps, 1.2f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.25f);
            if (horizontal) ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            if (windK > 0f) _windFX.Add((ps, windK));
            return ps;
        }

        /// <summary>くるくる回りながら落ちてくる物（カエデの種・花びら・葉）。地面や物に落ちたら止まる。</summary>
        ParticleSystem Spinners(string name, Vector3 center, Vector3 box, float rate, Color c0, Color c1, Vector2 size, float drag, float spin)
        {
            var ps = Create(name, _areaRoot, 40, c0);
            AreaEffects.Add(name);
            ps.transform.position = center;
            var main = ps.main;
            main.startLifetime = 14f;
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = 1f;
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Drag(ps, drag);
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(spin * 0.6f, spin);
            var n = ps.noise;
            n.enabled = true;
            n.strength = 0.4f;
            n.frequency = 0.2f;
            var lcol = ps.collision;
            lcol.enabled = _quality != 0;
            lcol.type = ParticleSystemCollisionType.World;
            lcol.collidesWith = ShakuConst.SurfaceMask;
            lcol.dampen = 1f;
            lcol.bounce = 0f;
            lcol.lifetimeLoss = 0f;
            lcol.quality = ParticleSystemCollisionQuality.Low;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.1f);
            _windFX.Add((ps, 1.3f));
            return ps;
        }

        /// <summary>一度に出す粒（呼ばれたときに Emit）。</summary>
        ParticleSystem Emitter(string name, int max, Color color, Vector2 size, Vector2 life, float gravity, float drag)
        {
            var ps = Create(name, transform, max, color);
            var main = ps.main;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.rateOverTime = 0f;
            Drag(ps, drag);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.05f);
            return ps;
        }

        void EmitAt(ParticleSystem ps, Vector3 pos, Vector3 vel, int count, float spread, Color? color = null, float size = -1f)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            if (color.HasValue) ep.startColor = color.Value;
            if (size > 0f) ep.startSize = size;
            for (int i = 0; i < count; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.05f;
                ep.velocity = vel + Random.insideUnitSphere * spread;
                ps.Emit(ep, 1);
            }
        }

        void EnsureEmitters()
        {
            if (_spores != null) return;
            _spores = Emitter("PuffSpores", 120, new Color(0.86f, 0.8f, 0.6f, 0.5f), new Vector2(0.18f, 0.4f), new Vector2(1.2f, 2.4f), -0.02f, 2.5f);
            _glints = Emitter("Glints", 60, new Color(1f, 1f, 0.95f, 1f), new Vector2(0.08f, 0.16f), new Vector2(0.25f, 0.5f), 0f, 1f);
            var gs = _glints.sizeOverLifetime;
            gs.enabled = true;
            gs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            _splashlets = Emitter("Splashlets", 80, new Color(0.82f, 0.94f, 1f, 0.8f), new Vector2(0.03f, 0.07f), new Vector2(0.3f, 0.6f), 1f, 0.5f);
            _slideSparks = Emitter("SlideSparks", 80, new Color(1f, 0.97f, 0.8f, 0.9f), new Vector2(0.06f, 0.12f), new Vector2(0.4f, 0.8f), 0f, 2f);
            _tapDrips = Emitter("TapDrips", 20, new Color(0.8f, 0.94f, 1f, 0.9f), new Vector2(0.05f, 0.07f), new Vector2(0.4f, 0.45f), 1f, 0.2f);
        }

        /// <summary>ホコリタケの胞子のけむり。</summary>
        public void Spores(Vector3 p)
        {
            EnsureEmitters();
            for (int i = 0; i < 16; i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.8f + 0.4f;
                EmitAt(_spores, p, d * Random.Range(0.4f, 1.1f), 1, 0.05f);
            }
        }

        /// <summary>小さなきらめき。</summary>
        public void Glint(Vector3 p)
        {
            EnsureEmitters();
            EmitAt(_glints, p, Vector3.zero, 1, 0f);
        }

        /// <summary>小さな水はね。</summary>
        public void Splashlet(Vector3 p)
        {
            EnsureEmitters();
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * 0.6f;
                EmitAt(_splashlets, p + Vector3.up * 0.03f, new Vector3(r.x, Random.Range(0.8f, 1.6f), r.y), 1, 0.05f);
            }
        }

        /// <summary>すべり台をすべるときの、うしろへ流れるきらきら。</summary>
        public void SlideSparkle(Vector3 p, Vector3 back)
        {
            EnsureEmitters();
            EmitAt(_slideSparks, p + Random.insideUnitSphere * 0.6f, back * Random.Range(1f, 2.5f) + Vector3.up * 0.3f, 1, 0.2f);
        }

        /// <summary>水飲み場のじゃぐちから落ちるしずく。</summary>
        public void TapDrip(Vector3 waterPoint)
        {
            EnsureEmitters();
            // 落ちて水面にとどくまで 0.4 秒（高さ 0.8 から）
            EmitAt(_tapDrips, waterPoint + Vector3.up * 0.8f, Vector3.zero, 1, 0f);
        }

        /// <summary>しずくを取った・名所を見つけたときのきらめき（エリアの色をまぜる）。</summary>
        public void Burst(Vector3 pos, int count, Color tint)
        {
            if (_burst == null) return;
            _burst.transform.position = pos;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.startColor = Color.Lerp(Color.white, tint, Random.Range(0.3f, 1f));
                _burst.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------
        // エリアごと
        // ------------------------------------------------------------------
        void BuildAreaExtras(WorldGenerator world)
        {
            _world = world;
            AreaEffects.Clear();
            _hollowMotes = _sandBlow = _islandSand = _wake = _bubbles = null;
            EnsureEmitters();
            _portals.Clear();
            if (world.Root != null)
                foreach (var r in world.Root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.name == "GatePortal") _portals.Add(r);
            if (_lineMat == null && particleMaterial != null)
            {
                _lineMat = new Material(particleMaterial) { name = "RainbowLine" };
                _lineMat.SetFloat("_Shape", 1f);
                _lineMat.SetFloat("_Softness", 0.9f);
            }
            if (_area.Id == "forest") ForestFX(world);
            else if (_area.Id == "river") RiverFX(world);
            else ParkFX(world);
        }

        void ShaftMotes(IEnumerable<Vector2> spots)
        {
            int i = 0;
            foreach (var s in spots)
            {
                Vector3 g = _area.Ground(s.x, s.y);
                Motes("ShaftMotes" + i++, g + Vector3.up * 3f, new Vector3(3f, 6f, 3f), 30, 4f, new Color(1f, 0.95f, 0.8f, 0.55f),
                    new Vector2(0.02f, 0.045f), new Vector2(5f, 8f), new Vector2(0.01f, 0.05f), -0.003f, 0.12f, 0.3f);
            }
        }

        void ForestFX(WorldGenerator world)
        {
            var spots = new List<Vector2>();
            spots.AddRange(new[] { ForestLayout.Meadow, ForestLayout.Stump + new Vector2(-2f, 2f), ForestLayout.AcornPlaza });
            spots.AddRange(WorldGenerator.ForestExtraShafts);
            ShaftMotes(spots);   // 6. 光の筋の中のちり
            Vector2 h = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
            Vector3 hg = ForestLayout.Ground(h.x, h.y);
            // 7. 光るキノコの洞のまわりの、地面の青い光のにじみ・8. 青白い光のつぶがのぼる（79. 近づくとふえる）
            Motes("HollowHalo", hg + Vector3.up * 0.12f, new Vector3(9f, 0.05f, 9f), 14, 2f, new Color(0.35f, 0.75f, 1f, 0.16f),
                new Vector2(2.5f, 4f), new Vector2(6f, 8f), Vector2.zero, 0f, 0f, 0f, horizontal: true);
            _hollowMotes = Motes("HollowMotes", hg + Vector3.up * 0.6f, new Vector3(9f, 0.6f, 9f), 60, 5f, new Color(0.55f, 0.9f, 1f, 0.8f),
                new Vector2(0.04f, 0.08f), new Vector2(4f, 7f), new Vector2(0.05f, 0.15f), -0.04f, 0.2f, 0.1f);
            // 9. 赤キノコの森の胞子
            Vector3 grove = ForestLayout.Ground(ForestLayout.MushroomGrove.x, ForestLayout.MushroomGrove.y);
            Motes("GroveSpores", grove + Vector3.up * 3f, new Vector3(24f, 5f, 24f), 70, 7f, new Color(1f, 0.92f, 0.85f, 0.6f),
                new Vector2(0.03f, 0.06f), new Vector2(6f, 10f), new Vector2(0.02f, 0.08f), -0.006f, 0.25f, 0.8f);
            // 10. 花の草原のタンポポの綿毛・11. 草原の花粉
            Vector3 meadow = ForestLayout.Ground(ForestLayout.Meadow.x, ForestLayout.Meadow.y);
            Motes("MeadowSeeds", meadow + Vector3.up * 1.5f, new Vector3(26f, 2f, 26f), 40, 2.5f, new Color(1f, 1f, 1f, 0.85f),
                new Vector2(0.08f, 0.14f), new Vector2(8f, 12f), new Vector2(0.05f, 0.2f), -0.01f, 0.3f, 1.6f);
            Motes("MeadowPollen", meadow + Vector3.up * 2f, new Vector3(24f, 3f, 24f), 80, 10f, new Color(1f, 0.9f, 0.5f, 0.7f),
                new Vector2(0.02f, 0.04f), new Vector2(5f, 8f), new Vector2(0.02f, 0.08f), -0.004f, 0.18f, 1f);
            // 12. 水たまりの水面すれすれの、うすいもや（白くならないよう、少なくうすく）・13. 羽虫の柱
            Vector3 pond = new Vector3(ForestLayout.Pond.x, ForestLayout.WaterLevel + 0.4f, ForestLayout.Pond.y);
            Motes("PondMist", pond, new Vector3(26f, 0.4f, 26f), 14, 1.2f, new Color(0.85f, 0.95f, 1f, 0.045f),
                new Vector2(2f, 3.5f), new Vector2(6f, 9f), new Vector2(0.05f, 0.15f), 0f, 0.1f, 0.4f);
            Motes("Midges", pond + new Vector3(6f, 1.4f, -4f), new Vector3(0.6f, 1.6f, 0.6f), 30, 8f, new Color(0.25f, 0.25f, 0.2f, 0.5f),
                new Vector2(0.025f, 0.035f), new Vector2(2f, 3f), new Vector2(0.1f, 0.3f), 0f, 1.6f, 0.05f);
            // 14. 大樹から、くるくる回るカエデの種
            Spinners("MapleSeeds", ForestLayout.Ground(0f, 52f) + Vector3.up * 26f, new Vector3(30f, 2f, 16f), 0.5f,
                new Color(0.85f, 0.7f, 0.45f), new Color(0.7f, 0.55f, 0.35f), new Vector2(0.18f, 0.26f), 3.2f, 9f);
            // 15. 古い切り株のまわりに、ときどき舞い落ちる葉
            Vector3 st = ForestLayout.Ground(ForestLayout.Stump.x, ForestLayout.Stump.y);
            Spinners("StumpLeaves", st + Vector3.up * 18f, new Vector3(20f, 2f, 20f), 0.35f,
                new Color(0.9f, 0.55f, 0.25f), new Color(0.75f, 0.45f, 0.2f), new Vector2(0.15f, 0.28f), 4.6f, 2.5f);
            // 17. ほら穴の天井から落ちるしずく
            Motes("CaveDrips", hg + Vector3.up * 4.2f, new Vector3(6f, 0.1f, 6f), 12, 1.2f, new Color(0.7f, 0.95f, 1f, 0.9f),
                new Vector2(0.04f, 0.06f), new Vector2(0.9f, 1f), Vector2.zero, 1f, 0f, 0f);
            // 19. クモの巣のつゆがきらっと・20. 苔の丘の小さな緑の光・22. 大樹の根元のホタル
            Vector3 moss = ForestLayout.Ground(ForestLayout.MossHill.x, ForestLayout.MossHill.y);
            Motes("MossGlow", moss + Vector3.up * 0.15f, new Vector3(18f, 0.3f, 18f), 50, 6f, new Color(0.6f, 1f, 0.5f, 0.75f),
                new Vector2(0.03f, 0.05f), new Vector2(1.5f, 3f), Vector2.zero, 0f, 0f, 0f);
            Fireflies(_areaRoot, new Vector3(ForestLayout.GreatTree.x, 0f, ForestLayout.GreatTree.y - 27f), 12f, 30, new Color(0.85f, 1f, 0.45f));
            AreaEffects.Add("RootFireflies");
        }

        void RiverFX(WorldGenerator world)
        {
            var spots = new List<Vector2>(WorldGenerator.RiverExtraShafts()) { RiverLayout.IslandCenter, new Vector2(RiverLayout.CenterX(RiverLayout.PoolZ), RiverLayout.PoolZ) };
            ShaftMotes(spots);
            float fz = RiverLayout.FallZ, pz = RiverLayout.PoolZ;
            // 6. 川面のきらめき（小さく、少なく）
            foreach (float z in new[] { 48f, 10f, -12f, -34f })
            {
                var g = Motes("RiverGlints" + Mathf.RoundToInt(z), new Vector3(RiverLayout.CenterX(z), RiverLayout.WaterLevel(z) + 0.05f, z),
                    new Vector3(RiverLayout.HalfWidth(z) * 1.6f, 0.02f, 18f), 30, 9f, new Color(1f, 1f, 0.92f, 0.9f),
                    new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.45f), Vector2.zero, 0f, 0f, 0f);
                var sz = g.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
            }
            // 7. しぶきの滝の虹
            Rainbow(new Vector3(RiverLayout.CenterX(fz - 3.5f), RiverLayout.WaterLevel(fz - 3.5f), fz - 3.5f), 5.2f);
            // 8. よどみの上の光のつぶ
            Vector3 pool = new Vector3(RiverLayout.CenterX(pz), RiverLayout.WaterLevel(pz) + 1.2f, pz);
            Motes("PoolMotes", pool, new Vector3(22f, 2f, 22f), 40, 5f, new Color(1f, 0.97f, 0.8f, 0.6f),
                new Vector2(0.03f, 0.05f), new Vector2(5f, 8f), new Vector2(0.02f, 0.06f), -0.004f, 0.2f, 0.6f);
            // 13. 滝の下を流れていく泡のつぶ（少なく）
            var foam = Motes("FallFoam", new Vector3(RiverLayout.CenterX(fz - 3f), RiverLayout.WaterLevel(fz - 3f) + 0.03f, fz - 3f),
                new Vector3(RiverLayout.HalfWidth(fz) * 1.2f, 0.01f, 1.5f), 50, 6f, new Color(0.9f, 0.97f, 1f, 0.55f),
                new Vector2(0.05f, 0.1f), new Vector2(3f, 5f), Vector2.zero, 0f, 0.05f, 0f, horizontal: true);
            var fv = foam.velocityOverLifetime;
            fv.enabled = true;
            fv.space = ParticleSystemSimulationSpace.World;
            fv.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            fv.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            fv.z = new ParticleSystem.MinMaxCurve(-1.6f, -0.9f);
            // 14. よどみの底からのぼるあわ
            _bubbles = Motes("PoolBubbles", new Vector3(pool.x, RiverLayout.WaterLevel(pz) - 0.8f, pool.z), new Vector3(16f, 0.2f, 16f), 30, 3f,
                new Color(0.85f, 0.97f, 1f, 0.5f), new Vector2(0.04f, 0.08f), new Vector2(1.2f, 1.6f), new Vector2(0f, 0f), -0.06f, 0.05f, 0f);
            // 17. 岸のガマの穂の綿毛・20. 川の上をわたるタンポポの綿毛
            foreach (var r in world.ExtraSpots("reed"))
            {
                Motes("ReedFluff", r + Vector3.up * 2.4f, new Vector3(1.5f, 0.5f, 1.5f), 10, 0.4f, new Color(1f, 0.97f, 0.9f, 0.85f),
                    new Vector2(0.05f, 0.09f), new Vector2(8f, 12f), new Vector2(0.05f, 0.15f), -0.005f, 0.3f, 1.5f);
                break;
            }
            Motes("RiverSeeds", new Vector3(RiverLayout.CenterX(0f), RiverLayout.WaterLevel(0f) + 2.5f, 0f), new Vector3(30f, 3f, 70f), 40, 2f,
                new Color(1f, 1f, 1f, 0.85f), new Vector2(0.07f, 0.12f), new Vector2(10f, 14f), new Vector2(0.05f, 0.2f), -0.008f, 0.35f, 1.6f);
            // 18. 中州の砂がさらさら舞う（風が強いとき）
            Vector2 ic = RiverLayout.IslandCenter;
            _islandSand = Motes("IslandSand", RiverLayout.Ground(ic.x, ic.y) + Vector3.up * 0.15f, new Vector3(7f, 0.2f, 12f), 50, 0f,
                new Color(0.95f, 0.88f, 0.7f, 0.35f), new Vector2(0.04f, 0.08f), new Vector2(1f, 2f), new Vector2(0.1f, 0.3f), 0.05f, 0.2f, 2f);
            // 21. 倒れ枝の橋の上から、ときどき葉が川へ
            Vector3 bridge = Vector3.Lerp(world.BridgeA, world.BridgeB, 0.5f);
            Spinners("BridgeLeaves", bridge + Vector3.up * 6f, new Vector3(Vector3.Distance(world.BridgeA, world.BridgeB) * 0.6f, 1f, 2f), 0.12f,
                new Color(0.85f, 0.6f, 0.3f), new Color(0.6f, 0.75f, 0.35f), new Vector2(0.15f, 0.24f), 4.6f, 2.5f);
            // 98. トンネルの出口に、川の方から風がふいてくる（光のつぶが流れる）
            Vector2 gate = RiverLayout.Gate + new Vector2(8f, 0.5f);
            var breeze = Motes("GateBreeze", RiverLayout.Ground(gate.x, gate.y) + Vector3.up * 1.2f + Vector3.right * 6f, new Vector3(2f, 2f, 6f), 40, 6f,
                new Color(0.75f, 0.95f, 1f, 0.6f), new Vector2(0.03f, 0.06f), new Vector2(3f, 4f), Vector2.zero, 0f, 0.15f, 0f);
            var bv = breeze.velocityOverLifetime;
            bv.enabled = true;
            bv.space = ParticleSystemSimulationSpace.World;
            bv.x = new ParticleSystem.MinMaxCurve(-2.2f, -1.4f);
            bv.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
            bv.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            // 100. 葉っぱの舟のうしろの、小さな波のつぶ
            _wake = Emitter("FerryWake", 60, new Color(0.85f, 0.96f, 1f, 0.5f), new Vector2(0.05f, 0.1f), new Vector2(0.6f, 1.1f), 0f, 1.5f);
            _wake.transform.SetParent(_areaRoot, false);
            if (world.Ferry != null) _ferryPrev = world.Ferry.transform.position;
        }

        /// <summary>滝のしぶきにかかる小さな虹（色の帯を重ねた半円。加算でうすく）。</summary>
        void Rainbow(Vector3 center, float radius)
        {
            if (_lineMat == null) return;
            var mesh = new Mesh { name = "Rainbow" };
            var v = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            Color[] bands = { new Color(1f, 0.25f, 0.25f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.95f, 0.3f), new Color(0.35f, 1f, 0.4f), new Color(0.35f, 0.65f, 1f), new Color(0.6f, 0.4f, 1f) };
            const int seg = 40;
            float w = 0.16f;
            for (int b = 0; b < bands.Length; b++)
            {
                float r = radius - b * w;
                int start = v.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * i / seg;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    float fade = Mathf.Sin(a) * 0.9f + 0.1f;   // 両はしは、うすく
                    Color col = bands[b] * new Color(1f, 1f, 1f, 0.22f * fade);
                    v.Add(d * (r + w * 0.5f)); c.Add(col); uv.Add(new Vector2(i / (float)seg, 1f));
                    v.Add(d * (r - w * 0.5f)); c.Add(col); uv.Add(new Vector2(i / (float)seg, 0f));
                }
                for (int i = 0; i < seg; i++)
                {
                    int a0 = start + i * 2;
                    t.Add(a0); t.Add(a0 + 1); t.Add(a0 + 2);
                    t.Add(a0 + 1); t.Add(a0 + 3); t.Add(a0 + 2);
                }
            }
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Rainbow");
            go.transform.SetParent(_areaRoot, false);
            go.transform.position = center;
            // 滝を見上げる景色（名所の景色）から、正面に見える向き
            Vector3 view = new Vector3(-7f, 0f, 11f) - new Vector3(center.x, 0f, center.z);
            go.transform.rotation = Quaternion.LookRotation(view.normalized, Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _lineMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            AreaEffects.Add("Rainbow");
        }

        void ParkFX(WorldGenerator world)
        {
            var spots = new List<Vector2> { ParkLayout.FlowerBed, ParkLayout.Sandbox };
            spots.AddRange(WorldGenerator.ParkExtraShafts);
            ShaftMotes(spots);
            // 7. 街灯のまわりのやわらかな光・15. 街灯のまわりを飛ぶ小さな虫
            Vector3 lamp = ParkLayout.Ground(ParkLayout.Lamp.x, ParkLayout.Lamp.y);
            Vector3 lampTop = lamp + Vector3.up * LampHeight();
            Motes("LampHalo", lampTop, new Vector3(0.2f, 0.2f, 0.2f), 3, 0.6f, new Color(1f, 0.92f, 0.65f, 0.22f),
                new Vector2(3f, 3.6f), new Vector2(4f, 5f), Vector2.zero, 0f, 0f, 0f);
            Motes("LampMoths", lampTop + Vector3.down * 0.4f, new Vector3(1.6f, 1f, 1.6f), 14, 3f, new Color(0.9f, 0.85f, 0.7f, 0.7f),
                new Vector2(0.04f, 0.06f), new Vector2(2f, 3f), new Vector2(0.3f, 0.6f), 0f, 2.5f, 0f);
            // 9. 公園の上を飛んでいくシャボン玉・98. 着いたとき（入り口のそば）にも、シャボン玉
            var bub = Motes("SoapBubbles", ParkLayout.Ground(-6f, 4f) + Vector3.up * 2.5f, new Vector3(40f, 2f, 40f), 24, 0.9f,
                new Color(0.85f, 0.9f, 1f, 0.35f), new Vector2(0.3f, 0.6f), new Vector2(9f, 14f), new Vector2(0.05f, 0.2f), -0.012f, 0.35f, 1.4f);
            var bc = bub.colorOverLifetime;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.95f), 0f), new GradientColorKey(new Color(0.75f, 0.95f, 1f), 0.5f), new GradientColorKey(new Color(1f, 1f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            bc.color = g;
            var arrive = Motes("ArrivalBubbles", ParkLayout.Ground(ParkLayout.Spawn.x - 4f, ParkLayout.Spawn.y) + Vector3.up * 1.2f, new Vector3(4f, 1f, 4f), 12, 0f,
                new Color(0.85f, 0.9f, 1f, 0.35f), new Vector2(0.3f, 0.5f), new Vector2(6f, 9f), new Vector2(0.1f, 0.3f), -0.02f, 0.3f, 1f);
            var ab = arrive.emission;
            ab.SetBursts(new[] { new ParticleSystem.Burst(0.3f, 10) });
            var abm = arrive.main;
            abm.loop = false;
            arrive.Play();
            // 10. 芝生のタンポポの綿毛・11. 花だんの花粉・12. 花だんから落ちる花びら
            Motes("LawnSeeds", ParkLayout.Ground(10f, 10f) + Vector3.up * 1.5f, new Vector3(70f, 2f, 70f), 40, 2.5f, new Color(1f, 1f, 1f, 0.85f),
                new Vector2(0.07f, 0.12f), new Vector2(9f, 13f), new Vector2(0.05f, 0.2f), -0.01f, 0.35f, 1.6f);
            Vector3 bed = ParkLayout.Ground(ParkLayout.FlowerBed.x, ParkLayout.FlowerBed.y);
            Motes("BedPollen", bed + Vector3.up * 2.2f, new Vector3(22f, 2f, 9f), 50, 8f, new Color(1f, 0.92f, 0.55f, 0.7f),
                new Vector2(0.02f, 0.04f), new Vector2(4f, 7f), new Vector2(0.02f, 0.07f), -0.004f, 0.18f, 1f);
            Spinners("TulipPetals", bed + Vector3.up * 3.2f, new Vector3(20f, 0.5f, 8f), 0.25f,
                new Color(1f, 0.45f, 0.5f), new Color(1f, 0.85f, 0.4f), new Vector2(0.12f, 0.2f), 5f, 2f);
            // 14. 砂場で、風が強いとき砂がさらさら舞う
            Vector3 sand = ParkLayout.Ground(ParkLayout.Sandbox.x, ParkLayout.Sandbox.y);
            _sandBlow = Motes("SandBlow", sand + Vector3.up * 0.15f, new Vector3(24f, 0.2f, 16f), 60, 0f, new Color(0.96f, 0.88f, 0.66f, 0.35f),
                new Vector2(0.04f, 0.08f), new Vector2(1f, 2f), new Vector2(0.1f, 0.3f), 0.05f, 0.2f, 2f);
            // 19. ベンチのまわりに吹きよせられる落ち葉・20. ジャングルジムのてっぺんを流れていく葉
            Spinners("BenchLeaves", ParkLayout.Ground(ParkLayout.Bench.x - 6f, ParkLayout.Bench.y) + Vector3.up * 8f, new Vector3(10f, 1f, 18f), 0.2f,
                new Color(0.8f, 0.55f, 0.28f), new Color(0.95f, 0.75f, 0.35f), new Vector2(0.15f, 0.25f), 4.6f, 2.2f);
            var gymLeaves = Motes("GymWindLeaves", ParkLayout.Ground(ParkLayout.JungleGym.x, ParkLayout.JungleGym.y) + Vector3.up * 13.5f, new Vector3(10f, 2f, 10f), 20, 0.8f,
                new Color(0.85f, 0.65f, 0.3f, 0.9f), new Vector2(0.12f, 0.2f), new Vector2(3f, 5f), new Vector2(0.1f, 0.3f), 0.05f, 0.3f, 3.5f);
            var gr = gymLeaves.rotationOverLifetime;
            gr.enabled = true;
            gr.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            // 21. 水たまりの水面のきらめき
            Vector3 pud = new Vector3(ParkLayout.Puddle.x, ParkLayout.WaterLevel + 0.03f, ParkLayout.Puddle.y);
            var pg = Motes("PuddleGlints", pud, new Vector3(ParkLayout.PuddleRadius * 1.6f, 0.02f, ParkLayout.PuddleRadius * 1.6f), 16, 4f, new Color(1f, 1f, 0.92f, 0.9f),
                new Vector2(0.05f, 0.09f), new Vector2(0.2f, 0.45f), Vector2.zero, 0f, 0f, 0f);
            var ps = pg.sizeOverLifetime;
            ps.enabled = true;
            ps.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
        }

        float LampHeight()
        {
            if (_world == null || _world.assets == null) return 12f;
            var m = _world.assets.Get("Park_Lamp");
            return m != null ? m.bounds.max.y - 0.6f : 12f;
        }

        void UpdateAreaExtras()
        {
            if (_world == null || !_world.IsGenerated) return;
            float t = Time.time;
            // 98（森）. トンネルの光が、ゆっくり明るくなったり暗くなったりする
            if (_portals.Count > 0)
            {
                _portalMpb ??= new MaterialPropertyBlock();
                foreach (var r in _portals)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_portalMpb);
                    Color c = _portalMpb.GetColor("_TintColor");
                    c.a = 0.42f + 0.18f * Mathf.Sin(t * 1.4f + r.transform.position.x);
                    _portalMpb.SetColor("_TintColor", c);
                    r.SetPropertyBlock(_portalMpb);
                }
            }
            var worm = InchwormController.Instance;
            float gust = Wind.Gust(t);
            if (_area.Id == "forest")
            {
                // 79. 光るキノコの洞に近づくと、光のつぶがふえる
                if (_hollowMotes != null && worm != null)
                {
                    Vector2 h = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
                    float near = 1f - Mathf.Clamp01((Vector2.Distance(new Vector2(worm.CenterPosition.x, worm.CenterPosition.z), h) - 4f) / 14f);
                    var em = _hollowMotes.emission;
                    em.rateOverTime = 4f + 12f * near;
                }
                // 19. クモの巣のつゆが、ときどききらっと光る・100. 葉っぱの舟のふちで、ときどきしずくがはねる
                if (t > _rockGlintNext)
                {
                    _rockGlintNext = t + FR(0.6f, 1.4f);
                    var webs = _world.ExtraSpots("web");
                    if (webs.Count > 0) Glint(webs[_fxr.Next(webs.Count)] + Random.insideUnitSphere * 0.8f);
                }
                if (t > _lilyHopNext)
                {
                    _lilyHopNext = t + FR(2f, 5f);
                    Vector2 p = ForestLayout.Pond + Random.insideUnitCircle * 9f;
                    Splashlet(new Vector3(p.x, ForestLayout.WaterLevel + 0.18f, p.y));
                }
            }
            else if (_area.Id == "river")
            {
                // 16. とびいしの瀬で、石に当たる水が小さなしぶき・19. 滝つぼのまわりの岩がきらめく
                if (t > _stoneSplashNext && _world.StepStones.Count > 0)
                {
                    _stoneSplashNext = t + FR(0.4f, 1f);
                    Vector3 s = _world.StepStones[_fxr.Next(_world.StepStones.Count)];
                    Splashlet(new Vector3(s.x + FR(-0.8f, 0.8f), RiverLayout.WaterLevel(s.z), s.z + FR(0.4f, 0.9f)));
                }
                if (t > _rockGlintNext)
                {
                    _rockGlintNext = t + FR(0.3f, 0.8f);
                    float fz = RiverLayout.FallZ;
                    float side = Random.value < 0.5f ? -1f : 1f;
                    Vector3 r = new Vector3(RiverLayout.CenterX(fz) + side * (RiverLayout.HalfWidth(fz) + FR(0.5f, 2.5f)), RiverLayout.WaterLevel(fz - 1f) + FR(0.4f, 2.2f), fz + FR(-1f, 1.5f));
                    Glint(r);
                }
                if (_islandSand != null)
                {
                    var em = _islandSand.emission;
                    em.rateOverTime = gust > 0.6f ? (gust - 0.6f) * 60f : 0f;
                }
                // 100. 葉っぱの舟が動くと、うしろに小さな波のつぶ
                var ferry = _world.Ferry;
                if (ferry != null && _wake != null)
                {
                    Vector3 p = ferry.transform.position;
                    Vector3 v = (p - _ferryPrev) / Mathf.Max(Time.deltaTime, 1e-4f);
                    _ferryPrev = p;
                    if (v.sqrMagnitude > 0.04f && Random.value < Time.deltaTime * 14f)
                    {
                        Vector3 back = -v.normalized;
                        Vector3 side = Vector3.Cross(Vector3.up, back) * FR(-1f, 1f);
                        EmitAt(_wake, new Vector3(p.x, RiverLayout.WaterLevel(p.z) + 0.03f, p.z) + back * 2.4f + side, back * 0.3f + side * 0.4f, 1, 0.05f);
                    }
                }
            }
            else
            {
                if (_sandBlow != null)
                {
                    var em = _sandBlow.emission;
                    em.rateOverTime = gust > 0.6f ? (gust - 0.6f) * 70f : 0f;
                }
                // 8. すべり台の坂が、ときどききらっと光る
                if (t > _slideGlintNext && _world.Slide != null)
                {
                    _slideGlintNext = t + FR(1.5f, 3.5f);
                    float k = FR(0.1f, 0.9f);
                    Vector3 a = _world.Slide.slide.TransformPoint(SlideRide.RampTop), b = _world.Slide.slide.TransformPoint(SlideRide.RampLow);
                    Glint(Vector3.Lerp(a, b, k) + Vector3.up * 0.15f + Random.insideUnitSphere * 0.8f);
                }
                // 22. ビー玉が、日の光にきらめく
                if (t > _marbleGlintNext)
                {
                    _marbleGlintNext = t + FR(0.8f, 2f);
                    foreach (var rp in RollingProp.All)
                    {
                        if (rp == null || !rp.name.StartsWith("Park_Marble") || Random.value > 0.35f) continue;
                        Glint(rp.transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f);
                        break;
                    }
                }
            }
            // 14（川辺）. よどみのあわは、水面まで上がったら消える（のぼる速さを、ゆっくり）
            if (_bubbles != null)
            {
                var vel = _bubbles.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            }
        }
    }
}
