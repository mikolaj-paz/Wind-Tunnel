// Odkomentuj, żeby przywrócić przesunięcia X/Z płaszczyzny pionowej (#define musi być przed using).
// #define VERTICAL_OFFSET
using System;
using System.Collections.Generic;
using ImGuiNET;
using SFB;
using UnityEngine;
using Num = System.Numerics;

// Okno ImGui: wybór/import modelu, skala przekroju, ustawienie płaszczyzny cięcia i podgląd.
public class PreviewWindow : MonoBehaviour
{
    [SerializeField] private SlicerController slicerController;
    [SerializeField] private Texture previewCameraOutputTexture;
    [SerializeField] private List<Mesh> models;
    [SerializeField, Range(0.1f, 1f)] private float objectSizeToPlaneRatio = 0.8f;
    [SerializeField, Range(0.1f, 0.5f)] private float windowToScreenWidthRatio = 0.25f;

    // Margines od ścian obiektu. Cięcie dokładnie na płaskiej ścianie daje niestabilny wynik.
    private const float EdgeMargin = 0.01f;
    private static readonly Num.Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private ModelLibrary library;
    private int selectedModel;
    private bool loading;
    private string importError;

    private Vector3 objectStartPos, objectStartScale;
    private Quaternion objectStartRot;

    private Transform planeTransform;
    private Vector3 planeCenter;
    private float planeStartAngle;
    private bool vertical = true;
    private Vector3 maxOffset;

    private void Awake()
    {
        planeTransform = slicerController.PlaneTransform;
        planeCenter = planeTransform.localPosition;
        planeStartAngle = planeTransform.localEulerAngles.y;
        var t = slicerController.ObjectToSlice.transform;
        t.GetLocalPositionAndRotation(out objectStartPos, out objectStartRot);
        objectStartScale = t.localScale;
        library = new ModelLibrary(models);
        if (models.Count > 0) ShowModel(0);
        else FitObjectToPlane();
    }

    private void OnEnable() => UImGui.UImGuiUtility.Layout += OnLayout;
    private void OnDisable() => UImGui.UImGuiUtility.Layout -= OnLayout;

    // ---------- Model ----------

    // Jak ShowModel, ale dodatkowo ustawia płaszczyznę od nowa (wybór z listy / import).
    private void SelectModel(int i)
    {
        ShowModel(i);
        ResetPlane();
    }

    // Podmienia mesh przekrawanego obiektu i dopasowuje go od nowa.
    private void ShowModel(int i)
    {
        selectedModel = i;
        var mf = slicerController.ObjectToSlice;
        mf.sharedMesh = library[i];
        mf.transform.SetLocalPositionAndRotation(objectStartPos, objectStartRot);
        mf.transform.localScale = objectStartScale;
        FitObjectToPlane();
        slicerController.DisplayScale = 1f;
    }

    private void FitObjectToPlane()
    {
        var mf = slicerController.ObjectToSlice;
        var extents = ModelFitter.FitToPlane(mf.transform, mf.sharedMesh.vertices, planeTransform, planeCenter, objectSizeToPlaneRatio);
        maxOffset = extents * (1f - EdgeMargin);
    }

    private async void ImportModel(string path)
    {
        loading = true;
        importError = null;
        try
        {
            if (library.TryFindImported(path, out int existing))
            {
                importError = "Ten model jest już zaimportowany.";
                SelectModel(existing);
            }
            else SelectModel(await library.Import(path));
        }
        catch (Exception e)
        {
            importError = e.Message;
            Debug.LogException(e);
        }
        loading = false;
    }

    // Natywne okno systemu (Windows / GTK3 na Linuksie / macOS). Wersja synchroniczna: okno jest modalne,
    // aplikacja czeka na wybór (na Windowsie wersja Async i tak działa synchronicznie).
    private static bool TryPickModelFile(out string path)
    {
        var paths = StandaloneFileBrowser.OpenFilePanel("Wybierz model", "", new[] { new ExtensionFilter("glTF", "glb", "gltf") }, false);
        path = paths.Length > 0 ? paths[0] : null;
        return !string.IsNullOrEmpty(path);
    }

    // ---------- Płaszczyzna ----------

    private void ResetPlane() =>
        planeTransform.SetLocalPositionAndRotation(planeCenter, PlaneRotation(vertical, planeStartAngle, 0));

    private static Quaternion PlaneRotation(bool vertical, float startAngle, int angle) =>
        vertical ? Quaternion.Euler(0f, startAngle + angle, 0f) : Quaternion.Euler(-90f, 0f, 90f);

    private int CurrentPlaneAngle() =>
        Mathf.RoundToInt(Mathf.DeltaAngle(planeStartAngle, planeTransform.localEulerAngles.y));

    // ---------- UI ----------

    private void OnLayout(UImGui.UImGui ui)
    {
        BeginWindowAtRightEdge("Preview", windowToScreenWidthRatio);
        DrawModelPicker();
        DrawImportStatus();
        DrawDisplayScaleSlider();
        DrawPlaneModeButtons();
        DrawPlaneSliders();
        if (previewCameraOutputTexture != null) DrawFullWidthImage(previewCameraOutputTexture);
        ImGui.End();
    }

    private static void BeginWindowAtRightEdge(string title, float widthRatio)
    {
        var display = ImGui.GetIO().DisplaySize;
        float width = display.X * widthRatio;
        ImGui.SetNextWindowPos(new Num.Vector2(display.X - width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Num.Vector2(width, 0f), ImGuiCond.Always);
        ImGui.Begin(title, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
    }

    private void DrawModelPicker()
    {
        ImGui.BeginDisabled(loading);
        if (ImGui.Combo("Model", ref selectedModel, library.Names, library.Names.Length)) SelectModel(selectedModel);
        if (ImGui.Button("Dodaj model...") && TryPickModelFile(out var path)) ImportModel(path);
        ImGui.EndDisabled();
    }

    private void DrawImportStatus()
    {
        if (loading) ImGui.TextDisabled("Ładowanie modelu..." + Spinner(ImGui.GetTime()));
        if (importError != null) ImGui.TextColored(ErrorColor, importError);
    }

    private static char Spinner(double time) => "|/-\\"[(int)(time * 8) % 4];

    private void DrawDisplayScaleSlider()
    {
        float scale = slicerController.DisplayScale;
        if (ImGui.SliderFloat("Skala przekroju", ref scale, 0.01f, 100f, "%.2fx", ImGuiSliderFlags.Logarithmic))
            slicerController.DisplayScale = scale;
    }

    // Zmiana trybu (także kliknięcie aktywnego) albo Reset ustawia płaszczyznę od nowa.
    private void DrawPlaneModeButtons()
    {
        bool horizontalClicked = ImGui.RadioButton("Poziomy", !vertical);
        ImGui.SameLine();
        bool verticalClicked = ImGui.RadioButton("Pionowy", vertical);
        ImGui.SameLine();
        bool resetClicked = ImGui.Button("Reset");

        if (horizontalClicked) vertical = false;
        if (verticalClicked) vertical = true;
        if (horizontalClicked || verticalClicked || resetClicked) ResetPlane();
    }

    private void DrawPlaneSliders()
    {
        var offset = planeTransform.localPosition - planeCenter;
        int angle = vertical ? CurrentPlaneAngle() : 0;
        bool changed = vertical
            ? DrawVerticalPlaneSliders(ref offset, ref angle, maxOffset)
            : DrawHorizontalPlaneSlider(ref offset, maxOffset);
        if (changed)
            planeTransform.SetLocalPositionAndRotation(planeCenter + offset, PlaneRotation(vertical, planeStartAngle, angle));
    }

    private static bool DrawVerticalPlaneSliders(ref Vector3 offset, ref int angle, Vector3 maxOffset)
    {
        bool changed = false;
#if VERTICAL_OFFSET
        changed |= ImGui.SliderFloat("Przesunięcie X", ref offset.x, -maxOffset.x, maxOffset.x);
        changed |= ImGui.SliderFloat("Przesunięcie Z", ref offset.z, -maxOffset.z, maxOffset.z);
#endif
        changed |= ImGui.SliderInt("Kąt obrotu", ref angle, -180, 180, angle + "°");
        return changed;
    }

    private static bool DrawHorizontalPlaneSlider(ref Vector3 offset, Vector3 maxOffset) =>
        ImGui.SliderFloat("Przesunięcie Y", ref offset.y, -maxOffset.y, maxOffset.y);

    private static void DrawFullWidthImage(Texture texture)
    {
        float w = ImGui.GetContentRegionAvail().X;
        var id = UImGui.UImGuiUtility.GetTextureId(texture);
        ImGui.Image(new ImTextureRef { _TexID = id }, new Num.Vector2(w, w * texture.height / texture.width));
    }
}
