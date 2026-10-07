using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shakutori
{
    /// <summary>
    /// 水面（池・川）が画面に入っているときだけ、カメラの「深さの写し」と「色の写し」を作る。
    /// 水のシェーダーは、この 2 つで水の深さの色・岸ぎわの泡・水中のゆがみを描く。ほかには使わない。
    /// 写しがいらないフレームは、深さだけを先に描く処理（WebGL で MSAA のときは、全部をもう一度描く）と、
    /// 色を写す処理がまるごとなくなる。画面に映るかどうかだけで決める見た目の調整で、ゲームの計算には関係しない。
    /// </summary>
    public static class WaterView
    {
        static readonly List<Renderer> s_water = new List<Renderer>();
        static readonly Plane[] s_planes = new Plane[6];

        /// <summary>直前に描いたフレームで、写しを作ったか（テスト用）。</summary>
        public static bool CopiesOn { get; private set; } = true;
        public static int WaterCount => s_water.Count;

        /// <summary>水面のレンダラーを登録する（エリアを作るときに呼ぶ）。</summary>
        public static void Register(Renderer r)
        {
            if (r != null && !s_water.Contains(r)) s_water.Add(r);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        // カメラの描画がはじまる直前（URP がカメラの設定を読む前）に決める。カメラはこのフレームの最終の位置
        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || cam != Camera.main) return;
            Apply(cam);
        }

        /// <summary>いまのカメラの視野で、写しを作るかを決めて、カメラに設定する。</summary>
        public static void Apply(Camera cam)
        {
            bool need = InView(cam);
            var data = cam.GetUniversalAdditionalCameraData();
            var opt = need ? CameraOverrideOption.On : CameraOverrideOption.Off;
            if (data.requiresDepthOption != opt) data.requiresDepthOption = opt;
            if (data.requiresColorOption != opt) data.requiresColorOption = opt;
            CopiesOn = need;
        }

        /// <summary>水面のどれかが、カメラの視野に入っているか（少し広めに見る）。</summary>
        public static bool InView(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, s_planes);
            for (int i = s_water.Count - 1; i >= 0; i--)
            {
                var r = s_water[i];
                if (r == null) { s_water.RemoveAt(i); continue; }
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                b.Expand(2f);
                if (GeometryUtility.TestPlanesAABB(s_planes, b)) return true;
            }
            return false;
        }
    }
}
