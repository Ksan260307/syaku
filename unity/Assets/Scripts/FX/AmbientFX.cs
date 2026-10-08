using UnityEngine;

namespace Shakutori
{
    /// <summary>森の空気感：光の中をただよう花粉、ホタル、舞い落ちる葉、しずくを取ったときのきらめき。</summary>
    public class AmbientFX : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Fx = new Unity.Profiling.ProfilerMarker("Shaku.Fx");
        public Material particleMaterial;
        public Transform followTarget;

        ParticleSystem _pollen;
        ParticleSystem _burst;
        ParticleSystem _leaves;
        ParticleSystem _dust;      // 着地の土けむり
        ParticleSystem _splash;    // 水しぶきの粒
        ParticleSystem _drip;      // ぬれた体からたれる、しずく
        // 風に流される演出（粒の大きさ・軽さで、流され方がちがう）
        readonly System.Collections.Generic.List<(ParticleSystem ps, float k)> _windFX = new System.Collections.Generic.List<(ParticleSystem, float)>();
        Transform _areaRoot;
        AreaLayout _area = Areas.Forest;

        public void Build()
        {
            _pollen = Create("Pollen", transform, 160, new Color(1f, 0.95f, 0.75f, 0.8f));
            {
                var main = _pollen.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = _pollen.emission;
                em.rateOverTime = 20f;
                var sh = _pollen.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(14f, 6f, 14f);
                var noise = _pollen.noise;
                noise.enabled = true;
                noise.strength = 0.15f;
                noise.frequency = 0.3f;
                Drag(_pollen, 1.2f);
                var col = _pollen.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(new Color(1f, 0.95f, 0.75f));
            }

            // しずくを取ったときのきらめき
            _burst = Create("CollectBurst", transform, 120, new Color(0.7f, 1f, 1f, 1f));
            {
                var main = _burst.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                main.gravityModifier = -0.05f;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 1f, 1f), new Color(1f, 1f, 0.8f));
                Drag(_burst, 2.5f);
                var em = _burst.emission;
                em.rateOverTime = 0f;
                var sh = _burst.shape;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 0.15f;
                var col = _burst.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white, 0.05f);
                var size = _burst.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            }
            _windFX.Clear();
            _windFX.Add((_pollen, 1.2f));

            // 着地の土けむり：地面にそって広がり、空気のてい抗ですぐ止まって、ふわっと消える
            _dust = Create("LandingDust", transform, 120, new Color(0.72f, 0.62f, 0.48f, 0.55f));
            {
                var main = _dust.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
                main.gravityModifier = 0.05f;
                var em = _dust.emission;
                em.rateOverTime = 0f;
                var sh = _dust.shape;
                sh.shapeType = ParticleSystemShapeType.Circle;   // 面にそって、まわりへ広がる
                sh.radius = 0.12f;
                Drag(_dust, 3.5f);
                var col = _dust.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white, 0.05f);
                var size = _dust.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
            }
            // 水しぶき：上へとび出して、重力で放物線をえがいて落ち、水面にもどると消える
            _splash = Create("WaterSplash", transform, 160, new Color(0.82f, 0.94f, 1f, 0.9f));
            {
                var main = _splash.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                main.gravityModifier = 1f;   // 本当の重力（ShakuPhysics.Gravity）で落ちる
                var em = _splash.emission;
                em.rateOverTime = 0f;
                var sh = _splash.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 28f;
                sh.radius = 0.1f;
                sh.rotation = new Vector3(-90f, 0f, 0f);   // 上向き
                Drag(_splash, 0.4f);
                var col = _splash.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.Planes;
                col.lifetimeLoss = 1f;   // 水面に落ちたら消える
                col.bounce = 0f;
                var plane = new GameObject("SplashWaterPlane").transform;
                plane.SetParent(transform, false);
                col.SetPlane(0, plane);
                _splashPlane = plane;
            }
            // ぬれた体からたれる、しずく
            _drip = Create("WetDrips", transform, 40, new Color(0.8f, 0.94f, 1f, 0.85f));
            {
                var main = _drip.main;
                main.loop = false;
                main.startLifetime = 0.6f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.gravityModifier = 1f;
                var em = _drip.emission;
                em.rateOverTime = 0f;
                var sh = _drip.shape;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 0.15f;
                Drag(_drip, 0.3f);
            }
        }

        Transform _splashPlane;

        /// <summary>画質：かるい（0）では、落ち葉と地面の当たり判定をしない（計算が重いので）。</summary>
        public void SetQuality(int q)
        {
            _quality = q;
            if (_leaves == null) return;
            var col = _leaves.collision;
            col.enabled = q != 0;
        }

        int _quality = 1;

        /// <summary>空気のてい抗（速いほど、ぐっとおそくなる）。</summary>
        static void Drag(ParticleSystem ps, float drag)
        {
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 100f;                         // 速さの上限は使わず、てい抗だけ
            lv.drag = drag;
            lv.multiplyDragByParticleVelocity = true;   // てい抗は速さの 2 乗
            lv.multiplyDragByParticleSize = false;
        }

        /// <summary>落ちて着地した：面にそって土けむりが広がる（強く落ちるほど多い。やわらかい葉っぱの上では、ほとんど出ない）。</summary>
        public void Dust(Vector3 pos, Vector3 normal, float strength, float softness)
        {
            if (_dust == null) return;
            int n = Mathf.RoundToInt((4f + 26f * strength) * (1f - 0.8f * softness));
            if (n <= 0) return;
            _dust.transform.SetPositionAndRotation(pos + normal * 0.03f, Quaternion.FromToRotation(Vector3.forward, normal));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
                if (side.sqrMagnitude < 1e-4f) continue;
                ep.position = pos + normal * 0.03f + side.normalized * 0.1f;
                ep.velocity = side.normalized * Random.Range(0.5f, 1.4f) * (0.5f + strength) + normal * Random.Range(0.05f, 0.4f);
                ep.applyShapeToPosition = false;
                _dust.Emit(ep, 1);
            }
        }

        /// <summary>カメムシのにおい：うすい黄緑のけむりが、ふわっと広がる。</summary>
        public void Stink(Vector3 pos)
        {
            if (_dust == null) return;
            var ep = new ParticleSystem.EmitParams { startColor = new Color(0.72f, 0.86f, 0.36f, 0.55f), applyShapeToPosition = false };
            for (int i = 0; i < 18; i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.6f + 0.2f;
                ep.position = pos + d * 0.12f;
                ep.velocity = d * Random.Range(0.25f, 0.7f);
                ep.startSize = Random.Range(0.25f, 0.45f);
                _dust.Emit(ep, 1);
            }
        }

        /// <summary>水に落ちた：勢いに合わせて、水しぶきの粒がとび出す（水面に落ちると消える）。</summary>
        public void Splash(Vector3 pos, float strength, float waterLevel)
        {
            if (_splash == null) return;
            _splashPlane.SetPositionAndRotation(new Vector3(pos.x, waterLevel - 0.02f, pos.z), Quaternion.identity);
            _splash.transform.position = new Vector3(pos.x, waterLevel + 0.02f, pos.z);
            var main = _splash.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f + 1.5f * strength, 2.0f + 3.0f * strength);
            _splash.Emit(Mathf.RoundToInt(20 + 60 * strength));
        }

        /// <summary>ぬれた体から、しずくがたれる。</summary>
        public void Drip(Vector3 pos)
        {
            if (_drip == null) return;
            _drip.transform.position = pos;
            _drip.Emit(1);
        }


        /// <summary>エリアごとの演出（ホタル・落ち葉・滝のしぶき・トンネルの光）を作り直す。</summary>
        public void BuildArea(WorldGenerator world)
        {
            _area = world.Area;
            if (_areaRoot != null) Destroy(_areaRoot.gameObject);
            _areaRoot = new GameObject("AreaFX").transform;
            _areaRoot.SetParent(transform, false);
            if (_area.Id == "forest") BuildForest();
            else if (_area.Id == "park") BuildPark();
            else BuildRiver();
            foreach (var g in world.Gates) GateMotes(g);
        }

        void BuildForest()
        {
            Fireflies(_areaRoot, new Vector3(ForestLayout.ArchTarget.x + 1.5f, 0f, ForestLayout.ArchTarget.y + 2.5f), 7f, 40, new Color(0.55f, 1f, 0.9f));
            Fireflies(_areaRoot, new Vector3(ForestLayout.Pond.x, 0f, ForestLayout.Pond.y), 15f, 45, new Color(0.85f, 1f, 0.45f));
            Fireflies(_areaRoot, new Vector3(ForestLayout.LogCenter.x, 0f, ForestLayout.LogCenter.y), 10f, 18, new Color(0.85f, 1f, 0.45f));

            // 大樹から舞い落ちる葉
            FallingLeaves(new Vector3(0f, ForestLayout.Height(0f, 30f) + 22f, 30f), new Vector3(70f, 2f, 60f), 1.6f,
                new Color(1f, 0.6f, 0.25f), new Color(1f, 0.85f, 0.35f));
        }

        /// <summary>木から舞い落ちる葉（pos の上空の箱から、風にのって落ち、地面や物に落ちて止まる）。</summary>
        void FallingLeaves(Vector3 pos, Vector3 box, float rate, Color c0, Color c1)
        {
            _leaves = Create("FallingLeaves", _areaRoot, 40, new Color(1f, 0.7f, 0.3f, 1f));
            {
                _leaves.transform.position = pos;
                var main = _leaves.main;
                main.startLifetime = 16f;
                main.startSpeed = 0.1f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                main.gravityModifier = 1f;     // 本当の重力で落ちる
                Drag(_leaves, 4.6f);           // 空気のてい抗が大きいので、最高速度は 1.4 くらい
                main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var em = _leaves.emission;
                em.rateOverTime = rate;
                var sh = _leaves.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = box;
                var noise = _leaves.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.15f;
                var rot = _leaves.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
                // 速く落ちる葉ほど、くるくる速く回る
                var rbs = _leaves.rotationBySpeed;
                rbs.enabled = true;
                rbs.range = new Vector2(0f, 1.5f);
                rbs.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
                // 地面や物に落ちたら、そこで止まる（しばらくして消える）
                var lcol = _leaves.collision;
                lcol.enabled = true;
                lcol.type = ParticleSystemCollisionType.World;
                lcol.collidesWith = ShakuConst.SurfaceMask;
                lcol.dampen = 1f;
                lcol.bounce = 0f;
                lcol.lifetimeLoss = 0f;
                lcol.quality = ParticleSystemCollisionQuality.Low;
                lcol.enabled = _quality != 0;
                _windFX.Add((_leaves, 1.5f));
                var col = _leaves.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white);
            }
        }


        void BuildPark()
        {
            // クヌギの木から舞い落ちる、茶色い葉
            Vector2 k = ParkLayout.Kunugi;
            FallingLeaves(new Vector3(k.x, ParkLayout.Height(k.x, k.y) + 30f, k.y), new Vector3(26f, 2f, 26f), 0.9f,
                new Color(0.75f, 0.5f, 0.25f), new Color(0.95f, 0.75f, 0.35f));
            // 水たまりのまわりのホタル
            Fireflies(_areaRoot, new Vector3(ParkLayout.Puddle.x, 0f, ParkLayout.Puddle.y), 9f, 18, new Color(0.85f, 1f, 0.45f));
        }

        void BuildRiver()
        {
            float pz = RiverLayout.PoolZ;
            Fireflies(_areaRoot, new Vector3(RiverLayout.CenterX(pz) - 8f, 0f, pz + 4f), 12f, 40, new Color(0.85f, 1f, 0.45f));
            Fireflies(_areaRoot, new Vector3(RiverLayout.CenterX(RiverLayout.FallZ - 6f) + 10f, 0f, RiverLayout.FallZ - 6f), 8f, 20, new Color(0.85f, 1f, 0.45f));
            // 滝のしぶき
            float fz = RiverLayout.FallZ;
            float cx = RiverLayout.CenterX(fz);
            float w = RiverLayout.HalfWidth(fz);
            var spray = Create("WaterfallSpray", _areaRoot, 220, new Color(0.9f, 0.97f, 1f, 0.55f));
            spray.transform.position = new Vector3(cx, RiverLayout.WaterLevel(fz - 3f) + 0.3f, fz - 2.8f);
            {
                var main = spray.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
                main.gravityModifier = 0.35f;
                var em = spray.emission;
                em.rateOverTime = 90f;
                var sh = spray.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(w * 1.8f, 0.2f, 0.8f);
                sh.rotation = new Vector3(-60f, 0f, 0f);
                var col = spray.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white, 0.1f);
                var size = spray.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
                Drag(spray, 0.6f);
                _windFX.Add((spray, 0.6f));
            }
            var mist = Create("WaterfallMist", _areaRoot, 60, new Color(0.85f, 0.95f, 1f, 0.12f));   // うすい霧（川が白っぽくかすまないように）
            mist.transform.position = new Vector3(cx, RiverLayout.WaterLevel(fz - 4f) + 1.2f, fz - 4f);
            {
                var main = mist.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
                var em = mist.emission;
                em.rateOverTime = 10f;
                var sh = mist.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(w * 2f, 1f, 3f);
                var col = mist.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white, 0.3f);
                Drag(mist, 0.5f);
                _windFX.Add((mist, 0.8f));
            }
        }

        void GateMotes(GateInstance g)
        {
            var ps = Create("GateMotes", _areaRoot, 50, g.def.targetArea == "river" ? new Color(0.6f, 0.95f, 1f, 1f)
                : g.def.targetArea == "park" ? new Color(1f, 0.9f, 0.55f, 1f) : new Color(0.8f, 1f, 0.6f, 1f));
            ps.transform.position = g.position + Vector3.up * 0.4f;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = -0.04f;
            Drag(ps, 1.5f);
            var em = ps.emission;
            em.rateOverTime = 12f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 2f;
            sh.scale = new Vector3(1f, 0.6f, 1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white);
        }

        void Fireflies(Transform parent, Vector3 center, float radius, int count, Color color)
        {
            var ps = Create("Fireflies", parent, count, color);
            center.y = _area.Height(center.x, center.z) + 1.5f;
            ps.transform.position = center;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = color * 2.2f;
            var em = ps.emission;
            em.rateOverTime = count / 7f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            sh.scale = new Vector3(1f, 0.25f, 1f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.25f;
            Drag(ps, 0.8f);
            _windFX.Add((ps, 0.25f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.2f, 0.4f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        static Gradient FadeInOut(Color c, float inTime = 0.2f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inTime), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        ParticleSystem Create(string name, Transform parent, int max, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = max;
            main.playOnAwake = true;
            main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = particleMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public void Burst(Vector3 pos, int count = 40)
        {
            if (_burst == null) return;
            _burst.transform.position = pos;
            _burst.Emit(count);
        }

        void LateUpdate()
        {
            using var prof = s_Fx.Auto();   // 処理時間の計測（パフォーマンスの調整用）
            Wind.Publish(Time.time);
            if (_pollen != null && followTarget != null)
                _pollen.transform.position = followTarget.position;
            // 風に流される力（粒のある場所の風。軽い粒ほどよく流される）
            for (int i = _windFX.Count - 1; i >= 0; i--)
            {
                var (ps, k) = _windFX[i];
                if (ps == null) { _windFX.RemoveAt(i); continue; }
                Vector3 w = Wind.At(ps.transform.position) * k;
                var f = ps.forceOverLifetime;
                f.enabled = true;
                f.space = ParticleSystemSimulationSpace.World;
                f.x = new ParticleSystem.MinMaxCurve(w.x);
                f.y = new ParticleSystem.MinMaxCurve(0f);
                f.z = new ParticleSystem.MinMaxCurve(w.z);
            }
            // 落ち葉は、風が強いと枝からたくさん落ちる
            if (_leaves != null)
            {
                float g = Wind.Gust(Time.time);
                var em = _leaves.emission;
                em.rateOverTime = 1.6f * (0.5f + 1.5f * g * g);
            }
        }
    }
}
