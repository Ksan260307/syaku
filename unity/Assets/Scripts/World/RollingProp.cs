using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 押すと転がる物（どんぐり）。重さ（共通の重力）と、地面とのまさつ・転がりのてい抗で転がり、水には浮く。
    /// はじめは眠っていて（立っているどんぐりも、そのまま立っている）、しゃくとりむしの体に押されると動きだす。
    /// 遊べる場所の外や、地面の下へ行ってしまったら、もとの場所へもどる。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RollingProp : MonoBehaviour
    {
        public static readonly List<RollingProp> All = new List<RollingProp>();

        Rigidbody _rb;
        Collider _col;
        Vector3 _home;
        Quaternion _homeRot;
        float _radius;
        float _rolled;          // 転がった角度の合計（度）

        public Vector3 Home => _home;
        public Rigidbody Body => _rb;
        /// <summary>もとの場所から動いた距離。</summary>
        public float Moved => Vector3.Distance(transform.position, _home);
        /// <summary>転がった角度の合計（度）。</summary>
        public float RolledDegrees => _rolled;

        static PhysicsMaterial _material;

        /// <summary>転がる物にする（size は体の大きさ、radius はおよその半径）。</summary>
        public static RollingProp Make(GameObject go, Mesh mesh, float size, float radius)
        {
            go.layer = ShakuConst.RollingLayer;
            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = DetailMeshes.ForCollision(mesh);
            mc.convex = true;   // 転がる物は、つつみこむ形の当たり判定
            if (_material == null)
                _material = new PhysicsMaterial("Acorn")
                {
                    dynamicFriction = 0.55f,
                    staticFriction = 0.7f,
                    bounciness = 0.25f,
                    frictionCombine = PhysicsMaterialCombine.Average,
                    bounceCombine = PhysicsMaterialCombine.Average,
                };
            mc.sharedMaterial = _material;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f * size * size * size;   // 重さは体の大きさの 3 乗
            rb.linearDamping = 0.15f;               // 空気のてい抗
            rb.angularDamping = 1.1f;               // 転がりのてい抗（だんだん止まる）
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var rp = go.AddComponent<RollingProp>();
            rp._rb = rb;
            rp._col = mc;
            rp._home = go.transform.position;
            rp._homeRot = go.transform.rotation;
            rp._radius = radius;
            rb.Sleep();   // 押されるまでは、そのまま（立っているどんぐりも倒れない）
            return rp;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            Vector3 p = _rb.position;
            var area = Areas.Current;
            // 水に浮く（しずんだ深さに合わせた浮力と、水のてい抗）
            float wl = area.WaterLevelAt(p.x, p.z);
            if (wl > -100f && !_rb.IsSleeping())
            {
                float depth = wl - (p.y - _radius * 0.5f);
                if (depth > 0f)
                {
                    _rb.AddForce(Vector3.up * ShakuPhysics.BuoyantAccel(depth, _radius * 0.6f, 30f), ForceMode.Acceleration);
                    Vector3 flow = Creatures.WaterFlow(p, area);
                    _rb.linearVelocity = flow + (_rb.linearVelocity - flow) * Mathf.Exp(-2.5f * dt);
                }
            }
            if (!_rb.IsSleeping()) _rolled += _rb.angularVelocity.magnitude * Mathf.Rad2Deg * dt;
            // 遊べる場所の外・地面の下へ行ってしまったら、もとの場所へ
            if (!area.InPlayArea(p) || p.y < area.Height(p.x, p.z) - 2f || !ShakuPhysics.IsFinite(p)) ReturnHome();
        }

        public void ReturnHome()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _home;
            _rb.rotation = _homeRot;
            transform.SetPositionAndRotation(_home, _homeRot);
            _rb.Sleep();
        }
    }
}
