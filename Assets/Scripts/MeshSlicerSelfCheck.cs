#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// Szybki test MeshSlicer + SectionRasterizer z menu edytora.
public static class MeshSlicerSelfCheck
{
    [UnityEditor.MenuItem("Tools/MeshSlicer Self-Check")]
    static void Run()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        CheckCube(cube);
        CheckOverlappingCubes(cube);
        CheckHollowModel(cube);
        CheckOpenWalls();
        Debug.Log("MeshSlicer self-check done");
    }

    static void CheckCube(Mesh cube)
    {
        var (contours, solid) = SliceAndCount(cube);
        Debug.Assert(contours == 1 && solid == 100, $"cube: contours {contours}, solid cells {solid}");
    }

    static void CheckOverlappingCubes(Mesh cube)
    {
        var two = Combine((cube, Matrix4x4.identity), (cube, Matrix4x4.Translate(new Vector3(0.5f, 0f, 0f))));
        var (contours, solid) = SliceAndCount(two);
        Debug.Assert(contours == 1 && solid == 150, $"union: contours {contours}, solid cells {solid}");
        Object.DestroyImmediate(two);
    }

    static void CheckHollowModel(Mesh cube)
    {
        var flipped = FlipFaces(cube);
        var hollow = Combine((cube, Matrix4x4.Scale(Vector3.one * 1.6f)), (flipped, Matrix4x4.Scale(Vector3.one * 0.8f)));
        var (contours, solid) = SliceAndCount(hollow);
        Debug.Assert(contours == 1 && solid == 256, $"hollow: contours {contours}, solid cells {solid}");
        Object.DestroyImmediate(hollow);
        Object.DestroyImmediate(flipped);
    }

    static void CheckOpenWalls()
    {
        var open = OpenWalls();
        var (contours, solid) = SliceAndCount(open);
        Debug.Assert(contours == 1 && solid == 100, $"open walls: contours {contours}, solid cells {solid}");
        Object.DestroyImmediate(open);
    }

    static (int contours, int solid) SliceAndCount(Mesh mesh)
    {
        var s = MeshSlicer.Slice(mesh, Matrix4x4.identity, new Plane(Vector3.forward, Vector3.zero), Vector3.up);
        int solid = 0;
        foreach (var cell in SectionRasterizer.Rasterize(s.Contours, new Rect(-1, -1, 2, 2), 20, 20)) if (cell) solid++;
        return (s.Contours.Count, solid);
    }

    static Mesh Combine(params (Mesh mesh, Matrix4x4 transform)[] parts)
    {
        var combined = new Mesh();
        combined.CombineMeshes(System.Array.ConvertAll(parts, p => new CombineInstance { mesh = p.mesh, transform = p.transform }));
        return combined;
    }

    static Mesh FlipFaces(Mesh mesh)
    {
        var flipped = Object.Instantiate(mesh);
        var t = flipped.triangles;
        for (int i = 0; i < t.Length; i += 3) (t[i], t[i + 1]) = (t[i + 1], t[i]);
        flipped.triangles = t;
        return flipped;
    }

    static Mesh OpenWalls()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int w = 0; w < 4; w++)
        {
            var q = Quaternion.Euler(0f, 0f, 90f * w);
            int b = verts.Count;
            foreach (var (y, z) in new[] { (-0.495f, -0.5f), (0.495f, -0.5f), (0.495f, 0.5f), (-0.495f, 0.5f) })
                verts.Add(q * new Vector3(0.5f, y, z));
            tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        return mesh;
    }
}
#endif
