using System;
using System.Collections.Generic;
using System.Reflection;
using AIChara;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace AccessoryGizmo
{
    // ============================================================
    //  Static bridge — exposes currently selected accessory info
    //  from CharaEditorUI without modifying StudioCharaEditor source
    // ============================================================
    public static class GizmoAccBridge
    {
        // ── Current selection state (written by Harmony patch below) ────────
        public static string  CurrentAccKey          { get; internal set; }
        public static OCIChar CurrentOCIChar         { get; internal set; }
        public static bool    IsAccessoryTabActive   { get; internal set; }

        public static int MoveAdjustIndex => AccessoryGizmoPlugin.GizmoMoveIndex.Value;

        // ── Cached reflection refs (built once, reused each frame) ───────────
        private static Type        _mgrType;
        private static PropertyInfo _mgrInstanceProp;
        private static MethodInfo   _getCtrlMethod;
        private static FieldInfo    _accListField;

        private static bool _reflectionReady;

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;
            try
            {
                _mgrType        = Type.GetType("StudioCharaEditor.CharaEditorMgr, HS2StudioCharaEditor");
                _mgrInstanceProp = _mgrType?.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                // GetEditorController(OCIChar) overload
                _getCtrlMethod = _mgrType?.GetMethod("GetEditorController",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(OCIChar) }, null);

                _reflectionReady = true;
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogWarning("[GizmoAccBridge] Reflection init failed: " + ex.Message);
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the untyped AccessoryInfo object for the currently selected slot,
        /// or null if nothing is selected / CharaEditorMgr is unavailable.
        /// </summary>
        public static object GetCurrentAccessoryInfo()
        {
            try
            {
                if (CurrentOCIChar == null || string.IsNullOrEmpty(CurrentAccKey)) return null;
                EnsureReflection();
                if (_mgrType == null || _mgrInstanceProp == null || _getCtrlMethod == null) return null;

                object mgrInst = _mgrInstanceProp.GetValue(null);
                if (mgrInst == null) return null;

                object controller = _getCtrlMethod.Invoke(mgrInst, new object[] { CurrentOCIChar });
                if (controller == null) return null;

                // Cache field lookup per controller type
                if (_accListField == null)
                    _accListField = controller.GetType().GetField("myAccessoriesInfo",
                        BindingFlags.Instance | BindingFlags.Public);

                var accList = _accListField?.GetValue(controller) as System.Collections.IList;
                if (accList == null) return null;

                foreach (object accInfo in accList)
                {
                    var keyField = accInfo.GetType().GetField("AccKey",
                        BindingFlags.Instance | BindingFlags.Public);
                    if ((keyField?.GetValue(accInfo) as string) == CurrentAccKey)
                        return accInfo;
                }
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogDebug("[Bridge] GetCurrentAccessoryInfo: " + ex.Message);
            }
            return null;
        }

        /// <summary>Returns position vector from addMove[idx, 0].</summary>
        public static bool TryGetPosition(object accInfoObj, int idx, out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                var partsInfo = GetPartsInfo(accInfoObj);
                if (partsInfo == null) return false;
                var addMove = GetAddMove(partsInfo);
                if (addMove == null) return false;
                pos = (Vector3)addMove.GetValue(idx, 0);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Sets addMove[idx, 0] to newPos and calls UpdateAccessoryMoveFromInfo.
        /// Also mirrors to orgPartsInfo when the slot is a vanilla (≤20) slot.
        /// </summary>
        public static bool TrySetPosition(object accInfoObj, int idx, Vector3 newPos)
        {
            try
            {
                var partsInfo = GetPartsInfo(accInfoObj);
                if (partsInfo == null) return false;

                var addMove = GetAddMove(partsInfo);
                if (addMove == null) return false;

                addMove.SetValue(newPos, idx, 0);

                // Mirror to orgPartsInfo for vanilla slots
                var isVanillaProp = accInfoObj.GetType().GetProperty("IsVanillaSlot",
                    BindingFlags.Instance | BindingFlags.Public);
                bool isVanilla = (bool)(isVanillaProp?.GetValue(accInfoObj) ?? false);
                if (isVanilla)
                {
                    var orgField = accInfoObj.GetType().GetField("orgPartsInfo",
                        BindingFlags.Instance | BindingFlags.Public);
                    object orgParts = orgField?.GetValue(accInfoObj);
                    if (orgParts != null)
                    {
                        var orgAddMove = GetAddMove(orgParts);
                        orgAddMove?.SetValue(newPos, idx, 0);
                    }
                }

                // Apply to game
                int slotNo = GetSlotNo(accInfoObj);
                ChaControl chaCtrl = CurrentOCIChar?.charInfo;
                chaCtrl?.UpdateAccessoryMoveFromInfo(slotNo);
                return true;
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogDebug("[Bridge] TrySetPosition: " + ex.Message);
                return false;
            }
        }

        /// <summary>Returns the world-space Transform.position of the accessory mesh.</summary>
        public static bool TryGetWorldPosition(object accInfoObj, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            try
            {
                var accCmp = GetAccCmp(accInfoObj);
                if (accCmp == null) return false;
                worldPos = accCmp.transform.position;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Returns the world-space rotation of the accessory.</summary>
        public static bool TryGetWorldRotation(object accInfoObj, out Quaternion worldRot)
        {
            worldRot = Quaternion.identity;
            try
            {
                var accCmp = GetAccCmp(accInfoObj);
                if (accCmp == null) return false;
                worldRot = accCmp.transform.rotation;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Returns (category, id) for thumbnail keying.</summary>
        public static bool TryGetCategoryAndId(object accInfoObj, out int category, out int id)
        {
            category = 0; id = 0;
            try
            {
                var partsInfo = GetPartsInfo(accInfoObj);
                if (partsInfo == null) return false;
                var typeF = partsInfo.GetType().GetField("type",   BindingFlags.Instance | BindingFlags.Public);
                var idF   = partsInfo.GetType().GetField("id",     BindingFlags.Instance | BindingFlags.Public);
                category = (int)(typeF?.GetValue(partsInfo) ?? 0);
                id       = (int)(idF?.GetValue(partsInfo)   ?? 0);
                return category > 0;
            }
            catch { return false; }
        }

        /// <summary>Returns whether the accessory slot is empty.</summary>
        public static bool IsEmpty(object accInfoObj)
        {
            try
            {
                var prop = accInfoObj?.GetType().GetProperty("IsEmptySlot",
                    BindingFlags.Instance | BindingFlags.Public);
                return (bool)(prop?.GetValue(accInfoObj) ?? true);
            }
            catch { return true; }
        }

        /// <summary>Returns the accessory display name.</summary>
        public static string GetAccName(object accInfoObj)
        {
            try
            {
                var f = accInfoObj?.GetType().GetField("AccName",
                    BindingFlags.Instance | BindingFlags.Public);
                return f?.GetValue(accInfoObj) as string ?? "???";
            }
            catch { return "???"; }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static object GetPartsInfo(object accInfoObj)
        {
            var f = accInfoObj?.GetType().GetField("partsInfo", BindingFlags.Instance | BindingFlags.Public);
            return f?.GetValue(accInfoObj);
        }

        private static Array GetAddMove(object partsInfo)
        {
            var f = partsInfo?.GetType().GetField("addMove", BindingFlags.Instance | BindingFlags.Public);
            return f?.GetValue(partsInfo) as Array;
        }

        private static MonoBehaviour GetAccCmp(object accInfoObj)
        {
            var f = accInfoObj?.GetType().GetField("accCmp", BindingFlags.Instance | BindingFlags.Public);
            return f?.GetValue(accInfoObj) as MonoBehaviour;
        }

        private static int GetSlotNo(object accInfoObj)
        {
            var f = accInfoObj?.GetType().GetField("slotNo", BindingFlags.Instance | BindingFlags.Public);
            return (int)(f?.GetValue(accInfoObj) ?? 0);
        }
    }

    // ============================================================
    //  Harmony patch — reads selection state from CharaEditorUI
    //  every Update frame via a PostFix on its private Update()
    // ============================================================
    [HarmonyPatch]
    static class CharaEditorUI_UpdatePatch
    {
        static MethodBase TargetMethod()
        {
            try
            {
                Type t = Type.GetType("StudioCharaEditor.CharaEditorUI, HS2StudioCharaEditor");
                if (t == null)
                {
                    AccessoryGizmoPlugin.Logger.LogWarning(
                        "[GizmoAccBridge] CharaEditorUI type not found – gizmo selection tracking disabled.");
                    return null;
                }
                return t.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch { return null; }
        }

        // Postfix runs after the original Update completes
        static void Postfix(object __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);

                // Read the public property VisibleGUI
                bool visible = tr.Property("VisibleGUI").GetValue<bool>();
                if (!visible)
                {
                    GizmoAccBridge.IsAccessoryTabActive = false;
                    return;
                }

                // catelogIndex1 == 4 → Accessories tab
                int catIdx1 = tr.Field("catelogIndex1").GetValue<int>();
                bool isAccTab = (catIdx1 == 4);
                GizmoAccBridge.IsAccessoryTabActive = isAccTab;

                if (isAccTab)
                {
                    OCIChar ociTarget = tr.Field("ociTarget").GetValue<OCIChar>();
                    var multi = tr.Field("accSlotMultiSelection").GetValue<List<string>>();

                    GizmoAccBridge.CurrentOCIChar = ociTarget;
                    GizmoAccBridge.CurrentAccKey  = (multi != null && multi.Count > 0) ? multi[0] : null;
                }
                else
                {
                    GizmoAccBridge.CurrentAccKey = null;
                }
            }
            catch
            {
                // Silent — StudioCharaEditor may not be loaded
            }
        }
    }
}
