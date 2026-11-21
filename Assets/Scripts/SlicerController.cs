using UnityEngine;

public class SlicerController : MonoBehaviour
{
    [SerializeField] private MeshFilter _objectToSlice;
    [SerializeField] private Transform _planeTransform;
    [SerializeField] private Material _sliceMaterial;

    public MeshFilter ObjectToSlice { get => _objectToSlice; set => _objectToSlice = value; }
    public Transform PlaneTransform => _planeTransform;
    // Skala wyświetlanego przekroju względem oryginalnych wymiarów modelu (1 = jednostki z pliku, np. metry z Blendera).
    public float DisplayScale { get; set; } = 1f;

    private Plane _slicePlane;
    private GameObject _sliceObject;
    // Kopie danych mesha, czytane raz na mesh (zakłada, że mesh nie jest edytowany w miejscu po podpięciu).
    private Mesh _cachedMesh;
    private Vector3[] _verts;
    private int[] _tris;

    void CreateSlice()
    {
        if (_sliceObject != null)
        {
            Destroy(_sliceObject.GetComponent<MeshFilter>().sharedMesh);
            Destroy(_sliceObject);
            _sliceObject = null;
        }

        var src = _objectToSlice.transform;
        var mesh = _objectToSlice.sharedMesh;
        if (mesh != _cachedMesh)
        {
            _cachedMesh = mesh;
            _verts = mesh.vertices;
            _tris = mesh.triangles;
        }
        var section = MeshSlicer.Slice(_verts, _tris, mesh.bounds, src.localToWorldMatrix, _slicePlane, _planeTransform.up);

        var sliceObject = MeshSlicer.CreateObject(section, _sliceMaterial);
        _sliceObject = sliceObject;
    }

    void Update()
    {
        var obj = _objectToSlice.transform;
        if (_planeTransform.hasChanged || obj.hasChanged)
        {
            _planeTransform.hasChanged = false;
            obj.hasChanged = false;
            _slicePlane = new Plane(_planeTransform.forward, _planeTransform.position);
            CreateSlice();
        }
        // Przekrój liczony jest w świecie, gdzie model jest dopasowany do płaszczyzny; dzielenie przez skalę obiektu cofa to dopasowanie.
        if (_sliceObject != null) _sliceObject.transform.localScale = Vector3.one * (DisplayScale / obj.lossyScale.x);
    }
}
