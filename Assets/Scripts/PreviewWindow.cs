// Odkomentuj, żeby przywrócić przesunięcia X/Z płaszczyzny pionowej (#define musi być przed using).
// #define VERTICAL_OFFSET
using System;
using ImGuiNET;
using UnityEngine;

public class PreviewWindow : MonoBehaviour
{
    [SerializeField] private SlicerController slicerController;
    [SerializeField] private Texture previewCameraOutputTexture;
    // FBX z Blendera: w imporcie zaznaczyć Read/Write, bo MeshSlicer i FitTo czytają wierzchołki (inaczej w buildzie błąd).
    [SerializeField] private Mesh[] models;
    [SerializeField, Range(0.1f, 1f)] private float objectSizeToPlaneRatio = 0.8f;
    [SerializeField, Range(0.1f, 0.5f)] private float windowToScreenWidthRatio = 0.25f;

    private Transform planeTransform;
    private string[] modelNames;
    private int model;
    private Vector3 objPos, objScale;
    private Quaternion objRot;

    // Wszystko w przestrzeni lokalnej rodzica płaszczyzny (osie X/Y/Z rodzica, nie świata).
    private Vector3 center;
    private Vector3 offset;
    private int vertical = 1; // 0 = poziomy, 1 = pionowy
    private int angle;
    private float startAngle;
    private float maxOffset;

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
        modelNames = System.Array.ConvertAll(models, m => m.name);
        if (models.Length > 0) SetModel(0);
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
    }

    // Skaluje (jednolicie) i centruje obiekt na płaszczyźnie, potem dopasowuje zakres suwaków.
    private void FitTo(MeshFilter r)
    {
        var quad = planeTransform.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        var s = planeTransform.localScale;
        float planeSize = Mathf.Min(quad.x * s.x, quad.y * s.y);
        float diameter = PlaneSpaceBounds(r).extents.magnitude * 2f;
        if (diameter > 0f) r.transform.localScale *= objectSizeToPlaneRatio * planeSize / diameter;
        var b = PlaneSpaceBounds(r);
        var parent = planeTransform.parent;
        var shift = center - b.center;
        r.transform.position += parent ? parent.TransformVector(shift) : shift;
        maxOffset = b.extents.y * (1f - EdgeMargin);
    }

    // AABB obiektu w przestrzeni rodzica płaszczyzny, z wierzchołków: dokładny przy dowolnym obrocie.
    private Bounds PlaneSpaceBounds(MeshFilter r)
    {
        var parent = planeTransform.parent;
        var m = (parent ? parent.worldToLocalMatrix : Matrix4x4.identity) * r.transform.localToWorldMatrix;
        var verts = r.sharedMesh.vertices;
        var b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
        foreach (var v in verts) b.Encapsulate(m.MultiplyPoint3x4(v));
        return b;
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

        bool modelChanged = ImGui.Combo("Model", ref model, modelNames, modelNames.Length);
        if (modelChanged) SetModel(model);

        bool modeChanged = ImGui.RadioButton("Poziomy", ref vertical, 0);
        ImGui.SameLine();
        modeChanged |= ImGui.RadioButton("Pionowy", ref vertical, 1);
        ImGui.SameLine();
        bool reset = ImGui.Button("Reset");

        offset = planeTransform.localPosition - center;
        if (vertical == 1) angle = (int)Mathf.DeltaAngle(startAngle, planeTransform.localEulerAngles.y);

        bool changed = false;
        if (vertical == 1)
        {
#if VERTICAL_OFFSET
            changed |= ImGui.SliderFloat("Przesunięcie X", ref offset.x, -maxOffset, maxOffset);
            changed |= ImGui.SliderFloat("Przesunięcie Z", ref offset.z, -maxOffset, maxOffset);
#endif
            changed |= ImGui.SliderInt("Kąt obrotu", ref angle, -180, 180, angle + "°");
        }
        else
        {
            changed |= ImGui.SliderFloat("Przesunięcie Y", ref offset.y, -maxOffset, maxOffset);
        }

        if (modeChanged || reset || modelChanged)
        {
            offset = Vector3.zero;
            angle = 0;
        }

        if (modeChanged || reset || modelChanged || changed)
        {
            var rot = vertical == 1 ? Quaternion.Euler(0f, startAngle + angle, 0f) : Quaternion.Euler(-90f, 0f, 0f);
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
