using UnityEngine;

// Odświeża przekrój, gdy płaszczyzna albo obiekt się ruszy, i trzyma jeden obiekt sceny z jego podglądem.
public class SlicerController : MonoBehaviour
{
    [SerializeField] private MeshFilter _objectToSlice;
    [SerializeField] private Transform _planeTransform;
    [SerializeField] private Material _sliceMaterial;

    public MeshFilter ObjectToSlice { get => _objectToSlice; set => _objectToSlice = value; }
    public Transform PlaneTransform => _planeTransform;
    public float DisplayScale { get; set; } = 1f;

    private GameObject _sliceObject;
    private Mesh _cachedMesh;
    private Vector3[] _verts;
    private int[] _tris;

    void Update()
    {
        if (ConsumeTransformChanges()) RebuildSlice();
        if (_sliceObject != null) _sliceObject.transform.localScale = Vector3.one * SliceObjectScale();
    }

    // true, jeśli od ostatniego wywołania ruszyła się płaszczyzna albo obiekt.
    bool ConsumeTransformChanges()
    {
        var obj = _objectToSlice.transform;
        bool changed = _planeTransform.hasChanged || obj.hasChanged;
        _planeTransform.hasChanged = obj.hasChanged = false;
        return changed;
    }

    float SliceObjectScale() => DisplayScale / _objectToSlice.transform.lossyScale.x;

    void RebuildSlice()
    {
        DestroySliceObject();
        _sliceObject = CreateSliceObject(SliceCurrentMesh(), _sliceMaterial);
    }

    Section SliceCurrentMesh()
    {
        var mesh = _objectToSlice.sharedMesh;
        CacheMeshData(mesh);
        var plane = new Plane(_planeTransform.forward, _planeTransform.position);
        return MeshSlicer.Slice(_verts, _tris, mesh.bounds, _objectToSlice.transform.localToWorldMatrix, plane, _planeTransform.up);
    }

    void CacheMeshData(Mesh mesh)
    {
        if (mesh == _cachedMesh) return;
        _cachedMesh = mesh;
        _verts = mesh.vertices;
        _tris = mesh.triangles;
    }

    void DestroySliceObject()
    {
        if (_sliceObject == null) return;
        Destroy(_sliceObject.GetComponent<MeshFilter>().sharedMesh);
        Destroy(_sliceObject);
        _sliceObject = null;
    }

    // Obiekt do wyświetlenia w pełnej rozdzielczości (kontury w płaszczyźnie XY sceny 2D).
    static GameObject CreateSliceObject(Section section, Material material)
    {
        var go = new GameObject("Section");
        var col = go.AddComponent<PolygonCollider2D>();
        col.pathCount = section.Contours.Count;
        for (int i = 0; i < section.Contours.Count; i++) col.SetPath(i, section.Contours[i]);
        go.AddComponent<MeshFilter>().sharedMesh = col.CreateMesh(false, false);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
