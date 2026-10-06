using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>森のしずく（収集物）と名所（発見ポイント）の管理。いまいるエリアのものを扱う。</summary>
    public class Collectibles : MonoBehaviour
    {
        public WorldAssets assets;
        public float pickupRadius = 0.7f;
        public float dropScale = 0.34f;

        public event Action<int, Vector3> DropCollected;      // このエリアで何個目か, 位置
        public event Action<LandmarkDef> LandmarkDiscovered;

        public AreaLayout Area { get; private set; } = Areas.Forest;
        public int TotalDrops => _drops.Count;
        public int CollectedDrops { get; private set; }
        public int TotalPlaces => Area.Landmarks.Count;
        public int DiscoveredPlaces { get; private set; }
        public bool Active { get; set; }

        /// <summary>すべてのエリアの合計。</summary>
        public static int AllCollectedDrops => SaveSystem.Data.drops.Count;
        public static int AllDiscoveredPlaces => SaveSystem.Data.places.Count;

        class Drop
        {
            public int id;        // 保存用の ID（エリアごとにずらしてある）
            public Vector3 basePos;
            public Transform tr;
            public bool taken;
            public float phase;
        }

        readonly List<Drop> _drops = new List<Drop>();
        readonly HashSet<int> _places = new HashSet<int>();
        Transform _root;

        public IReadOnlyCollection<int> DiscoveredIds => _places;

        /// <summary>このエリアの i 番目のしずく。</summary>
        public bool IsDropTaken(int index) => index >= 0 && index < _drops.Count && _drops[index].taken;
        public Vector3 DropPosition(int index) => _drops[index].basePos;
        public int DropSaveId(int index) => _drops[index].id;

        public void Build(List<Vector3> points) => Build(points, Areas.Current);

        public void Build(List<Vector3> points, AreaLayout area)
        {
            Area = area;
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root.gameObject);
                else DestroyImmediate(_root.gameObject);
            }
            _root = new GameObject("Dewdrops").transform;
            _drops.Clear();
            Mesh mesh = assets.Get("Dewdrop");
            for (int i = 0; i < points.Count; i++)
            {
                var go = new GameObject("Dewdrop_" + i);
                go.transform.SetParent(_root, false);
                go.transform.position = points[i];
                go.transform.localScale = Vector3.one * dropScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.dewdrop;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _drops.Add(new Drop { id = area.DropIdOffset + i, basePos = points[i], tr = go.transform, phase = i * 1.37f });
            }
            ApplySave();
        }

        public void ApplySave()
        {
            var save = SaveSystem.Data;
            CollectedDrops = 0;
            foreach (var d in _drops)
            {
                d.taken = save.drops.Contains(d.id);
                d.tr.gameObject.SetActive(!d.taken);
                if (d.taken) CollectedDrops++;
            }
            _places.Clear();
            foreach (int p in save.places) _places.Add(p);
            DiscoveredPlaces = CountPlaces(Area);
        }

        int CountPlaces(AreaLayout area)
        {
            int n = 0;
            foreach (var lm in area.Landmarks)
                if (_places.Contains(lm.id)) n++;
            return n;
        }

        public bool IsDiscovered(int id) => _places.Contains(id);

        /// <summary>そのエリアで集めたしずくの数（保存データから）。</summary>
        public static int CollectedIn(AreaLayout area)
        {
            int n = 0;
            foreach (int id in SaveSystem.Data.drops)
                if (Areas.AreaOfDrop(id) == area) n++;
            return n;
        }

        public static int DiscoveredIn(AreaLayout area)
        {
            int n = 0;
            foreach (var lm in area.Landmarks)
                if (SaveSystem.Data.places.Contains(lm.id)) n++;
            return n;
        }

        public static bool IsAreaComplete(AreaLayout area) => CollectedIn(area) >= area.DropCount && DiscoveredIn(area) >= area.Landmarks.Count;

        void Update()
        {
            float t = Time.time;
            foreach (var d in _drops)
            {
                if (d.taken) continue;
                float bob = Mathf.Sin(t * 2f + d.phase) * 0.05f;
                d.tr.position = d.basePos + Vector3.up * (0.04f + bob);
                d.tr.rotation = Quaternion.Euler(0f, t * 40f + d.phase * 30f, 0f);
                float pulse = 1f + Mathf.Sin(t * 3.1f + d.phase) * 0.06f;
                d.tr.localScale = Vector3.one * dropScale * pulse;
            }
            if (!Active) return;
            var worm = InchwormController.Instance;
            if (worm == null) return;
            Vector3 head = worm.HeadPosition;
            Vector3 mid = worm.CenterPosition;
            float r2 = pickupRadius * pickupRadius;
            foreach (var d in _drops)
            {
                if (d.taken) continue;
                Vector3 c = d.basePos + Vector3.up * 0.12f;
                if ((head - c).sqrMagnitude < r2 || (mid - c).sqrMagnitude < r2 * 0.8f)
                {
                    d.taken = true;
                    d.tr.gameObject.SetActive(false);
                    CollectedDrops++;
                    if (!SaveSystem.Data.drops.Contains(d.id)) SaveSystem.Data.drops.Add(d.id);
                    SaveSystem.Save();
                    DropCollected?.Invoke(CollectedDrops, c);
                }
            }
            foreach (var lm in Area.Landmarks)
            {
                if (_places.Contains(lm.id)) continue;
                if (IsInside(lm, head)) Discover(lm);
            }
        }

        public static bool IsInside(LandmarkDef lm, Vector3 p)
        {
            if (lm.useCapsule)
            {
                Vector3 ab = lm.capsuleB - lm.capsuleA;
                float t = Mathf.Clamp01(Vector3.Dot(p - lm.capsuleA, ab) / ab.sqrMagnitude);
                return Vector3.Distance(p, lm.capsuleA + ab * t) < lm.capsuleRadius;
            }
            Vector2 xz = new Vector2(p.x, p.z);
            if (Vector2.Distance(xz, lm.position) > lm.radius) return false;
            if (lm.minHeightAboveGround > 0f)
            {
                float g = Areas.Get(lm.areaId).Height(lm.position.x, lm.position.y);
                return p.y > g + lm.minHeightAboveGround;
            }
            return true;
        }

        public void Discover(LandmarkDef lm)
        {
            if (_places.Contains(lm.id)) return;
            _places.Add(lm.id);
            DiscoveredPlaces = CountPlaces(Area);
            if (!SaveSystem.Data.places.Contains(lm.id)) SaveSystem.Data.places.Add(lm.id);
            SaveSystem.Save();
            LandmarkDiscovered?.Invoke(lm);
        }

        /// <summary>まだ取っていない中で一番近いしずく（ヒント用）。</summary>
        public bool NearestRemaining(Vector3 from, out Vector3 pos, out float dist)
        {
            pos = Vector3.zero;
            dist = float.MaxValue;
            foreach (var d in _drops)
            {
                if (d.taken) continue;
                float dd = Vector3.Distance(from, d.basePos);
                if (dd < dist) { dist = dd; pos = d.basePos; }
            }
            return dist < float.MaxValue;
        }

        public IEnumerable<Vector3> RemainingDrops()
        {
            foreach (var d in _drops)
                if (!d.taken) yield return d.basePos;
        }
    }
}
