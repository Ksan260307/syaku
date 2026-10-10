using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 読み込み画面のあいだに、エリアで使う物をひととおり 1 回ずつ描いておく。
    /// WebGL では、シェーダーの組み合わせや形のデータを、はじめて描くときに用意するので、
    /// そのままだと大きないきものや新しい物がはじめて画面に入った瞬間に、ゲームが一瞬止まる。
    /// ここでは、カメラのすぐ前に、とても小さく描く（読み込み画面にかくれて見えない）。遠くで使う三角形の少ない形も、先に作る。
    /// 水が見えるときだけ使う描き方（深さを先に描く）も、このあいだだけ通しておく。
    /// </summary>
    public static class RenderWarmup
    {
        /// <summary>WebGL でまとめ描きのシェーダーが用意される、まとめる数のきざみ（これより多いと、この数ずつに分けて描く）。</summary>
        static readonly int[] InstanceSteps = { 2, 32, 64, 96, 128, 160, 192, 224 };

        /// <summary>先に用意した形と材質の組の数（テスト用）。</summary>
        public static int LastCount { get; private set; }

        public static IEnumerator Run(Camera cam, WorldGenerator world, Creatures creatures, int frames = 3)
        {
            if (cam == null || world == null) yield break;
            var items = new List<(Mesh mesh, Material material, bool instanced)>();
            if (world.instanced != null) world.instanced.CollectWarmup(items);
            var loose = world.GetComponent<LooseProps>();
            if (loose != null) loose.CollectWarmup(items);
            if (creatures != null) creatures.CollectWarmup(items);
            if (world.Root != null)
                foreach (var r in world.Root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && r.sharedMaterial != null) items.Add((mf.sharedMesh, r.sharedMaterial, false));
                }
            // 同じ組は 1 つに
            var seen = new HashSet<(Mesh, Material, bool)>();
            var list = new List<(Mesh mesh, Material material, bool instanced)>();
            foreach (var it in items)
                if (it.mesh != null && it.material != null && seen.Add((it.mesh, it.material, it.instanced))) list.Add(it);
            LastCount = list.Count;
            // 遠くで使う三角形の少ない形を、先に作る
            foreach (var it in list)
                for (int lod = 1; lod < it.mesh.lodCount; lod++) DetailMeshes.Get(it.mesh, lod);

            // 1 つだけだと、まとめ描き（インスタンシング）をしない描き方になることがあるので、同じ所に 2 つ描く
            var two = new Matrix4x4[2];
            // まとめ描きのシェーダーは、まとめる数（32 きざみ）ごとに別に用意されるので、材質ごとに、どの数でも 1 回ずつ描く
            var many = new Matrix4x4[InstanceSteps[InstanceSteps.Length - 1]];
            var firstOf = new Dictionary<Material, Mesh>();
            foreach (var it in list)
                if (it.instanced && it.material.enableInstancing && !firstOf.ContainsKey(it.material)) firstOf[it.material] = it.mesh;
            // 水が見えるときにだけ使う描き方（深さを先に描く・色の写し）も、ここで一度通しておく
            WaterView.ForceCopies = true;
            // 影を落とす物が画面に入らないフレームは、影なしの描き方になる。その描き方も 1 フレームだけ通しておく
            Light sun = world.sun;
            var sunShadows = sun != null ? sun.shadows : LightShadows.None;
            try
            {
                for (int f = 0; f < frames; f++)
                {
                    if (sun != null) sun.shadows = f == 1 ? LightShadows.None : sunShadows;
                    Transform t = cam.transform;
                    Vector3 at = t.position + t.forward * (cam.nearClipPlane + 2f);
                    foreach (var it in list)
                    {
                        // 形の大きさによらず、とても小さく（画面では点くらい）
                        float size = Mathf.Max(0.01f, it.mesh.bounds.extents.magnitude);
                        var m = Matrix4x4.TRS(at - t.rotation * (it.mesh.bounds.center * (0.02f / size)), t.rotation, Vector3.one * (0.02f / size));
                        var rp = new RenderParams(it.material)
                        {
                            shadowCastingMode = ShadowCastingMode.On,
                            receiveShadows = true,
                            worldBounds = new Bounds(at, Vector3.one * 0.1f),
                        };
                        if (it.instanced && it.material.enableInstancing)
                        {
                            two[0] = two[1] = m;
                            Graphics.RenderMeshInstanced(rp, it.mesh, 0, two, 2);
                            int lods = it.mesh.lodCount;
                            for (int lod = 1; lod < lods && f == 0; lod++) Graphics.RenderMeshInstanced(rp, DetailMeshes.Get(it.mesh, lod), 0, two, 2);
                        }
                        else Graphics.RenderMesh(rp, it.mesh, 0, m);
                    }
                    if (f == 0)
                        foreach (var kv in firstOf)
                        {
                            float size = Mathf.Max(0.01f, kv.Value.bounds.extents.magnitude);
                            var m = Matrix4x4.TRS(at, t.rotation, Vector3.one * (0.02f / size));
                            for (int i = 0; i < many.Length; i++) many[i] = m;
                            var rp = new RenderParams(kv.Key) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = new Bounds(at, Vector3.one * 0.1f) };
                            foreach (int n in InstanceSteps) Graphics.RenderMeshInstanced(rp, kv.Value, 0, many, n);
                        }
                    yield return null;
                }
            }
            finally
            {
                WaterView.ForceCopies = false;
                if (sun != null) sun.shadows = sunShadows;
            }
        }
    }
}
