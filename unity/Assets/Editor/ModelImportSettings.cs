using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Shakutori.EditorTools
{
    /// <summary>
    /// Blender から出力した FBX の取り込み設定。
    /// 輪郭線（裏面法）を角のある形でも途切れさせないよう、位置ごとに平均した法線を UV3 に書き込む。
    /// しゃくとりむし本体は UV1〜3 に変形用データが入っているので触らない。
    /// </summary>
    public class ModelImportSettings : AssetPostprocessor
    {
        const string ModelFolder = "Assets/Art/Models";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.generateSecondaryUV = false;
            mi.weldVertices = true;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            if (assetPath.Contains("Inchworm")) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null) WriteSmoothNormals(mf.sharedMesh);
            }
        }

        static void WriteSmoothNormals(Mesh mesh)
        {
            var verts = mesh.vertices;
            var norms = mesh.normals;
            if (norms == null || norms.Length != verts.Length) return;
            var sum = new Dictionary<Vector3Int, Vector3>();
            const float q = 10000f;
            var keys = new Vector3Int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(verts[i].x * q), Mathf.RoundToInt(verts[i].y * q), Mathf.RoundToInt(verts[i].z * q));
                keys[i] = k;
                sum.TryGetValue(k, out var s);
                sum[k] = s + norms[i];
            }
            var smooth = new List<Vector3>(verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = sum[keys[i]];
                smooth.Add(n.sqrMagnitude > 1e-8f ? n.normalized : norms[i]);
            }
            mesh.SetUVs(3, smooth);
        }
    }
}
