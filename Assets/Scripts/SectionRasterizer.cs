using System.Collections.Generic;
using UnityEngine;

// Rasteryzacja przekroju dla symulacji: grid[x,y] == true -> komórka jest ciałem stałym.
// Próbkowane są środki komórek (scanline, even-odd).
public static class SectionRasterizer
{
    // area = obszar tunelu we współrzędnych przekroju.
    public static bool[,] Rasterize(List<Vector2[]> contours, Rect area, int width, int height)
    {
        var grid = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            var xs = Crossings(contours, CellCenter(y, area.yMin, area.height, height));
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = Mathf.Max(0, Mathf.CeilToInt(ToCellCoord(xs[k], area.xMin, area.width, width)));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt(ToCellCoord(xs[k + 1], area.xMin, area.width, width)));
                for (int x = x0; x <= x1; x++) grid[x, y] = true;
            }
        }
        return grid;
    }

    // Posortowane X, w których pozioma linia y przecina krawędzie konturów.
    public static List<float> Crossings(List<Vector2[]> contours, float y)
    {
        var xs = new List<float>();
        foreach (var c in contours)
            for (int i = 0; i < c.Length; i++)
            {
                Vector2 a = c[i], b = c[(i + 1) % c.Length];
                if (a.y > y != b.y > y) xs.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
            }
        xs.Sort();
        return xs;
    }

    // Środek komórki o indeksie i w świecie.
    public static float CellCenter(int i, float min, float size, int cells) => min + (i + 0.5f) * size / cells;

    // Odwrotność CellCenter: pozycja w świecie -> (ułamkowy) indeks komórki.
    public static float ToCellCoord(float pos, float min, float size, int cells) => (pos - min) / size * cells - 0.5f;
}
