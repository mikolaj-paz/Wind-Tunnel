using ImGuiNET;
using UnityEngine;

// Globalna konfiguracja ImGui wspólna dla wszystkich okien (nie należy do żadnego konkretnego okna).
public class ImGuiStyleSettings : MonoBehaviour
{
    [SerializeField] private float referenceHeight = 1080f; // przy tej wysokości ekranu czcionka ma rozmiar bazowy (jak 1rem)

    private void OnEnable() => UImGui.UImGuiUtility.Layout += OnLayout;
    private void OnDisable() => UImGui.UImGuiUtility.Layout -= OnLayout;

    private void OnLayout(UImGui.UImGui ui)
    {
        // Tekst skaluje się z wysokością ekranu.
        ImGui.GetStyle().FontScaleMain = ImGui.GetIO().DisplaySize.Y / referenceHeight;
    }
}
