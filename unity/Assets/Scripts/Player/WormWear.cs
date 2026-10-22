using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// きせかえの小物（ぼうし・めがね）を、曲がった体の頭にのせる。
    /// 小物のメッシュは、しゃくとりむしのメッシュと同じ向き（Blender で x = 右、y = 頭の方、z = せなか）で、頭のまん中が原点。
    /// 毎フレーム、頭のまん中と向き（InchwormBody.HeadCenter / HeadForward / HeadUp）に合わせて置きなおす。
    /// ぴょこんげは、頭が動くと、ばねのようにぴょこぴょこゆれる。
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class WormWear : MonoBehaviour
    {
        public InchwormBody body;
        public WorldAssets assets;
        public Material material;

        /// <summary>ぴょこんげの付け根（Unity のメッシュの座標。Blender の (0, -0.004, 0.058)）。</summary>
        public static readonly Vector3 HairRoot = new Vector3(0f, 0.058f, 0.004f);

        Slot _hat, _glasses;
        // あいた口（頭の先の口の所に、小さな暗いだ円。あくほど大きく）
        GameObject _mouthGo;
        MeshRenderer _mouthMr;
        static Mesh s_mouthMesh;
        /// <summary>口のあきぐあい（テスト用。0 なら口はかくれる）。</summary>
        public float MouthShown { get; private set; }
        Renderer _bodyRenderer;
        Vector3 _prevPivot, _prevVel;
        bool _hasPrev;
        Vector2 _swing, _swingVel;   // ぴょこんげのかたむき（頭の右・前のまわり、度）と、その速さ

        class Slot
        {
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public SkinDef def;
        }

        /// <summary>いまかぶっているぼうし・かけているめがね（テスト用）。</summary>
        public SkinDef Hat => _hat != null && _hat.go.activeSelf ? _hat.def : null;
        public SkinDef Glasses => _glasses != null && _glasses.go.activeSelf ? _glasses.def : null;
        public Transform HatTransform => _hat != null ? _hat.go.transform : null;
        public Transform GlassesTransform => _glasses != null ? _glasses.go.transform : null;
        public Renderer HatRenderer => _hat != null ? _hat.mr : null;
        public Renderer GlassesRenderer => _glasses != null ? _glasses.mr : null;
        /// <summary>ぴょこんげのかたむき（度）。</summary>
        public float HairSwing => _swing.magnitude;

        Slot MakeSlot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = body != null ? body.gameObject.layer : gameObject.layer;
            var s = new Slot { go = go, mf = go.AddComponent<MeshFilter>(), mr = go.AddComponent<MeshRenderer>() };
            s.mr.shadowCastingMode = ShadowCastingMode.On;
            s.mr.receiveShadows = true;
            go.SetActive(false);
            return s;
        }

        /// <summary>かぶるぼうしと、かけるめがねを決める（null で外す）。同じなら何もしない。</summary>
        public void Set(SkinDef hat, SkinDef glasses)
        {
            _hat ??= MakeSlot("WearHat");
            _glasses ??= MakeSlot("WearGlasses");
            Put(_hat, hat);
            Put(_glasses, glasses);
        }

        void Put(Slot slot, SkinDef def)
        {
            if (slot.def == def && (def == null) == !slot.go.activeSelf) return;
            slot.def = def;
            Mesh mesh = def != null && assets != null && !string.IsNullOrEmpty(def.mesh) ? assets.TryGet(def.mesh) : null;
            Material mat = material != null ? material : assets != null ? assets.creature : null;
            if (mesh == null || mat == null)
            {
                slot.go.SetActive(false);
                return;
            }
            slot.mf.sharedMesh = mesh;
            slot.mr.sharedMaterial = mat;
            slot.go.SetActive(true);
            if (def.bouncy) _hasPrev = false;
            Place(0f);
        }

        void LateUpdate() => Place(Time.deltaTime);

        /// <summary>頭のまん中と向きに合わせて置く（dt はぴょこんげのゆれの時間）。</summary>
        public void Place(float dt)
        {
            if (body == null) return;
            if (_bodyRenderer == null) _bodyRenderer = body.GetComponent<Renderer>();
            bool show = _bodyRenderer == null || (_bodyRenderer.enabled && _bodyRenderer.gameObject.activeInHierarchy);
            Vector3 fwd = body.HeadForward, up = body.HeadUp;
            if (!ShakuPhysics.IsFinite(body.HeadCenter) || fwd.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f) show = false;
            Quaternion rot = show ? Quaternion.LookRotation(-fwd, up) : Quaternion.identity;
            PlaceSlot(_hat, show, rot, dt);
            PlaceSlot(_glasses, show, rot, dt);
            PlaceMouth(show);
        }

        /// <summary>口：しゃくとりむしの口のあきぐあい（InchwormController.MouthOpen）に合わせて、口の所に暗いだ円をえがく。</summary>
        void PlaceMouth(bool show)
        {
            var worm = InchwormController.Instance;
            float open = worm != null ? worm.MouthOpen : 0f;
            MouthShown = show ? open : 0f;
            if (_mouthGo == null)
            {
                if (open < 0.04f) return;
                Material mat = material != null ? material : assets != null ? assets.creature : null;
                if (mat == null) return;
                _mouthGo = new GameObject("WearMouth");
                _mouthGo.transform.SetParent(transform, false);
                _mouthGo.layer = body.gameObject.layer;
                _mouthGo.AddComponent<MeshFilter>().sharedMesh = MouthMesh();
                _mouthMr = _mouthGo.AddComponent<MeshRenderer>();
                _mouthMr.sharedMaterial = mat;
                _mouthMr.shadowCastingMode = ShadowCastingMode.Off;
            }
            bool on = show && open > 0.04f;
            if (_mouthMr.enabled != on) _mouthMr.enabled = on;
            if (!on) return;
            float L = body.Scale;
            Vector3 n = body.MouthNormal;
            Vector3 up = Vector3.ProjectOnPlane(body.HeadUp, n);
            if (up.sqrMagnitude < 1e-4f) up = body.HeadUp;
            _mouthGo.transform.SetPositionAndRotation(body.MouthCenter + n * (0.0025f * L), Quaternion.LookRotation(n, up));
            _mouthGo.transform.localScale = new Vector3(0.012f * L * (0.75f + 0.35f * open), 0.011f * L * open, 1f);
        }

        /// <summary>口のだ円（+Z を向いた面。まん中ほど暗く、下がわに少し赤い舌）。</summary>
        static Mesh MouthMesh()
        {
            if (s_mouthMesh != null) return s_mouthMesh;
            const int seg = 20;
            var v = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            var c = new System.Collections.Generic.List<Color> { new Color(0.16f, 0.07f, 0.06f) };
            var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
                c.Add(Mathf.Sin(a) < -0.4f ? new Color(0.72f, 0.3f, 0.32f) : new Color(0.3f, 0.13f, 0.1f));
            }
            for (int i = 0; i < seg; i++)
            {
                t.Add(0);
                t.Add(1 + (i + 1) % seg);
                t.Add(1 + i);
            }
            s_mouthMesh = new Mesh { name = "WormMouth" };
            s_mouthMesh.SetVertices(v);
            s_mouthMesh.SetColors(c);
            s_mouthMesh.SetTriangles(t, 0);
            var nrm = new Vector3[v.Count];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.forward;
            s_mouthMesh.normals = nrm;
            s_mouthMesh.RecalculateBounds();
            return s_mouthMesh;
        }

        void PlaceSlot(Slot slot, bool show, Quaternion rot, float dt)
        {
            if (slot == null || slot.def == null) return;
            if (slot.mr.enabled != show) slot.mr.enabled = show;
            if (!show) return;
            float L = body.Scale;
            Quaternion r = rot;
            Vector3 pos = body.HeadCenter;
            if (slot.def.bouncy)
            {
                Quaternion q = Bounce(rot, body.HeadCenter + rot * (HairRoot * L), dt);
                r = rot * q;
                // 付け根のまわりに回す
                pos = body.HeadCenter + rot * (HairRoot * L) - r * (HairRoot * L);
            }
            slot.go.transform.SetPositionAndRotation(pos, r);
            slot.go.transform.localScale = Vector3.one * L;
        }

        /// <summary>ぴょこんげのゆれ：付け根が加速すると、毛先は体のうしろへ残り、ばねでもどる（行き過ぎて、ぴょこぴょこ）。</summary>
        Quaternion Bounce(Quaternion head, Vector3 pivot, float dt)
        {
            if (dt <= 0f || !_hasPrev)
            {
                _prevPivot = pivot;
                _prevVel = Vector3.zero;
                _hasPrev = true;
                return Quaternion.Euler(_swing.y, 0f, _swing.x);
            }
            dt = Mathf.Min(dt, 0.05f);
            Vector3 vel = (pivot - _prevPivot) / dt;
            Vector3 acc = (vel - _prevVel) / dt;
            _prevPivot = pivot;
            _prevVel = vel;
            if (!ShakuPhysics.IsFinite(acc)) acc = Vector3.zero;
            acc = Vector3.ClampMagnitude(acc, 60f);
            // 頭から見た加速（右・前）。毛先は、加速と反対へかたむく
            Vector3 local = Quaternion.Inverse(head) * acc;
            // （1 体長/秒² の加速で、およそ 3 度かたむく。ゆるやかに、ゆらゆら）
            Vector2 push = new Vector2(local.x, local.z) * -800f + new Vector2(Mathf.Sin(Time.time * 2.1f), Mathf.Sin(Time.time * 1.7f + 1f)) * 500f;
            const float k = 260f, c = 9f;
            int n = Mathf.CeilToInt(dt / 0.008f);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = push - _swing * k - _swingVel * c;
                _swingVel += a * h;
                _swing += _swingVel * h;
            }
            _swing = Vector2.ClampMagnitude(_swing, 40f);
            if (!ShakuPhysics.IsFinite(new Vector3(_swing.x, _swing.y, 0f))) _swing = _swingVel = Vector2.zero;
            // 右へのかたむきは前のまわり、前へのかたむきは右のまわり（メッシュの座標：x = 左、z = うしろ）
            return Quaternion.Euler(_swing.y, 0f, _swing.x);
        }
    }
}
