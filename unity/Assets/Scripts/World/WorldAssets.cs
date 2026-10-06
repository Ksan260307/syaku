using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>Blender で作ったメッシュとマテリアルへの参照（エディタのセットアップで自動生成）。</summary>
    [CreateAssetMenu(menuName = "Shakutori/World Assets")]
    public class WorldAssets : ScriptableObject
    {
        [Serializable]
        public struct NamedMesh
        {
            public string name;
            public Mesh mesh;
        }

        [Serializable]
        public struct NamedTexture
        {
            public string name;
            public Texture2D texture;
        }

        public List<NamedMesh> meshes = new List<NamedMesh>();
        public List<NamedTexture> portraits = new List<NamedTexture>();

        [Header("Materials")]
        public Material prop;          // 岩・キノコ・落ち葉など（輪郭線あり）
        public Material propGlossy;    // どんぐりなど艶のあるもの
        public Material bark;          // 大樹・切り株・丸太（大きいので輪郭控えめ）
        public Material glow;          // 光るキノコ
        public Material terrain;
        public Material foliage;       // 草・クローバー・苔
        public Material flowers;       // 花・シダ（大きめの植物）
        public Material water;
        public Material lightShaft;
        public Material dewdrop;
        public Material particle;
        public Material silk;
        public Material worm;
        public Material river;         // 流れる川
        public Material waterfall;     // 滝
        public Material creature;      // いきもの
        public Material creatureWing;  // 羽（両面）
        public Material creatureGlow;  // ホタルの光

        [Header("Textures")]
        public Texture2D sunCookie;

        Dictionary<string, Mesh> _lookup;

        public Texture2D Portrait(string id)
        {
            foreach (var p in portraits)
                if (p.name == id) return p.texture;
            return null;
        }

        /// <summary>なければ null（警告を出さない）。</summary>
        public Mesh TryGet(string meshName)
        {
            foreach (var m in meshes)
                if (m.name == meshName) return m.mesh;
            return null;
        }

        public Mesh Get(string meshName)
        {
            if (_lookup == null || _lookup.Count != meshes.Count)
            {
                _lookup = new Dictionary<string, Mesh>();
                foreach (var m in meshes)
                    if (m.mesh != null) _lookup[m.name] = m.mesh;
            }
            if (_lookup.TryGetValue(meshName, out var mesh)) return mesh;
            Debug.LogWarning($"[WorldAssets] mesh not found: {meshName}");
            return null;
        }
    }
}
