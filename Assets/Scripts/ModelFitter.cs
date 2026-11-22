using UnityEngine;

// Dopasowanie obiektu do płaszczyzny przekroju. Wszystko w przestrzeni lokalnej rodzica płaszczyzny.
public static class ModelFitter
{
    public static Vector3 FitToPlane(Transform obj, Vector3[] verts, Transform plane, Vector3 center, float sizeRatio)
    {
        obj.localScale *= ScaleToFit(BoundsInPlaneSpace(obj, verts, plane), sizeRatio * PlaneSize(plane));
        var fitted = BoundsInPlaneSpace(obj, verts, plane);
        obj.position += PlaneSpaceToWorld(plane).MultiplyVector(center - fitted.center);
        return fitted.extents;
    }

    public static float PlaneSize(Transform plane)
    {
        var quad = plane.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        var s = plane.localScale;
        return Mathf.Min(quad.x * s.x, quad.y * s.y);
    }

    public static float ScaleToFit(Bounds bounds, float targetSize)
    {
        float diameter = bounds.size.magnitude;
        return diameter > 0f ? targetSize / diameter : 1f;
    }

    public static Bounds BoundsInPlaneSpace(Transform obj, Vector3[] verts, Transform plane) =>
        BoundsOf(verts, WorldToPlaneSpace(plane) * obj.localToWorldMatrix);

    public static Bounds BoundsOf(Vector3[] verts, Matrix4x4 m)
    {
        var b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
        foreach (var v in verts) b.Encapsulate(m.MultiplyPoint3x4(v));
        return b;
    }

    static Matrix4x4 WorldToPlaneSpace(Transform plane) => plane.parent ? plane.parent.worldToLocalMatrix : Matrix4x4.identity;
    static Matrix4x4 PlaneSpaceToWorld(Transform plane) => plane.parent ? plane.parent.localToWorldMatrix : Matrix4x4.identity;
}
