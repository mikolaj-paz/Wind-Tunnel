using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Rendering;

// Wczytuje .glb/.gltf w runtime (eksport z Blendera: File > Export > glTF 2.0) i scala wszystkie części w jeden Mesh.
// Wymaga define GLTFAST_KEEP_MESH_DATA (Player Settings), inaczej glTFast robi meshe nieczytelne z CPU, a MeshSlicer czyta wierzchołki.
public static class ModelLoader
{
    public static async Task<Mesh> Load(string path)
    {
        path = Path.GetFullPath(path);
        var gltf = new GltfImport();
        var root = new GameObject("GltfImport");
        root.SetActive(false); // żeby tymczasowa kopia nie mignęła w scenie
        try
        {
            // URI musi być bezwzględne: .gltf szuka obok siebie plików .bin i tekstur.
            if (!await gltf.LoadFile(path, new System.Uri(path)) || !await gltf.InstantiateMainSceneAsync(root.transform))
                throw new IOException("Nie udało się wczytać: " + path);

            var toRoot = root.transform.worldToLocalMatrix;
            var parts = new System.Collections.Generic.List<CombineInstance>();
            long verts = 0;
            var sources = new System.Collections.Generic.List<(Mesh mesh, Transform t)>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) sources.Add((mf.sharedMesh, mf.transform));
            // Obiekty z armaturą lub shape keys glTFast tworzy jako SkinnedMeshRenderer, bez MeshFilter.
            // shortcut: bierze mesh w pozie spoczynkowej (bind pose), bez kości i shape keys; gdyby była potrzebna poza z pliku, użyć BakeMesh.
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sources.Add((smr.sharedMesh, smr.transform));
            foreach (var (m, t) in sources)
            {
                if (m == null) continue;
                verts += m.vertexCount;
                for (int s = 0; s < m.subMeshCount; s++)
                    parts.Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = toRoot * t.localToWorldMatrix });
            }
            if (parts.Count == 0) throw new IOException("Plik nie zawiera meshy: " + path);

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (verts > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32; // duże modele
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }
        finally
        {
            Object.Destroy(root);
            gltf.Dispose(); // zwalnia meshe/tekstury glTFast; nasz scalony mesh jest osobną kopią
        }
    }
}
