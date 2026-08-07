using UnityEngine;

namespace AccessoryGizmo
{
    public enum GizmoAxis { None, X, Y, Z }

    /// <summary>
    /// Holds all transient state for one gizmo interaction frame.
    /// Reset on MouseUp or when no accessory is selected.
    /// </summary>
    public class GizmoState
    {
        // Visibility
        public bool IsVisible  = false;
        public bool ForceHide  = false;   // set to true when user presses G key to suppress

        // Interaction
        public bool      IsDragging   = false;
        public GizmoAxis ActiveAxis   = GizmoAxis.None;
        public GizmoAxis HoveredAxis  = GizmoAxis.None;

        // Drag bookmarks
        public Vector2 DragStartMousePos;
        public Vector3 DragStartAddMovePos;

        // Screen-space computed each frame when a valid acc is visible
        public Vector2 ScreenCenter;

        // Arrow tip positions in IMGUI screen coords (top-left origin)
        public Vector2 TipX;
        public Vector2 TipY;
        public Vector2 TipZ;

        // Normalised screen directions of each world axis
        public Vector2 DirX;
        public Vector2 DirY;
        public Vector2 DirZ;

        // Hit-test rects for mouse events
        public Rect RectX;
        public Rect RectY;
        public Rect RectZ;
        public Rect RectCenter;  // small sphere at the origin dot

        // Live addMove position (updated each frame)
        public Vector3 CurrentPos;

        public void ResetDrag()
        {
            IsDragging  = false;
            ActiveAxis  = GizmoAxis.None;
            HoveredAxis = GizmoAxis.None;
        }
    }
}
