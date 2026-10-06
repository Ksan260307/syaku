using UnityEngine;

namespace Shakutori
{
    /// <summary>森の空気感：光の中をただよう花粉、ホタル、舞い落ちる葉、しずくを取ったときのきらめき。</summary>
    public class AmbientFX : MonoBehaviour
    {
        public Material particleMaterial;
        public Transform followTarget;

        ParticleSystem _pollen;
        ParticleSystem _burst;
        ParticleSystem _leaves;
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
        }


        /// <summary>エリアごとの演出（ホタル・落ち葉・滝のしぶき・トンネルの光）を作り直す。</summary>
        public void BuildArea(WorldGenerator world)
        {
            _area = world.Area;
            if (_areaRoot != null) Destroy(_areaRoot.gameObject);
            _areaRoot = new GameObject("AreaFX").transform;
            _areaRoot.SetParent(transform, false);
            if (_area.Id == "forest") BuildForest();
            else BuildRiver();
            foreach (var g in world.Gates) GateMotes(g);
        }

        void BuildForest()
        {
            Fireflies(_areaRoot, new Vector3(ForestLayout.ArchTarget.x + 1.5f, 0f, ForestLayout.ArchTarget.y + 2.5f), 7f, 40, new Color(0.55f, 1f, 0.9f));
            Fireflies(_areaRoot, new Vector3(ForestLayout.Pond.x, 0f, ForestLayout.Pond.y), 15f, 45, new Color(0.85f, 1f, 0.45f));
            Fireflies(_areaRoot, new Vector3(ForestLayout.LogCenter.x, 0f, ForestLayout.LogCenter.y), 10f, 18, new Color(0.85f, 1f, 0.45f));

            // 大樹から舞い落ちる葉
            _leaves = Create("FallingLeaves", _areaRoot, 40, new Color(1f, 0.7f, 0.3f, 1f));
            {
                _leaves.transform.position = new Vector3(0f, ForestLayout.Height(0f, 30f) + 22f, 30f);
                var main = _leaves.main;
                main.startLifetime = 16f;
                main.startSpeed = 0.1f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                main.gravityModifier = 0.02f;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.85f, 0.35f));
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var em = _leaves.emission;
                em.rateOverTime = 1.6f;
                var sh = _leaves.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(70f, 2f, 60f);
                var noise = _leaves.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.15f;
                var rot = _leaves.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
                var col = _leaves.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(Color.white);
            }

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
            var spray = Create("WaterfallSpray", _areaRoot, 220, new Color(0.9f, 0.97f, 1f, 0.7f));
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
            }
            var mist = Create("WaterfallMist", _areaRoot, 60, new Color(0.85f, 0.95f, 1f, 0.25f));
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
            }
        }

        void GateMotes(GateInstance g)
        {
            var ps = Create("GateMotes", _areaRoot, 50, g.def.targetArea == "river" ? new Color(0.6f, 0.95f, 1f, 1f) : new Color(0.8f, 1f, 0.6f, 1f));
            ps.transform.position = g.position + Vector3.up * 0.4f;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = -0.04f;
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
            Wind.Publish(Time.time);
            if (_pollen != null && followTarget != null)
                _pollen.transform.position = followTarget.position;
        }
    }
}
