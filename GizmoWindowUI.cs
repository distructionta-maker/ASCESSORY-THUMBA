using System;
using System.Collections.Generic;
using System.Reflection;
using AIChara;
using KKAPI.Studio;
using Studio;
using UnityEngine;

namespace AccessoryGizmo
{
    /// <summary>
    /// Combined IMGUI window with three tabs:
    ///   [1] Gizmo Status  — shows live axis values + quick gizmo controls
    ///   [2] Thumbnail     — capture/view custom 256x256 thumbnails per accessory
    ///   [3] Apply Acc     — browse the game's accessory catalogue and equip to selected slot
    /// </summary>
    public class GizmoWindowUI : MonoBehaviour
    {
        private readonly int _winID = 10125;
        private Rect   _winRect = new Rect(10f, 100f, 380f, 500f);
        private bool   _visible;
        private int    _tab;   // 0=Status, 1=Thumbnail, 2=ApplyAcc

        // ── Styles (built once in OnGUI) ─────────────────────────────────────
        private GUIStyle _winStyle;
        private GUIStyle _tabBtn;
        private GUIStyle _activeTabBtn;
        private GUIStyle _headerLabel;
        private GUIStyle _smallLabel;
        private GUIStyle _bigBtn;
        private GUIStyle _dangerBtn;
        private bool     _stylesReady;

        // Placeholder texture for missing thumbnails
        private Texture2D _thumbPlaceholder;

        // ── Applier state ────────────────────────────────────────────────────
        private Vector2 _applierScroll;
        private string  _applierSearch = "";
        private int     _selectedCatIndex;
        private List<ListInfoBase> _applierItems = new List<ListInfoBase>();
        private bool    _applierListDirty = true;

        // Known HS2 accessory category numbers (ao_* range)
        // Scanned dynamically; fallback to common range 351–370
        private static readonly string[] CAT_LABELS =
        {
            "Head Hair","Back Hair","Front Hair","Side Hair","Ext. Hair",
            "Head Acc","Face","Neck","Shoulder","Chest",
            "Waist","Back","Arm","Gloves","Hand",
            "Leg","Panst","Socks","Shoes","Other"
        };
        // Corresponding CategoryNo int values (offset 350 = ao_none base)
        private static readonly int[] CAT_NOS =
        {
            351,352,353,354,355,
            356,357,358,359,360,
            361,362,363,364,365,
            366,367,368,369,370
        };

        // ── Thumbnail gallery state ──────────────────────────────────────────
        private Vector2 _thumbScroll;
        private List<ThumbnailEntry> _thumbEntries = new List<ThumbnailEntry>();
        private bool    _thumbListDirty = true;
        private float   _thumbRefreshTimer;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            _thumbPlaceholder = MakePlaceholderTex();
        }

        private void Update()
        {
            if (AccessoryGizmoPlugin.KeyToggleWindow.Value.IsDown())
                _visible = !_visible;

            if (_visible)
            {
                _thumbRefreshTimer -= Time.deltaTime;
                if (_thumbRefreshTimer <= 0f)
                {
                    _thumbRefreshTimer = 3f;
                    _thumbListDirty = true;
                }
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyles();
            _winRect = GUILayout.Window(_winID, _winRect, DrawWindow, "  ✦  Accessory Gizmo", _winStyle);

            // Block studio camera while mouse is inside window
            if (_winRect.Contains(Event.current.mousePosition))
            {
                if (Studio.Studio.Instance != null)
                    Studio.Studio.Instance.cameraCtrl.noCtrlCondition = () => _winRect.Contains(Event.current.mousePosition);
                Input.ResetInputAxes();
            }
        }

        // ── Window drawing ────────────────────────────────────────────────────

        private void DrawWindow(int id)
        {
            // Tab bar
            GUILayout.BeginHorizontal();
            DrawTab(0, "📊 Status");
            DrawTab(1, "🖼 Thumbnail");
            DrawTab(2, "🎀 Apply Acc");
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            switch (_tab)
            {
                case 0: DrawStatusTab(); break;
                case 1: DrawThumbnailTab(); break;
                case 2: DrawApplierTab(); break;
            }

            GUI.DragWindow(new Rect(0, 0, _winRect.width, 20));
        }

        // ── TAB 0: Gizmo Status ───────────────────────────────────────────────

        private void DrawStatusTab()
        {
            GUILayout.Label("Gizmo Controls", _headerLabel);
            GUILayout.Space(4);

            // Selected accessory info
            if (!GizmoAccBridge.IsAccessoryTabActive)
            {
                HintBox("Open CharaEditor (Shift+D) and switch to the Accessories tab.");
            }
            else
            {
                var accInfo = GizmoAccBridge.GetCurrentAccessoryInfo();
                if (accInfo == null || GizmoAccBridge.IsEmpty(accInfo))
                {
                    HintBox("Select a non-empty accessory slot in CharaEditor.");
                }
                else
                {
                    string accName = GizmoAccBridge.GetAccName(accInfo);
                    GUILayout.Label("Selected:  " + accName, _smallLabel);

                    // Show live addMove position
                    Vector3 pos;
                    GizmoAccBridge.TryGetPosition(accInfo, GizmoAccBridge.MoveAdjustIndex, out pos);

                    GUILayout.Space(6);
                    GUILayout.BeginVertical(GUI.skin.box);

                    GUILayout.Label("Adjust Slot " + (GizmoAccBridge.MoveAdjustIndex + 1) + " Position", _headerLabel);
                    GUILayout.Space(2);

                    DrawAxisRow("X", pos.x, ColX, (v) =>
                    {
                        Vector3 p = pos; p.x = v;
                        GizmoAccBridge.TrySetPosition(accInfo, GizmoAccBridge.MoveAdjustIndex, p);
                    });
                    DrawAxisRow("Y", pos.y, ColY, (v) =>
                    {
                        Vector3 p = pos; p.y = v;
                        GizmoAccBridge.TrySetPosition(accInfo, GizmoAccBridge.MoveAdjustIndex, p);
                    });
                    DrawAxisRow("Z", pos.z, ColZ, (v) =>
                    {
                        Vector3 p = pos; p.z = v;
                        GizmoAccBridge.TrySetPosition(accInfo, GizmoAccBridge.MoveAdjustIndex, p);
                    });

                    if (GUILayout.Button("Reset Position to Zero", _dangerBtn))
                    {
                        GizmoAccBridge.TrySetPosition(accInfo, GizmoAccBridge.MoveAdjustIndex, Vector3.zero);
                    }
                    GUILayout.EndVertical();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("Settings", _headerLabel);
            GUILayout.BeginVertical(GUI.skin.box);

            // Local/World space toggle
            bool local = AccessoryGizmoPlugin.GizmoLocalSpace.Value;
            bool newLocal = GUILayout.Toggle(local, "  Local Space (align arrows with accessory rotation)");
            if (newLocal != local) AccessoryGizmoPlugin.GizmoLocalSpace.Value = newLocal;

            // Sensitivity slider
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sensitivity:", GUILayout.Width(90));
            float sens = GUILayout.HorizontalSlider(AccessoryGizmoPlugin.GizmoSensitivity.Value, 0.1f, 5f);
            GUILayout.Label(sens.ToString("F2"), GUILayout.Width(38));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(sens - AccessoryGizmoPlugin.GizmoSensitivity.Value) > 0.001f)
                AccessoryGizmoPlugin.GizmoSensitivity.Value = sens;

            // Adjust slot
            GUILayout.BeginHorizontal();
            GUILayout.Label("Adjust Slot:", GUILayout.Width(90));
            if (GUILayout.Toggle(GizmoAccBridge.MoveAdjustIndex == 0, " Adjust 1", GUILayout.Width(75)))
                AccessoryGizmoPlugin.GizmoMoveIndex.Value = 0;
            if (GUILayout.Toggle(GizmoAccBridge.MoveAdjustIndex == 1, " Adjust 2", GUILayout.Width(75)))
                AccessoryGizmoPlugin.GizmoMoveIndex.Value = 1;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // ── TAB 1: Thumbnail ─────────────────────────────────────────────────

        private void DrawThumbnailTab()
        {
            // Refresh list if dirty
            if (_thumbListDirty && ThumbnailManager.Instance != null)
            {
                _thumbEntries  = ThumbnailManager.Instance.GetAllEntries();
                _thumbListDirty = false;
            }

            // Current accessory panel
            GUILayout.Label("Current Accessory", _headerLabel);
            var accInfo = GizmoAccBridge.GetCurrentAccessoryInfo();
            bool hasAcc = accInfo != null && !GizmoAccBridge.IsEmpty(accInfo);
            int cat = 0, accId = 0;
            if (hasAcc) GizmoAccBridge.TryGetCategoryAndId(accInfo, out cat, out accId);

            GUILayout.BeginVertical(GUI.skin.box);
            if (!hasAcc)
            {
                HintBox("No accessory selected. Open CharaEditor → Accessories tab.");
            }
            else
            {
                string accName = GizmoAccBridge.GetAccName(accInfo);
                GUILayout.Label(accName, _smallLabel);
                GUILayout.Space(4);

                // Show existing thumbnail or placeholder
                Texture2D thumb = ThumbnailManager.Instance?.GetThumbnail(cat, accId);
                DrawThumbPreview(thumb, 130);

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();

                // Capture button
                GUI.enabled = !ThumbnailManager.Instance.IsCapturing;
                if (GUILayout.Button("📷  Capture 256×256", _bigBtn, GUILayout.Height(30)))
                    ThumbnailManager.Instance.RequestCapture(cat, accId);
                GUI.enabled = true;

                // Delete button
                if (thumb != null)
                {
                    if (GUILayout.Button("🗑", _dangerBtn, GUILayout.Width(30), GUILayout.Height(30)))
                    {
                        ThumbnailManager.Instance.DeleteThumbnail(cat, accId);
                        _thumbListDirty = true;
                    }
                }
                GUILayout.EndHorizontal();

                // Status message
                if (!string.IsNullOrEmpty(ThumbnailManager.Instance.LastMessage))
                    GUILayout.Label(ThumbnailManager.Instance.LastMessage, _smallLabel);
            }
            GUILayout.EndVertical();

            // Gallery of all saved thumbnails
            GUILayout.Space(8);
            GUILayout.Label("Saved Thumbnails (" + _thumbEntries.Count + ")", _headerLabel);
            _thumbScroll = GUILayout.BeginScrollView(_thumbScroll, GUILayout.ExpandHeight(true));

            const int GRID_COLS = 3;
            const int CELL      = 110;
            int col = 0;
            GUILayout.BeginHorizontal();

            foreach (var entry in _thumbEntries)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(CELL));
                Texture2D t = ThumbnailManager.Instance?.GetThumbnail(entry.Category, entry.Id)
                           ?? _thumbPlaceholder;
                if (GUILayout.Button(t, GUILayout.Width(CELL - 8), GUILayout.Height(CELL - 8)))
                { /* could select / copy on click */ }
                GUILayout.Label(entry.Key, _smallLabel, GUILayout.Width(CELL - 8));
                GUILayout.EndVertical();

                col++;
                if (col >= GRID_COLS)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    col = 0;
                }
            }

            if (col > 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        // ── TAB 2: Accessory Applier ─────────────────────────────────────────

        private void DrawApplierTab()
        {
            // Target character selector
            var ociChar = GizmoAccBridge.CurrentOCIChar;
            if (ociChar == null)
            {
                HintBox("Open CharaEditor (Shift+D) and select a character.");
                return;
            }
            ChaControl chaCtrl = ociChar.charInfo;
            GUILayout.Label("Character: " + chaCtrl.chaFile.parameter.fullname, _smallLabel);

            // Destination slot from current selection
            string accKey = GizmoAccBridge.CurrentAccKey;
            int slotNo    = 0;
            if (!string.IsNullOrEmpty(accKey)) int.TryParse(accKey, out slotNo);
            GUILayout.Label("Target slot:  " + (slotNo + 1).ToString("D2") + "  (change in CharaEditor)", _smallLabel);

            GUILayout.Space(4);

            // Category filter
            GUILayout.Label("Category", _headerLabel);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CAT_LABELS.Length; i++)
            {
                if (i > 0 && i % 5 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                GUIStyle s = i == _selectedCatIndex ? _activeTabBtn : _tabBtn;
                if (GUILayout.Button(CAT_LABELS[i], s, GUILayout.Height(22)))
                {
                    _selectedCatIndex = i;
                    _applierListDirty = true;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Rebuild list if needed
            if (_applierListDirty)
            {
                _applierListDirty = false;
                RefreshApplierList(chaCtrl, CAT_NOS[_selectedCatIndex]);
            }

            // Search bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(55));
            string newSearch = GUILayout.TextField(_applierSearch);
            if (newSearch != _applierSearch) { _applierSearch = newSearch; }
            if (GUILayout.Button("✕", GUILayout.Width(24))) _applierSearch = "";
            GUILayout.EndHorizontal();

            // Item list
            _applierScroll = GUILayout.BeginScrollView(_applierScroll, GUILayout.ExpandHeight(true));

            int catNo = CAT_NOS[_selectedCatIndex];

            foreach (var item in _applierItems)
            {
                if (item == null) continue;
                string name = item.Name;
                if (!string.IsNullOrEmpty(_applierSearch) &&
                    name.IndexOf(_applierSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                GUILayout.BeginHorizontal(GUI.skin.box);

                // Thumbnail preview (small)
                Texture2D thumb = ThumbnailManager.Instance?.GetThumbnail(catNo, item.Id)
                               ?? _thumbPlaceholder;
                GUILayout.Label(thumb, GUILayout.Width(48), GUILayout.Height(48));

                GUILayout.BeginVertical();
                GUILayout.Label(name, _smallLabel);
                GUILayout.Label("ID: " + item.Id, _smallLabel);
                GUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Equip", GUILayout.Width(55), GUILayout.Height(44)))
                {
                    ApplyAccessory(chaCtrl, slotNo, catNo, item.Id);
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            GUILayout.EndScrollView();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshApplierList(ChaControl chaCtrl, int catNo)
        {
            _applierItems.Clear();
            try
            {
                var dict = chaCtrl.lstCtrl.GetAllListInfo((ChaListDefine.CategoryNo)catNo);
                if (dict != null)
                    foreach (var kv in dict)
                        _applierItems.Add(kv.Value);
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogWarning("[Applier] Category " + catNo + ": " + ex.Message);
            }
        }

        private void ApplyAccessory(ChaControl chaCtrl, int slotNo, int catNo, int accId)
        {
            try
            {
                chaCtrl.ChangeAccessory(slotNo, catNo, accId, "", true);

                // Tell CharaEditorMgr to refresh its accessory list if loaded
                try
                {
                    Type mgrType = Type.GetType("StudioCharaEditor.CharaEditorMgr, HS2StudioCharaEditor");
                    object inst  = mgrType?.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (inst != null)
                    {
                        // Get controller and call RefreshAccessoriesList
                        var getCtrl = mgrType.GetMethod("GetEditorController",
                            BindingFlags.Instance | BindingFlags.Public, null,
                            new[] { typeof(OCIChar) }, null);
                        object ctrl = getCtrl?.Invoke(inst, new object[] { GizmoAccBridge.CurrentOCIChar });
                        ctrl?.GetType().GetMethod("RefreshAccessoriesList",
                            BindingFlags.Instance | BindingFlags.Public)
                            ?.Invoke(ctrl, null);
                    }
                }
                catch { /* CharaEditor might not be open – that is fine */ }

                AccessoryGizmoPlugin.Logger.LogInfo("[Applier] Applied acc catNo=" + catNo + " id=" + accId + " slot=" + slotNo);
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogError("[Applier] ApplyAccessory failed: " + ex.Message);
            }
        }

        private void DrawAxisRow(string label, float value, Color color, Action<float> onChanged)
        {
            GUILayout.BeginHorizontal();
            Color prev = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(label, GUILayout.Width(14));
            GUI.contentColor = prev;

            string raw = GUILayout.TextField(value.ToString("F4"), GUILayout.Width(80));
            if (float.TryParse(raw, out float parsed) && Mathf.Abs(parsed - value) > 0.000001f)
                onChanged(parsed);

            if (GUILayout.Button("−", GUILayout.Width(22))) onChanged(value - 0.005f);
            if (GUILayout.Button("+", GUILayout.Width(22))) onChanged(value + 0.005f);
            GUILayout.EndHorizontal();
        }

        private void DrawThumbPreview(Texture2D tex, int size)
        {
            Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex ?? _thumbPlaceholder, ScaleMode.ScaleToFit);
        }

        private void DrawTab(int index, string label)
        {
            GUIStyle s = _tab == index ? _activeTabBtn : _tabBtn;
            if (GUILayout.Button(label, s)) _tab = index;
        }

        private void HintBox(string msg)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(msg, _smallLabel);
            GUILayout.EndVertical();
        }

        // ── Style & texture helpers ───────────────────────────────────────────

        private static readonly Color ColX = new Color(0.95f, 0.35f, 0.35f);
        private static readonly Color ColY = new Color(0.35f, 0.95f, 0.35f);
        private static readonly Color ColZ = new Color(0.35f, 0.60f, 0.95f);

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _winStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 13, fontStyle = FontStyle.Bold,
            };

            _tabBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Normal,
                normal    = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                padding   = new RectOffset(4, 4, 2, 2),
            };

            _activeTabBtn = new GUIStyle(_tabBtn)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white, background = MakeColorTex(new Color(0.2f, 0.45f, 0.75f, 0.85f)) },
            };

            _headerLabel = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold, fontSize = 12,
                normal    = { textColor = new Color(0.85f, 0.85f, 1.0f) },
            };

            _smallLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 11,
                wordWrap  = true,
                normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            };

            _bigBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal   = { textColor = Color.white, background = MakeColorTex(new Color(0.15f, 0.50f, 0.15f, 0.9f)) },
                hover    = { textColor = Color.white, background = MakeColorTex(new Color(0.20f, 0.70f, 0.20f, 0.9f)) },
            };

            _dangerBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                normal   = { textColor = Color.white, background = MakeColorTex(new Color(0.55f, 0.10f, 0.10f, 0.9f)) },
                hover    = { textColor = Color.white, background = MakeColorTex(new Color(0.75f, 0.15f, 0.15f, 0.9f)) },
            };

            _stylesReady = true;
        }

        private static Texture2D MakeColorTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D MakePlaceholderTex()
        {
            var t = new Texture2D(64, 64);
            var pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (((i / 64) + (i % 64)) % 2 == 0)
                    ? new Color(0.20f, 0.20f, 0.20f)
                    : new Color(0.28f, 0.28f, 0.28f);
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }
    }
}
