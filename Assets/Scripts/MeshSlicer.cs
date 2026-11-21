using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

public static class MeshSlicer
{
    public struct Section
    {
        public List<Vector2[]> Contours;
        public Vector3 Origin, AxisU, AxisV;
    }

    // Clipper liczy na liczbach całkowitych: 6 miejsc po przecinku = 1 µm przy jednostkach w metrach.
    const int ClipperPrecision = 6;

    // Promień "domknięcia" przekroju (ułamek rozmiaru modelu): szczeliny węższe niż 2x tyle się zamykają.
    const float CloseRadius = 0.005f;

    // up: kierunek "góry" obrazu przekroju (np. transform.up płaszczyzny); rzutowany na płaszczyznę.
    public static Section Slice(Mesh mesh, Matrix4x4 localToWorld, Plane plane, Vector3 up) =>
        Slice(mesh.vertices, mesh.triangles, mesh.bounds, localToWorld, plane, up);

    // Wersja na gotowych tablicach: mesh.vertices/triangles kopiują cały mesh przy każdym wywołaniu, więc wołający może je trzymać.
    public static Section Slice(Vector3[] verts, int[] tris, Bounds bounds, Matrix4x4 localToWorld, Plane plane, Vector3 up)
    {
        var n = plane.normal;
        var u = Vector3.Cross(up, n).normalized;
        var s = new Section
        {
            Contours = new List<Vector2[]>(),
            // Środek geometrii, nie pivot: importowane modele mają pivot często daleko od bryły, a skala wyświetlania rośnie wokół Origin.
            Origin = plane.ClosestPointOnPlane(localToWorld.MultiplyPoint3x4(bounds.center)),
            AxisU = u,
            AxisV = Vector3.Cross(n, u),
        };

        var world = new Vector3[verts.Length];
        var dist = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            world[i] = localToWorld.MultiplyPoint3x4(verts[i]);
            dist[i] = plane.GetDistanceToPoint(world[i]);
        }

        // Spawanie punktów cięcia na siatce 1e-5 rozmiaru modelu (0.01 mm przy 1 m): skleja szwy, gdzie wierzchołki
        // w Blenderze nie zostały złączone (Merge by Distance), a i tak jest dużo drobniej niż siatka symulacji.
        float size = localToWorld.MultiplyVector(bounds.size).magnitude;
        float weld = Mathf.Max(size * 1e-5f, 1e-9f);
        Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p / weld);
        Vector2 ToPlane(Vector3 p) => new(Vector3.Dot(p - s.Origin, s.AxisU), Vector3.Dot(p - s.Origin, s.AxisV));

        // Odcinki skierowane zgodnie z orientacją trójkąta, żeby sąsiednie odcinki łączyły się w łańcuchy (koniec -> początek).
        var next = new Dictionary<Vector3Int, Vector3Int>();
        var pos = new Dictionary<Vector3Int, Vector2>();
        var cut = new Vector3[2];
        for (int i = 0; i < tris.Length; i += 3)
        {
            int c = 0;
            for (int k = 0; k < 3 && c < 2; k++)
            {
                int a = tris[i + k], b = tris[i + (k + 1) % 3];
                if (dist[a] >= 0 != dist[b] >= 0) cut[c++] = Cut(world[a], dist[a], world[b], dist[b]);
            }
            if (c < 2) continue;
            Vector3 w0 = world[tris[i]], faceN = Vector3.Cross(world[tris[i + 1]] - w0, world[tris[i + 2]] - w0);
            if (Vector3.Dot(Vector3.Cross(faceN, n), cut[1] - cut[0]) < 0) (cut[0], cut[1]) = (cut[1], cut[0]);
            Vector3Int k0 = Key(cut[0]), k1 = Key(cut[1]);
            if (k0 == k1) continue;
            next[k0] = k1; // przy rozgałęzieniu wygrywa ostatni odcinek; reszta to osobny łańcuch, sklejany niżej pogrubieniem
            pos[k0] = ToPlane(cut[0]);
            pos[k1] = ToPlane(cut[1]);
        }

        // Najpierw początki otwartych łańcuchów, żeby nie zaczynać w ich środku; potem zamknięte pętle.
        var heads = new HashSet<Vector3Int>(next.Keys);
        heads.ExceptWith(next.Values);
        var starts = new List<Vector3Int>(heads);
        starts.AddRange(next.Keys);

        // Łańcuchy jako otwarte linie (nigdy nie domykane prostą - to dawało "skoki" przez cały model).
        var lines = new PathsD();
        var visited = new HashSet<Vector3Int>();
        foreach (var start in starts)
        {
            if (visited.Contains(start)) continue;
            var line = new PathD();
            var cur = start;
            bool more = true;
            while (more && visited.Add(cur))
            {
                line.Add(new PointD(pos[cur].x, pos[cur].y));
                more = next.TryGetValue(cur, out cur);
            }
            if (more) line.Add(new PointD(pos[cur].x, pos[cur].y)); // dojście do odwiedzonego punktu: domknięcie pętli albo styk z innym łańcuchem
            if (line.Count >= 2) lines.Add(line);
        }

        // Domknięcie morfologiczne: pogrub linie o r (sklejają się kawałki i szczeliny < 2r), zsumuj, zostaw tylko kontury
        // zewnętrzne (dziura w przekroju 2D jest zamknięta, przepływ jej nie widzi; przy pustych modelach to artefakt)
        // i zwęź o r z powrotem do oryginalnego obrysu. Pojedyncze luźne linie (np. podłoga sceny) znikają.
        double r = size * CloseRadius;
        var thick = Clipper.InflatePaths(lines, r, JoinType.Round, EndType.Round, 2, ClipperPrecision);
        // Dziury usuwane dopiero po sumie: pierścień z kilku osobnych łańcuchów ma dziurę dopiero po zsumowaniu.
        var filled = Clipper.Union(thick, null, FillRule.NonZero, ClipperPrecision);
        filled.RemoveAll(path => Clipper.Area(path) <= 0);
        filled = Clipper.Union(filled, null, FillRule.NonZero, ClipperPrecision); // wchłania wyspy, które leżały w usuniętych dziurach
        foreach (var path in Clipper.InflatePaths(filled, -r, JoinType.Round, EndType.Polygon, 2, ClipperPrecision))
        {
            var arr = new Vector2[path.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = new Vector2((float)path[i].x, (float)path[i].y);
            s.Contours.Add(arr);
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

        // Dwa nachodzące sześciany (jak osobne obiekty z Blendera) -> suma, nie dziura w części wspólnej.
        var two = new Mesh();
        two.CombineMeshes(new[]
        {
            new CombineInstance { mesh = cube, transform = Matrix4x4.identity },
            new CombineInstance { mesh = cube, transform = Matrix4x4.Translate(new Vector3(0.5f, 0f, 0f)) },
        });
        s = Slice(two, Matrix4x4.identity, new Plane(Vector3.forward, Vector3.zero), Vector3.up);
        solid = 0;
        foreach (var cell in Rasterize(s.Contours, new Rect(-1, -1, 2, 2), 20, 20)) if (cell) solid++;
        Debug.Assert(s.Contours.Count == 1 && solid == 150, $"union: contours {s.Contours.Count}, solid cells {solid}");
        Object.DestroyImmediate(two);

        // Model pusty w środku: sześcian 2x z odwróconym sześcianem 1x w środku (ściany z grubością) -> pełny przekrój.
        var flipped = Object.Instantiate(cube);
        var t = flipped.triangles;
        for (int i = 0; i < t.Length; i += 3) (t[i], t[i + 1]) = (t[i + 1], t[i]);
        flipped.triangles = t;
        var hollow = new Mesh();
        hollow.CombineMeshes(new[]
        {
            new CombineInstance { mesh = cube, transform = Matrix4x4.Scale(Vector3.one * 1.6f) },
            new CombineInstance { mesh = flipped, transform = Matrix4x4.Scale(Vector3.one * 0.8f) },
        });
        s = Slice(hollow, Matrix4x4.identity, new Plane(Vector3.forward, Vector3.zero), Vector3.up);
        solid = 0;
        foreach (var cell in Rasterize(s.Contours, new Rect(-1, -1, 2, 2), 20, 20)) if (cell) solid++;
        Debug.Assert(s.Contours.Count == 1 && solid == 256, $"hollow: contours {s.Contours.Count}, solid cells {solid}");
        Object.DestroyImmediate(hollow);
        Object.DestroyImmediate(flipped);

        // Otwarty model: 4 osobne ściany boczne, w narożnikach szczelina 0.007 (< 2r) -> pełny kwadrat, nie pusty pierścień.
        var wv = new List<Vector3>();
        var wt = new List<int>();
        for (int w = 0; w < 4; w++)
        {
            var q = Quaternion.Euler(0f, 0f, 90f * w);
            int b = wv.Count;
            foreach (var (y, z) in new[] { (-0.495f, -0.5f), (0.495f, -0.5f), (0.495f, 0.5f), (-0.495f, 0.5f) })
                wv.Add(q * new Vector3(0.5f, y, z));
            wt.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var open = new Mesh();
        open.SetVertices(wv);
        open.SetTriangles(wt, 0);
        s = Slice(open, Matrix4x4.identity, new Plane(Vector3.forward, Vector3.zero), Vector3.up);
        solid = 0;
        foreach (var cell in Rasterize(s.Contours, new Rect(-1, -1, 2, 2), 20, 20)) if (cell) solid++;
        Debug.Assert(s.Contours.Count == 1 && solid == 100, $"open walls: contours {s.Contours.Count}, solid cells {solid}");
        Object.DestroyImmediate(open);
        Debug.Log("MeshSlicer self-check done");
    }
#endif
}
