# Wind Tunnel

A virtual wind tunnel in Unity. Load a 3D model (`.glb` / `.gltf`, e.g. exported from Blender), position the cutting plane, and the app computes the model's 2D cross-section and rasterizes it onto a cell grid that serves as input for the flow simulation.

## Goal

The target is an interactive, real-time 2D wind tunnel simulator that visualizes airflow around a chosen object:

- **Custom objects**: import your own models or airfoil shapes and pick a 2D cross-section. It is placed in the test section as an obstacle in the flow.
- **Result maps**: **vorticity**, to watch vortices form in the wake, and **pressure**, to show stagnation and low-pressure zones. **Temperature** is optional and depends on available time.
- **Live parameters**: inflow speed and grid resolution can be changed while the simulation runs, and the effect is visible immediately.
- **Educational, not scientific**: a simplified tool for building intuition about flow and comparing how different shapes behave. It is not meant to meet research-grade accuracy.

Technically, the simulation uses an Eulerian grid: velocity and pressure fields are stored at fixed points in space and advanced step by step in time. Unity (C#) handles the UI (panels, sliders, import), the simulation loop and rendering. The fields live in CPU-side arrays. After each step they are mapped to colors and written to a texture shown in the tunnel view. The selected cross-section is rasterized onto the grid as solid cells.

## Requirements

- Unity **6000.6.10f1** (version from [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt))
- Git LFS (`git lfs install` before cloning)
- .NET SDK, only to restore NuGet packages from the CLI

## Running locally

```sh
git clone https://github.com/mikolaj-paz/Wind-Tunnel.git
cd Wind-Tunnel
git lfs pull
```

`Assets/Packages` is in `.gitignore`. NuGetForUnity restores the NuGet packages (Clipper2) when the project is opened in the editor, or manually:

```sh
dotnet tool install --global NuGetForUnity.Cli
nugetforunity restore .
```

Then open the project in Unity Hub and load the scene [Assets/Scenes/Wind Tunnel.unity](Assets/Scenes/Wind%20Tunnel.unity).

## Releases

Releases are built by GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)). To start one, push a tag starting with `v`:

```sh
git tag v1.0.0
git push origin v1.0.0
```

The workflow:

1. creates a GitHub Release for the tag with auto-generated notes (`--generate-notes`),
2. builds **Windows x64** (Mono) and **Linux x64** (IL2CPP) in parallel using [game-ci/unity-builder](https://game-ci.com/),
3. zips each build as `WindTunnel-<tag>-<platform>.zip` and attaches it to the release.

The tag name is also used as the app version (`Application.version`).
