using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

// Lista modeli do wyboru: te z inspektora + zaimportowane z plików (żyją tylko do zamknięcia aplikacji).
public class ModelLibrary
{
    // Linux: wielkość liter w ścieżkach ma znaczenie, Windows: nie.
    static readonly StringComparer PathComparer =
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly List<Mesh> models;
    private readonly Dictionary<string, int> importedPaths = new(PathComparer);

    public string[] Names { get; private set; }
    public Mesh this[int i] => models[i];

    public ModelLibrary(List<Mesh> models)
    {
        this.models = models;
        UpdateNames();
    }

    public bool TryFindImported(string path, out int index) =>
        importedPaths.TryGetValue(Path.GetFullPath(path), out index);

    public async Task<int> Import(string path)
    {
        path = Path.GetFullPath(path);
        models.Add(await ModelLoader.Load(path));
        int index = models.Count - 1;
        importedPaths[path] = index;
        UpdateNames();
        return index;
    }

    void UpdateNames() => Names = models.ConvertAll(m => m.name).ToArray();
}
