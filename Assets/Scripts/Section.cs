using System.Collections.Generic;
using UnityEngine;

// Przekrój bryły: kontury 2D w układzie współrzędnych płaszczyzny cięcia (Origin + AxisU/AxisV).
public struct Section
{
    public List<Vector2[]> Contours;
    public Vector3 Origin, AxisU, AxisV;

    // up: kierunek "góry" obrazu przekroju (np. transform.up płaszczyzny); rzutowany na płaszczyznę.
    // center: środek geometrii, nie pivot - importowane modele mają pivot często daleko od bryły, a skala wyświetlania rośnie wokół Origin.
    public static Section Create(Plane plane, Vector3 up, Vector3 center)
    {
        var u = Vector3.Cross(up, plane.normal).normalized;
        return new Section
        {
            Contours = new List<Vector2[]>(),
            Origin = plane.ClosestPointOnPlane(center),
            AxisU = u,
            AxisV = Vector3.Cross(plane.normal, u),
        };
    }

    public Vector2 ToPlane(Vector3 worldPoint)
    {
        var d = worldPoint - Origin;
        return new Vector2(Vector3.Dot(d, AxisU), Vector3.Dot(d, AxisV));
    }
}
