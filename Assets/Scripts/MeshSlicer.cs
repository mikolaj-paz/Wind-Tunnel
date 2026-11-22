using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

// Przecina mesh płaszczyzną i zwraca zamknięte kontury przekroju.
public static class MeshSlicer
{
    // Clipper liczy na liczbach całkowitych: 6 miejsc po przecinku = 1 µm przy jednostkach w metrach.
    const int ClipperPrecision = 6;

    // Promień "domknięcia" przekroju (ułamek rozmiaru modelu): szczeliny węższe niż 2x tyle się zamykają.
    const float CloseRadius = 0.005f;

    // Spawanie punktów cięcia na siatce 1e-5 rozmiaru modelu (0.01 mm przy 1 m): skleja szwy, gdzie wierzchołki
    // w Blenderze nie zostały złączone (Merge by Distance), a i tak jest dużo drobniej niż siatka symulacji.
    const float WeldRatio = 1e-5f;

    public static Section Slice(Mesh mesh, Matrix4x4 localToWorld, Plane plane, Vector3 up) =>
        Slice(mesh.vertices, mesh.triangles, mesh.bounds, localToWorld, plane, up);

    public static Section Slice(Vector3[] verts, int[] tris, Bounds bounds, Matrix4x4 localToWorld, Plane plane, Vector3 up)
    {
        var section = Section.Create(plane, up, localToWorld.MultiplyPoint3x4(bounds.center));
        float size = localToWorld.MultiplyVector(bounds.size).magnitude;
        float weld = Mathf.Max(size * WeldRatio, 1e-9f);

        var segments = CutTriangles(ToWorld(verts, localToWorld), tris, plane, section);
        var chains = TraceChains(LinkSegments(segments, weld));
        var closed = CloseGaps(ToClipperPaths(chains, weld), size * CloseRadius);
        section.Contours = ToContours(closed);
        return section;
    }

    public static Vector3[] ToWorld(Vector3[] verts, Matrix4x4 localToWorld)
    {
        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) world[i] = localToWorld.MultiplyPoint3x4(verts[i]);
        return world;
    }

    public static List<(Vector2 from, Vector2 to)> CutTriangles(Vector3[] world, int[] tris, Plane plane, Section section)
    {
        var segments = new List<(Vector2, Vector2)>();
        for (int i = 0; i < tris.Length; i += 3)
            if (TryCutTriangle(world[tris[i]], world[tris[i + 1]], world[tris[i + 2]], plane, out var from, out var to))
                segments.Add((section.ToPlane(from), section.ToPlane(to)));
        return segments;
    }

    public static bool TryCutTriangle(Vector3 a, Vector3 b, Vector3 c, Plane plane, out Vector3 from, out Vector3 to)
    {
        bool ab = TryCutEdge(a, b, plane, out var cutAB);
        bool bc = TryCutEdge(b, c, plane, out var cutBC);
        bool ca = TryCutEdge(c, a, plane, out var cutCA);

        from = ab ? cutAB : cutBC;
        to = ca ? cutCA : cutBC;
        if (!(ab || bc || ca)) return false;

        var faceNormal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(Vector3.Cross(faceNormal, plane.normal), to - from) < 0) (from, to) = (to, from);
        return true;
    }

    public static bool TryCutEdge(Vector3 p, Vector3 q, Plane plane, out Vector3 cut)
    {
        float dp = plane.GetDistanceToPoint(p), dq = plane.GetDistanceToPoint(q);
        cut = default;
        if (dp >= 0 == dq >= 0) return false;

        if (dp >= 0) { (p, q) = (q, p); (dp, dq) = (dq, dp); }
        cut = p + (q - p) * (dp / (dp - dq));
        return true;
    }

    public static Vector2Int WeldKey(Vector2 p, float weld) => Vector2Int.RoundToInt(p / weld);

    public static Dictionary<Vector2Int, Vector2Int> LinkSegments(List<(Vector2 from, Vector2 to)> segments, float weld)
    {
        var next = new Dictionary<Vector2Int, Vector2Int>();
        foreach (var (from, to) in segments)
        {
            Vector2Int k0 = WeldKey(from, weld), k1 = WeldKey(to, weld);
            if (k0 != k1) next[k0] = k1;
        }
        return next;
    }

    public static List<List<Vector2Int>> TraceChains(Dictionary<Vector2Int, Vector2Int> next)
    {
        var chains = new List<List<Vector2Int>>();
        var visited = new HashSet<Vector2Int>();
        foreach (var start in ChainStarts(next))
        {
            if (visited.Contains(start)) continue;
            var chain = TraceChain(start, next, visited);
            if (chain.Count >= 2) chains.Add(chain);
        }
        return chains;
    }

    public static List<Vector2Int> ChainStarts(Dictionary<Vector2Int, Vector2Int> next)
    {
        var heads = new HashSet<Vector2Int>(next.Keys);
        heads.ExceptWith(next.Values);
        var starts = new List<Vector2Int>(heads);
        starts.AddRange(next.Keys);
        return starts;
    }

    public static List<Vector2Int> TraceChain(Vector2Int start, Dictionary<Vector2Int, Vector2Int> next, HashSet<Vector2Int> visited)
    {
        var chain = new List<Vector2Int>();
        var cur = start;
        bool more = true;
        while (more && visited.Add(cur))
        {
            chain.Add(cur);
            more = next.TryGetValue(cur, out cur);
        }
        if (more) chain.Add(cur);
        return chain;
    }

    public static PathsD ToClipperPaths(List<List<Vector2Int>> chains, float weld)
    {
        var paths = new PathsD();
        foreach (var chain in chains) paths.Add(new PathD(chain.ConvertAll(k => new PointD(k.x * weld, k.y * weld))));
        return paths;
    }

    public static PathsD CloseGaps(PathsD lines, double r)
    {
        var thick = Clipper.InflatePaths(lines, r, JoinType.Round, EndType.Round, 2, ClipperPrecision);
        return Clipper.InflatePaths(FillHoles(thick), -r, JoinType.Round, EndType.Polygon, 2, ClipperPrecision);
    }

    public static PathsD FillHoles(PathsD paths)
    {
        var union = Clipper.Union(paths, null, FillRule.NonZero, ClipperPrecision);
        union.RemoveAll(path => Clipper.Area(path) <= 0);
        return Clipper.Union(union, null, FillRule.NonZero, ClipperPrecision);
    }

    public static List<Vector2[]> ToContours(PathsD paths) =>
        paths.ConvertAll(path => path.ConvertAll(p => new Vector2((float)p.x, (float)p.y)).ToArray());
}
