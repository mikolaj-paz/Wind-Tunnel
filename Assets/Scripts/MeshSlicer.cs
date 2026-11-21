using System.Collections.Generic;
using UnityEngine;

public static class MeshSlicer
{
    public struct Section
    {
        public List<Vector2[]> Contours;
        public Vector3 Origin, AxisU, AxisV;
    }

    // shortcut: zakłada siatkę zamkniętą (watertight); otwarta siatka da urwane kontury.
    // up: kierunek "góry" obrazu przekroju (np. transform.up płaszczyzny); rzutowany na płaszczyznę.
    public static Section Slice(Mesh mesh, Matrix4x4 localToWorld, Plane plane, Vector3 up)
    {
        var n = plane.normal;
        var u = Vector3.Cross(up, n).normalized;
        var s = new Section
        {
            Contours = new List<Vector2[]>(),
            Origin = plane.ClosestPointOnPlane(localToWorld.GetColumn(3)),
            AxisU = u,
            AxisV = Vector3.Cross(n, u),
        };

        var verts = mesh.vertices;
        var tris = mesh.triangles;
        var world = new Vector3[verts.Length];
        var dist = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            world[i] = localToWorld.MultiplyPoint3x4(verts[i]);
            dist[i] = plane.GetDistanceToPoint(world[i]);
        }

        var links = new Dictionary<Vector3, List<Vector3>>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3? first = null;
            for (int k = 0; k < 3; k++)
            {
                int a = tris[i + k], b = tris[i + (k + 1) % 3];
                if (dist[a] >= 0 == dist[b] >= 0) continue;
                var p = Cut(world[a], dist[a], world[b], dist[b]);
                if (first == null) first = p;
                else if (!first.Value.Equals(p)) { Link(links, first.Value, p); Link(links, p, first.Value); }
            }
        }

        var visited = new HashSet<Vector3>();
        foreach (var start in links.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<Vector2>();
            for (Vector3? cur = start; cur != null && visited.Add(cur.Value);)
            {
                var d = cur.Value - s.Origin;
                loop.Add(new Vector2(Vector3.Dot(d, s.AxisU), Vector3.Dot(d, s.AxisV)));
                var next = (Vector3?)null;
                foreach (var p in links[cur.Value]) if (!visited.Contains(p)) { next = p; break; }
                cur = next;
            }
            if (loop.Count >= 3) s.Contours.Add(loop.ToArray());
        }
        return s;
    }

    // Obiekt do wyświetlenia w pełnej rozdzielczości (kontury w płaszczyźnie XY sceny 2D).
    public static GameObject CreateObject(Section s, Material material)
    {
        var go = new GameObject("Section");
        var col = go.AddComponent<PolygonCollider2D>();
        col.pathCount = s.Contours.Count;
        for (int i = 0; i < s.Contours.Count; i++) col.SetPath(i, s.Contours[i]);
        go.AddComponent<MeshFilter>().sharedMesh = col.CreateMesh(false, false);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    // Rasteryzacja dla symulacji: grid[x,y] == true -> komórka jest ciałem stałym.
    // area = obszar tunelu we współrzędnych przekroju; próbkowane są środki komórek (scanline, even-odd).
    public static bool[,] Rasterize(List<Vector2[]> contours, Rect area, int width, int height)
    {
        var grid = new bool[width, height];
        var xs = new List<float>();
        for (int y = 0; y < height; y++)
        {
            float py = area.yMin + (y + 0.5f) * area.height / height;
            xs.Clear();
            foreach (var c in contours)
                for (int i = 0; i < c.Length; i++)
                {
                    Vector2 a = c[i], b = c[(i + 1) % c.Length];
                    if (a.y > py != b.y > py) xs.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - area.xMin) / area.width * width - 0.5f));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((xs[k + 1] - area.xMin) / area.width * width - 0.5f));
                for (int x = x0; x <= x1; x++) grid[x, y] = true;
            }
        }
        return grid;
    }

    static Vector3 Cut(Vector3 a, float da, Vector3 b, float db)
    {
        if (da >= 0) { (a, b) = (b, a); (da, db) = (db, da); }
        return a + (b - a) * (da / (da - db));
    }

    static void Link(Dictionary<Vector3, List<Vector3>> links, Vector3 from, Vector3 to)
    {
        if (!links.TryGetValue(from, out var list)) links[from] = list = new List<Vector3>();
        list.Add(to);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/MeshSlicer Self-Check")]
    static void SelfCheck()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var s = Slice(cube, Matrix4x4.identity, new Plane(Vector3.forward, Vector3.zero), Vector3.up);
        Debug.Assert(s.Contours.Count == 1, $"contours: {s.Contours.Count}");
        int solid = 0;
        foreach (var cell in Rasterize(s.Contours, new Rect(-1, -1, 2, 2), 20, 20)) if (cell) solid++;
        Debug.Assert(solid == 100, $"solid cells: {solid}");
        Debug.Log("MeshSlicer self-check done");
    }
#endif
}
