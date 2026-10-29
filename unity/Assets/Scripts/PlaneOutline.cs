using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FieldInspection
{
    [RequireComponent(typeof(ARPlane))]
    public class PlaneOutline : MonoBehaviour
    {
        ARPlane plane;
        LineRenderer line;
        Material material;

        void Awake()
        {
            plane = GetComponent<ARPlane>();
            line = gameObject.AddComponent<LineRenderer>();
            material = new Material(Resources.Load<Material>("InspectionMarker"));
            material.color = new Color(0.4f, 0.9f, 0.65f);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.012f;
            plane.boundaryChanged += OnBoundaryChanged;
            Draw();
        }
        void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs _) { Draw(); }
        void Draw()
        {
            if (!plane.boundary.IsCreated) return;
            line.positionCount = plane.boundary.Length;
            for (int i = 0; i < plane.boundary.Length; i++)
                line.SetPosition(i, new Vector3(plane.boundary[i].x, 0.003f, plane.boundary[i].y));
        }
        void Update() { line.enabled = plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null; }
        void OnDestroy() { if (plane != null) plane.boundaryChanged -= OnBoundaryChanged; if (material != null) Destroy(material); }
    }
}
