using Studio;
using UnityEngine;

namespace AccessoryGizmo
{
    /// <summary>
    /// Main MonoBehaviour for the 3-axis drag gizmo.
    /// Runs position projection, hover-detection, drag math, and delegates drawing to GizmoRenderer.
    /// Blocks Studio camera control while a drag is active.
    /// </summary>
    public class GizmoManager : MonoBehaviour
    {
        private readonly GizmoState    _st  = new GizmoState();
        private readonly GizmoRenderer _rdr = new GizmoRenderer();

        private object  _accInfo;
        private float   _refreshTimer;
        private const float REFRESH_INTERVAL = 0.08f;   // seconds between reflection calls

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _st.IsVisible = true;   // gizmo visible by default; G key toggles suppression
        }

        private void Update()
        {
            // Toggle visibility override
            if (AccessoryGizmoPlugin.KeyToggleGizmo.Value.IsDown())
                _st.ForceHide = !_st.ForceHide;

            // Throttled accessory info refresh (reflection is expensive)
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = REFRESH_INTERVAL;
                _accInfo = GizmoAccBridge.IsAccessoryTabActive
                    ? GizmoAccBridge.GetCurrentAccessoryInfo()
                    : null;
            }

            // Block studio camera while dragging
            if (_st.IsDragging && Studio.Studio.Instance != null)
            {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = () => _st.IsDragging;
                Input.ResetInputAxes();
            }
        }

        private void OnGUI()
        {
            bool shouldShow = !_st.ForceHide
                           && GizmoAccBridge.IsAccessoryTabActive
                           && _accInfo != null
                           && !GizmoAccBridge.IsEmpty(_accInfo);

            if (!shouldShow)
            {
                if (_st.IsDragging) _st.ResetDrag();
                return;
            }

            // Get world pos of accessory mesh
            Vector3 worldPos;
            if (!GizmoAccBridge.TryGetWorldPosition(_accInfo, out worldPos)) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // Project to IMGUI screen space
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) return;   // behind camera

            Vector2 centre = new Vector2(sp.x, Screen.height - sp.y);
            _st.ScreenCenter = centre;

            // Read current addMove position
            GizmoAccBridge.TryGetPosition(_accInfo, GizmoAccBridge.MoveAdjustIndex, out _st.CurrentPos);

            // Compute screen-projected axis directions
            bool local = AccessoryGizmoPlugin.GizmoLocalSpace.Value;
            Vector3 wX = Vector3.right, wY = Vector3.up, wZ = Vector3.forward;
            if (local)
            {
                Quaternion rot;
                GizmoAccBridge.TryGetWorldRotation(_accInfo, out rot);
                wX = rot * wX; wY = rot * wY; wZ = rot * wZ;
            }

            _st.DirX = ProjectAxis(cam, worldPos, wX);
            _st.DirY = ProjectAxis(cam, worldPos, wY);
            _st.DirZ = ProjectAxis(cam, worldPos, wZ);

            float len = AccessoryGizmoPlugin.HandleLength.Value;
            _st.TipX = centre + _st.DirX * len;
            _st.TipY = centre + _st.DirY * len;
            _st.TipZ = centre + _st.DirZ * len;

            // Hit-test rects
            _st.RectX      = ShaftRect(centre, _st.TipX);
            _st.RectY      = ShaftRect(centre, _st.TipY);
            _st.RectZ      = ShaftRect(centre, _st.TipZ);
            _st.RectCenter = new Rect(centre.x - 10f, centre.y - 10f, 20f, 20f);

            HandleInput(cam, worldPos);

            _rdr.Draw(_st);
        }

        // ── Input handling ────────────────────────────────────────────────────

        private void HandleInput(Camera cam, Vector3 worldPos)
        {
            Event ev = Event.current;
            Vector2 mp = ev.mousePosition;

            // Update hover (only when not dragging)
            if (!_st.IsDragging)
            {
                _st.HoveredAxis = GizmoAxis.None;
                // Check each axis via proximity to shaft (closest wins)
                if      (ContainsOrNear(mp, _st.RectCenter))  _st.HoveredAxis = GizmoAxis.X;
                else if (ContainsOrNear(mp, _st.RectX))       _st.HoveredAxis = GizmoAxis.X;
                else if (ContainsOrNear(mp, _st.RectY))       _st.HoveredAxis = GizmoAxis.Y;
                else if (ContainsOrNear(mp, _st.RectZ))       _st.HoveredAxis = GizmoAxis.Z;
            }

            // ── Mouse Down: start drag ───────────────────────────────────────
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                GizmoAxis hit = GizmoAxis.None;
                if      (ContainsOrNear(mp, _st.RectCenter)) hit = GizmoAxis.X;   // centre allows X drag; extend later
                else if (ContainsOrNear(mp, _st.RectX))      hit = GizmoAxis.X;
                else if (ContainsOrNear(mp, _st.RectY))      hit = GizmoAxis.Y;
                else if (ContainsOrNear(mp, _st.RectZ))      hit = GizmoAxis.Z;

                if (hit != GizmoAxis.None)
                {
                    _st.IsDragging          = true;
                    _st.ActiveAxis          = hit;
                    _st.DragStartMousePos   = mp;
                    _st.DragStartAddMovePos = _st.CurrentPos;
                    ev.Use();
                }
            }

            // ── Mouse Drag: update addMove ───────────────────────────────────
            if (ev.type == EventType.MouseDrag && _st.IsDragging && ev.button == 0)
            {
                Vector2 delta = mp - _st.DragStartMousePos;

                // Convert pixel delta → world units using depth + FOV
                float depth         = Vector3.Distance(cam.transform.position, worldPos);
                float fovFactor     = 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float unitsPerPixel = (depth * fovFactor) / Screen.height;
                float sens          = AccessoryGizmoPlugin.GizmoSensitivity.Value;

                Vector3 newPos = _st.DragStartAddMovePos;

                switch (_st.ActiveAxis)
                {
                    case GizmoAxis.X:
                    {
                        float d = Vector2.Dot(delta, _st.DirX) * unitsPerPixel * sens;
                        newPos.x = _st.DragStartAddMovePos.x + d;
                        break;
                    }
                    case GizmoAxis.Y:
                    {
                        float d = Vector2.Dot(delta, _st.DirY) * unitsPerPixel * sens;
                        newPos.y = _st.DragStartAddMovePos.y + d;
                        break;
                    }
                    case GizmoAxis.Z:
                    {
                        float d = Vector2.Dot(delta, _st.DirZ) * unitsPerPixel * sens;
                        newPos.z = _st.DragStartAddMovePos.z + d;
                        break;
                    }
                }

                _st.CurrentPos = newPos;
                GizmoAccBridge.TrySetPosition(_accInfo, GizmoAccBridge.MoveAdjustIndex, newPos);
                ev.Use();
            }

            // ── Mouse Up: end drag ───────────────────────────────────────────
            if (ev.type == EventType.MouseUp && ev.button == 0 && _st.IsDragging)
            {
                _st.ResetDrag();
                ev.Use();
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        /// <summary>
        /// Projects a world-space axis direction to normalised IMGUI screen direction.
        /// </summary>
        private static Vector2 ProjectAxis(Camera cam, Vector3 worldPos, Vector3 axis)
        {
            Vector3 tipScreen  = cam.WorldToScreenPoint(worldPos + axis * 0.1f);
            Vector3 baseScreen = cam.WorldToScreenPoint(worldPos);
            // Flip Y (screen is bottom-left, IMGUI is top-left)
            Vector2 dir = new Vector2(tipScreen.x  - baseScreen.x,
                                    -(tipScreen.y  - baseScreen.y));
            return dir.magnitude < 0.001f ? Vector2.right : dir.normalized;
        }

        /// <summary>Builds a padded AABB rect around a shaft segment for click testing.</summary>
        private static Rect ShaftRect(Vector2 a, Vector2 b)
        {
            const float pad = 10f;
            float minX = Mathf.Min(a.x, b.x) - pad;
            float minY = Mathf.Min(a.y, b.y) - pad;
            float maxX = Mathf.Max(a.x, b.x) + pad;
            float maxY = Mathf.Max(a.y, b.y) + pad;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static bool ContainsOrNear(Vector2 point, Rect rect) => rect.Contains(point);
    }
}
