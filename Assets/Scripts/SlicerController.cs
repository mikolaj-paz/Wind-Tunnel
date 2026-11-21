using UnityEngine;

public class SlicerController : MonoBehaviour
{
    [SerializeField] private MeshFilter _objectToSlice;
    [SerializeField] private Transform _planeTransform;
    [SerializeField] private Material _sliceMaterial;

    public MeshFilter ObjectToSlice { get => _objectToSlice; set => _objectToSlice = value; }
    public Transform PlaneTransform => _planeTransform;

    private Plane _slicePlane;
    private GameObject _sliceObject;

    void CreateSlice()
    {
        if (_sliceObject != null)
        {
            Destroy(_sliceObject.GetComponent<MeshFilter>().sharedMesh);
            Destroy(_sliceObject);
            _sliceObject = null;
        }

        var src = _objectToSlice.transform;
        var section = MeshSlicer.Slice(_objectToSlice.sharedMesh, src.localToWorldMatrix, _slicePlane, _planeTransform.up);

        var sliceObject = MeshSlicer.CreateObject(section, _sliceMaterial);
        _sliceObject = sliceObject;
    }

    void Update()
    {
        var obj = _objectToSlice.transform;
        if (!_planeTransform.hasChanged && !obj.hasChanged) return;
        _planeTransform.hasChanged = false;
        obj.hasChanged = false;

        _slicePlane = new Plane(_planeTransform.forward, _planeTransform.position);

        CreateSlice();
    }
}
