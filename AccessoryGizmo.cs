using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AccessoryGizmo
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInDependency(KKAPI.KoikatuAPI.GUID, "1.4")]
    [BepInDependency("Countd360.StudioCharaEditor.HS2", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("StudioNEOV2.exe")]
    public class AccessoryGizmoPlugin : BaseUnityPlugin
    {
        public const string GUID    = "Countd360.AccessoryGizmo.HS2";
        public const string Name    = "Accessory Gizmo";
        public const string Version = "1.0.0";

        public static AccessoryGizmoPlugin Instance { get; private set; }
        internal static new ManualLogSource Logger;

        // ── Gizmo Controls ──────────────────────────────────────────────────
        public static ConfigEntry<KeyboardShortcut> KeyToggleGizmo    { get; private set; }
        public static ConfigEntry<KeyboardShortcut> KeyToggleWindow   { get; private set; }
        public static ConfigEntry<float>            GizmoSensitivity  { get; private set; }
        public static ConfigEntry<bool>             GizmoLocalSpace   { get; private set; }
        public static ConfigEntry<int>              GizmoMoveIndex    { get; private set; }
        public static ConfigEntry<float>            HandleLength      { get; private set; }
        public static ConfigEntry<float>            HandleWidth       { get; private set; }

        private void Awake()
        {
            Instance = this;
            Logger   = base.Logger;

            // ── Config bindings ────────────────────────────────────────────
            KeyToggleGizmo  = Config.Bind("Gizmo", "Toggle Gizmo Key",
                new KeyboardShortcut(KeyCode.G),
                "Press to show/hide the 3-axis drag gizmo handles.");

            KeyToggleWindow = Config.Bind("Gizmo", "Toggle Window Key",
                new KeyboardShortcut(KeyCode.T, KeyCode.LeftShift),
                "Press to show/hide the Thumbnail & Accessory Applier window.");

            GizmoSensitivity = Config.Bind("Gizmo", "Drag Sensitivity", 1.0f,
                new ConfigDescription(
                    "Multiplier for how fast the accessory moves when dragging. Higher = faster.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            GizmoLocalSpace = Config.Bind("Gizmo", "Use Local Space", false,
                "When true, gizmo arrows align with the accessory's own orientation instead of world axes.");

            GizmoMoveIndex = Config.Bind("Gizmo", "Adjust Slot (0 or 1)", 0,
                new ConfigDescription(
                    "Which addMove slot to control: 0 = Adjust 1, 1 = Adjust 2.",
                    new AcceptableValueRange<int>(0, 1)));

            HandleLength = Config.Bind("Gizmo", "Handle Length (px)", 80f,
                new ConfigDescription("Screen-space pixel length of each axis arrow.",
                    new AcceptableValueRange<float>(30f, 200f)));

            HandleWidth = Config.Bind("Gizmo", "Handle Width (px)", 6f,
                new ConfigDescription("Pixel width of each axis arrow shaft.",
                    new AcceptableValueRange<float>(2f, 20f)));

            // ── Harmony patches ────────────────────────────────────────────
            try
            {
                var harmony = new Harmony(GUID);
                harmony.PatchAll();
                Logger.LogInfo("[AccessoryGizmo] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogError("[AccessoryGizmo] Harmony patch failed: " + ex.Message);
            }

            // ── MonoBehaviour components ───────────────────────────────────
            var go = new GameObject(Name);
            DontDestroyOnLoad(go);

            go.AddComponent<ThumbnailManager>();   // must be first (others depend on it)
            go.AddComponent<GizmoManager>();
            go.AddComponent<GizmoWindowUI>();

            Logger.LogInfo($"[{Name}] v{Version} loaded successfully.");
        }
    }
}
