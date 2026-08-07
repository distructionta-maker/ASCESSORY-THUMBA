using UnityEngine;

namespace AccessoryGizmo
{
    /// <summary>
    /// All GL + IMGUI drawing for the 3-axis gizmo handles.
    /// Uses Unity's "Hidden/Internal-Colored" shader for runtime GL lines/quads.
    /// All screen coordinates use IMGUI convention (top-left origin, Y grows down).
    /// GL.LoadPixelMatrix flips Y automatically.
    /// </summary>
    public class GizmoRenderer
    {
        // Palette
        private static readonly Color ColX      = new Color(0.95f, 0.25f, 0.25f, 1f);
        private static readonly Color ColY      = new Color(0.25f, 0.95f, 0.25f, 1f);
        private static readonly Color ColZ      = new Color(0.25f, 0.50f, 0.95f, 1f);
        private static readonly Color ColActive = new Color(1.00f, 0.92f, 0.20f, 1f);
        private static readonly Color ColHover  = new Color(1.00f, 1.00f, 1.00f, 0.90f);
        private static readonly Color ColCenter = new Color(0.95f, 0.95f, 0.95f, 0.95f);
        private static readonly Color ColShadow = new Color(0.00f, 0.00f, 0.00f, 0.40f);

        private Material  _mat;
        private GUIStyle  _labelStyle;
        private GUIStyle  _hudStyle;
        private bool      _stylesInited;

        public void Draw(GizmoState st)
        {
            EnsureMaterial();
            EnsureStyles();

            float hw = AccessoryGizmoPlugin.HandleWidth.Value;
            Color cX = ResolveColor(st, GizmoAxis.X, ColX);
            Color cY = ResolveColor(st, GizmoAxis.Y, ColY);
            Color cZ = ResolveColor(st, GizmoAxis.Z, ColZ);

            Vector2 sh2 = Vector2.one * 2f;
            DrawShaft(st.ScreenCenter + sh2, st.TipX + sh2, hw, ColShadow);
            DrawShaft(st.ScreenCenter + sh2, st.TipY + sh2, hw, ColShadow);
            DrawShaft(st.ScreenCenter + sh2, st.TipZ + sh2, hw, ColShadow);

            DrawShaft(st.ScreenCenter, st.TipX, hw, cX);
            DrawShaft(st.ScreenCenter, st.TipY, hw, cY);
            DrawShaft(st.ScreenCenter, st.TipZ, hw, cZ);

            DrawHead(st.TipX, st.DirX, hw * 2f, 14f, cX);
            DrawHead(st.TipY, st.DirY, hw * 2f, 14f, cY);
            DrawHead(st.TipZ, st.DirZ, hw * 2f, 14f, cZ);

            DrawCircle(st.ScreenCenter, 7f, ColCenter);
            DrawCircle(st.ScreenCenter, 7f, ColShadow, true);

            DrawAxisLabel(st.TipX + new Vector2(10, -10), "X", cX);
            DrawAxisLabel(st.TipY + new Vector2(10, -10), "Y", cY);
            DrawAxisLabel(st.TipZ + new Vector2(10, -10), "Z", cZ);

            if (st.IsDragging || st.HoveredAxis != GizmoAxis.None)
                DrawHUD(st);
        }

        private void DrawShaft(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 dir = to - from;
            if (dir.magnitude < 0.5f) return;
            dir.Normalize();
            Vector2 perp = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            SetupGL();
            GL.Begin(GL.QUADS);
            GL.Color(color);
            Vert(from + perp); Vert(to + perp); Vert(to - perp); Vert(from - perp);
            GL.End();
            GL.PopMatrix();
        }

        private void DrawHead(Vector2 tip, Vector2 dir, float width, float length, Color color)
        {
            if (dir.magnitude < 0.001f) return;
            dir.Normalize();
            Vector2 perp  = new Vector2(-dir.y, dir.x);
            Vector2 baseC = tip - dir * length;
            SetupGL();
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            Vert(tip);
            Vert(baseC + perp * (width * 0.5f));
            Vert(baseC - perp * (width * 0.5f));
            GL.End();
            GL.PopMatrix();
        }

        private void DrawCircle(Vector2 center, float radius, Color color, bool outline = false)
        {
            int segs = 20;
            SetupGL();
            GL.Begin(outline ? GL.LINES : GL.TRIANGLE_FAN);
            GL.Color(color);
            if (!outline) Vert(center);
            for (int i = 0; i <= segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                Vert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            GL.End();
            GL.PopMatrix();
        }

        private void DrawAxisLabel(Vector2 pos, string text, Color color)
        {
            _labelStyle.normal.textColor = color;
            GUI.Label(new Rect(pos.x, pos.y, 24, 20), text, _labelStyle);
        }

        private void DrawHUD(GizmoState st)
        {
            Vector2 origin = st.ScreenCenter + new Vector2(18f, -80f);
            Rect bg = new Rect(origin.x - 6f, origin.y - 6f, 152f, 82f);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawRectBorder(bg, new Color(0.4f, 0.4f, 0.4f, 0.6f));

            Vector3 p = st.CurrentPos;
            float xv = Mathf.Round(p.x * 10000f) / 10000f;
            float yv = Mathf.Round(p.y * 10000f) / 10000f;
            float zv = Mathf.Round(p.z * 10000f) / 10000f;

            Color hX = (st.ActiveAxis == GizmoAxis.X || st.HoveredAxis == GizmoAxis.X) ? ColActive : ColX;
            Color hY = (st.ActiveAxis == GizmoAxis.Y || st.HoveredAxis == GizmoAxis.Y) ? ColActive : ColY;
            Color hZ = (st.ActiveAxis == GizmoAxis.Z || st.HoveredAxis == GizmoAxis.Z) ? ColActive : ColZ;

            DrawHUDLine(new Vector2(origin.x, origin.y),       "X  " + xv.ToString("F4"), hX);
            DrawHUDLine(new Vector2(origin.x, origin.y + 26f), "Y  " + yv.ToString("F4"), hY);
            DrawHUDLine(new Vector2(origin.x, origin.y + 52f), "Z  " + zv.ToString("F4"), hZ);
        }

        private void DrawHUDLine(Vector2 pos, string text, Color color)
        {
            _hudStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(pos.x + 1, pos.y + 1, 140, 22), text, _hudStyle);
            _hudStyle.normal.textColor = color;
            GUI.Label(new Rect(pos.x,     pos.y,     140, 22), text, _hudStyle);
        }

        private void DrawRectBorder(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private Color ResolveColor(GizmoState st, GizmoAxis axis, Color baseColor)
        {
            if (st.ActiveAxis  == axis) return ColActive;
            if (st.HoveredAxis == axis) return ColHover;
            return baseColor;
        }

        private void SetupGL()
        {
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
        }

        private static void Vert(Vector2 p) => GL.Vertex3(p.x, Screen.height - p.y, 0f);

        private void EnsureMaterial()
        {
            if (_mat != null) return;
            Shader sh = Shader.Find("Hidden/Internal-Colored")
                     ?? Shader.Find("GUI/Text Shader");
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void EnsureStyles()
        {
            if (_stylesInited) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold, fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
            };
            _hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold, fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            _stylesInited = true;
        }
    }
}
