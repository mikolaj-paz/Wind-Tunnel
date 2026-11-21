// Odkomentuj, żeby przywrócić przesunięcia X/Z płaszczyzny pionowej (#define musi być przed using).
#define VERTICAL_OFFSET
using System;
using System.Collections.Generic;
using ImGuiNET;
using SFB;
using UnityEngine;

public class PreviewWindow : MonoBehaviour
{
    [SerializeField] private SlicerController slicerController;
    [SerializeField] private Texture previewCameraOutputTexture;
    // FBX z Blendera: w imporcie zaznaczyć Read/Write, bo MeshSlicer i FitTo czytają wierzchołki (inaczej w buildzie błąd).
    [SerializeField] private List<Mesh> models;
    [SerializeField, Range(0.1f, 1f)] private float objectSizeToPlaneRatio = 0.8f;
    [SerializeField, Range(0.1f, 0.5f)] private float windowToScreenWidthRatio = 0.25f;

    private Transform planeTransform;
    private string[] modelNames;
    private int model;
    private string importError;
    private bool loading, imported;
    private float displayScale = 1f;
    private readonly Dictionary<string, int> importedPaths = new( // pełna ścieżka -> indeks w models
        System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal); // Linux: wielkość liter ma znaczenie
    private Vector3 objPos, objScale;
    private Quaternion objRot;

    // Wszystko w przestrzeni lokalnej rodzica płaszczyzny (osie X/Y/Z rodzica, nie świata).
    private Vector3 center;
    private Vector3 offset;
    private int vertical = 1; // 0 = poziomy, 1 = pionowy
    private int angle;
    private float startAngle;
    private Vector3 maxOffset; // osobno dla każdej osi: zasięg bryły wzdłuż X/Y/Z rodzica płaszczyzny

    // Margines od ścian obiektu. Cięcie dokładnie na płaskiej ścianie daje niestabilny wynik:
    private const float EdgeMargin = 0.01f;

    private void Awake()
    {
        planeTransform = slicerController.PlaneTransform;
        center = planeTransform.localPosition;
        startAngle = planeTransform.localEulerAngles.y;
        var t = slicerController.ObjectToSlice.transform;
        t.GetLocalPositionAndRotation(out objPos, out objRot);
        objScale = t.localScale;
        modelNames = models.ConvertAll(m => m.name).ToArray();
        if (models.Count > 0) SetModel(0);
        else FitTo(slicerController.ObjectToSlice);
    }

    // Podmienia mesh przekrawanego obiektu i dopasowuje go od nowa.
    private void SetModel(int i)
    {
        model = i;
        var mf = slicerController.ObjectToSlice;
        mf.sharedMesh = models[i];
        mf.transform.SetLocalPositionAndRotation(objPos, objRot);
        mf.transform.localScale = objScale;
        FitTo(mf);
        displayScale = slicerController.DisplayScale = 1f; // każdy model startuje w swoich oryginalnych wymiarach
    }

    // Skaluje (jednolicie) i centruje obiekt na płaszczyźnie, potem dopasowuje zakres suwaków.
    private void FitTo(MeshFilter r)
    {
        var quad = planeTransform.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        var s = planeTransform.localScale;
        float planeSize = Mathf.Min(quad.x * s.x, quad.y * s.y);
        var verts = r.sharedMesh.vertices; // kopia całego mesha, więc czytana raz
        float diameter = PlaneSpaceBounds(r, verts).extents.magnitude * 2f;
        if (diameter > 0f) r.transform.localScale *= objectSizeToPlaneRatio * planeSize / diameter;
        var b = PlaneSpaceBounds(r, verts);
        var parent = planeTransform.parent;
        var shift = center - b.center;
        r.transform.position += parent ? parent.TransformVector(shift) : shift;
        maxOffset = b.extents * (1f - EdgeMargin);
    }

    // AABB obiektu w przestrzeni rodzica płaszczyzny, z wierzchołków: dokładny przy dowolnym obrocie.
    private Bounds PlaneSpaceBounds(MeshFilter r, Vector3[] verts)
    {
        var parent = planeTransform.parent;
        var m = (parent ? parent.worldToLocalMatrix : Matrix4x4.identity) * r.transform.localToWorldMatrix;
        var b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
        foreach (var v in verts) b.Encapsulate(m.MultiplyPoint3x4(v));
        return b;
    }

    // Dodane modele żyją tylko do zamknięcia aplikacji.
    private async void ImportModel(string selected)
    {
        loading = true;
        importError = null;
        try
        {
            var path = System.IO.Path.GetFullPath(selected);
            if (importedPaths.TryGetValue(path, out int existing))
            {
                importError = "Ten model jest już zaimportowany.";
                model = existing;
            }
            else
            {
                models.Add(await ModelLoader.Load(path));
                modelNames = models.ConvertAll(m => m.name).ToArray();
                model = models.Count - 1;
                importedPaths[path] = model;
            }
            imported = true; // OnLayout podmieni model i zresetuje płaszczyznę jak przy wyborze z listy
        }
        catch (Exception e)
        {
            importError = e.Message;
            Debug.LogException(e);
        }
        loading = false;
    }

    private void OnEnable()
    {
        UImGui.UImGuiUtility.Layout += OnLayout;
    }

    private void OnDisable()
    {
        UImGui.UImGuiUtility.Layout -= OnLayout;
    }

    private void OnLayout(UImGui.UImGui ui)
    {
        var display = ImGui.GetIO().DisplaySize;
        float width = display.X * windowToScreenWidthRatio;
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(display.X - width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, 0f), ImGuiCond.Always);
        ImGui.Begin("Preview", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);

        ImGui.BeginDisabled(loading);
        bool modelChanged = ImGui.Combo("Model", ref model, modelNames, modelNames.Length) | imported;
        imported = false;
        if (modelChanged) SetModel(model);

        if (ImGui.Button("Dodaj model..."))
        {
            // Natywne okno systemu (Windows / GTK3 na Linuksie / macOS). Wersja synchroniczna: okno jest modalne,
            // aplikacja czeka na wybór (na Windowsie wersja Async i tak działa synchronicznie).
            var paths = StandaloneFileBrowser.OpenFilePanel("Wybierz model", "", new[] { new ExtensionFilter("glTF", "glb", "gltf") }, false);
            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) ImportModel(paths[0]);
        }
        ImGui.EndDisabled();
        if (loading) ImGui.TextDisabled("Ładowanie modelu..." + "|/-\\"[(int)(ImGui.GetTime() * 8) % 4]);
        if (importError != null) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), importError);

        if (ImGui.SliderFloat("Skala przekroju", ref displayScale, 0.01f, 100f, "%.2fx", ImGuiSliderFlags.Logarithmic))
            slicerController.DisplayScale = displayScale;

        bool modeChanged = ImGui.RadioButton("Poziomy", ref vertical, 0);
        ImGui.SameLine();
        modeChanged |= ImGui.RadioButton("Pionowy", ref vertical, 1);
        ImGui.SameLine();
        bool reset = ImGui.Button("Reset");

        offset = planeTransform.localPosition - center;
        if (vertical == 1) angle = Mathf.RoundToInt(Mathf.DeltaAngle(startAngle, planeTransform.localEulerAngles.y));

        bool changed = false;
        if (vertical == 1)
        {
#if VERTICAL_OFFSET
            changed |= ImGui.SliderFloat("Przesunięcie X", ref offset.x, -maxOffset.x, maxOffset.x);
            changed |= ImGui.SliderFloat("Przesunięcie Z", ref offset.z, -maxOffset.z, maxOffset.z);
#endif
            changed |= ImGui.SliderInt("Kąt obrotu", ref angle, -180, 180, angle + "°");
        }
        else
        {
            changed |= ImGui.SliderFloat("Przesunięcie Y", ref offset.y, -maxOffset.y, maxOffset.y);
        }

        if (modeChanged || reset || modelChanged)
        {
            offset = Vector3.zero;
            angle = 0;
        }

        if (modeChanged || reset || modelChanged || changed)
        {
            var rot = vertical == 1 ? Quaternion.Euler(0f, startAngle + angle, 0f) : Quaternion.Euler(-90f, 0f, 90f);
            planeTransform.SetLocalPositionAndRotation(center + offset, rot);
        }

        if (previewCameraOutputTexture != null)
        {
            float w = ImGui.GetContentRegionAvail().X;
            var id = UImGui.UImGuiUtility.GetTextureId(previewCameraOutputTexture);
            ImGui.Image(new ImTextureRef { _TexID = id }, new System.Numerics.Vector2(w, w * previewCameraOutputTexture.height / previewCameraOutputTexture.width));
        }
        ImGui.End();
    }
}
