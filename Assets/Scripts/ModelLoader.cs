using System.Collections.Generic;
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
            await LoadIntoScene(gltf, path, root.transform);
            var parts = CollectParts(root.transform);
            if (parts.Count == 0) throw new IOException("Plik nie zawiera meshy: " + path);
            return Combine(parts, Path.GetFileNameWithoutExtension(path));
        }
        finally
        {
            Object.Destroy(root);
            gltf.Dispose();
        }
    }

    static async Task LoadIntoScene(GltfImport gltf, string path, Transform root)
    {
        if (!await gltf.LoadFile(path, new System.Uri(path)) || !await gltf.InstantiateMainSceneAsync(root))
            throw new IOException("Nie udało się wczytać: " + path);
    }

    static List<CombineInstance> CollectParts(Transform root)
    {
        var toRoot = root.worldToLocalMatrix;
        var parts = new List<CombineInstance>();
        foreach (var (mesh, t) in MeshSources(root))
            for (int s = 0; s < mesh.subMeshCount; s++)
                parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = toRoot * t.localToWorldMatrix });
        return parts;
    }

    static List<(Mesh mesh, Transform t)> MeshSources(Transform root)
    {
        var sources = new List<(Mesh, Transform)>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) sources.Add((mf.sharedMesh, mf.transform));
        // Obiekty z armaturą lub shape keys glTFast tworzy jako SkinnedMeshRenderer, bez MeshFilter.
        // shortcut: bierze mesh w pozie spoczynkowej (bind pose), bez kości i shape keys; gdyby była potrzebna poza z pliku, użyć BakeMesh.
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) sources.Add((smr.sharedMesh, smr.transform));
        return sources;
    }

    static Mesh Combine(List<CombineInstance> parts, string name)
    {
        var mesh = new Mesh { name = name };
        if (VertexCount(parts) > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32; // duże modele
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Liczone per submesh, więc może zawyżyć - wtedy co najwyżej UInt32 włączy się wcześniej, co jest bezpieczne.
    static long VertexCount(List<CombineInstance> parts)
    {
        long count = 0;
        foreach (var p in parts) count += p.mesh.vertexCount;
        return count;
    }
}
