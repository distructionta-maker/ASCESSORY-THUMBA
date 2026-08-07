using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AccessoryGizmo
{
    /// <summary>
    /// Manages capture, save, load, and caching of 256x256 custom accessory thumbnails.
    /// Storage root: BepInEx\Plugins\StudioCharaEditor\ThumbnailCache\custom thumbnails\
    /// File name key: {CategoryNo}_{AccID}.png
    /// </summary>
    public class ThumbnailManager : MonoBehaviour
    {
        public static ThumbnailManager Instance { get; private set; }

        public static string ThumbnailDir { get; private set; }

        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        // Capture state
        private bool    _capturePending;
        private int     _pendingCat;
        private int     _pendingId;
        public  bool    IsCapturing { get; private set; }
        public  string  LastMessage { get; private set; }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;

            // Resolve storage path:
            // Our DLL lives at  BepInEx\Plugins\HS2AccessoryGizmo.dll
            // Target:           BepInEx\Plugins\StudioCharaEditor\ThumbnailCache\custom thumbnails\
            string pluginsDir = Path.GetDirectoryName(typeof(AccessoryGizmoPlugin).Assembly.Location);
            ThumbnailDir = Path.Combine(pluginsDir, "StudioCharaEditor", "ThumbnailCache", "custom thumbnails");

            try { Directory.CreateDirectory(ThumbnailDir); }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogError("[ThumbnailManager] Cannot create thumbnail dir: " + ex.Message);
            }

            AccessoryGizmoPlugin.Logger.LogInfo("[ThumbnailManager] Thumbnail dir: " + ThumbnailDir);
        }

        private void Update()
        {
            // Capture is triggered from GUI thread; coroutine starts here
            if (_capturePending)
            {
                _capturePending = false;
                StartCoroutine(CaptureCoroutine(_pendingCat, _pendingId));
            }
        }

        private void OnDestroy()
        {
            foreach (var tex in _cache.Values)
                if (tex != null) Destroy(tex);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static string MakeKey(int cat, int id) => cat + "_" + id;

        /// <summary>Returns a cached/loaded texture, or null if none saved.</summary>
        public Texture2D GetThumbnail(int cat, int id)
        {
            string key = MakeKey(cat, id);
            Texture2D tex;
            if (_cache.TryGetValue(key, out tex) && tex != null)
                return tex;

            // Try loading from disk
            string path = Path.Combine(ThumbnailDir, key + ".png");
            if (!File.Exists(path)) return null;

            try
            {
                byte[]    data   = File.ReadAllBytes(path);
                Texture2D loaded = new Texture2D(256, 256, TextureFormat.RGB24, false);
                if (ImageConversion.LoadImage(loaded, data))
                {
                    _cache[key] = loaded;
                    return loaded;
                }
                Destroy(loaded);
            }
            catch (Exception ex)
            {
                AccessoryGizmoPlugin.Logger.LogWarning("[ThumbnailManager] Load failed: " + ex.Message);
            }
            return null;
        }

        public bool HasThumbnail(int cat, int id)
            => File.Exists(Path.Combine(ThumbnailDir, MakeKey(cat, id) + ".png"));

        /// <summary>
        /// Schedules a screenshot capture for the given accessory.
        /// The actual capture happens at end-of-frame to avoid mid-draw artifacts.
        /// </summary>
        public void RequestCapture(int cat, int id)
        {
            if (IsCapturing) return;
            _pendingCat     = cat;
            _pendingId      = id;
            _capturePending = true;
            LastMessage     = "Capturing...";
        }

        public void DeleteThumbnail(int cat, int id)
        {
            string key  = MakeKey(cat, id);
            string path = Path.Combine(ThumbnailDir, key + ".png");
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
            if (_cache.ContainsKey(key))
            {
                if (_cache[key] != null) Destroy(_cache[key]);
                _cache.Remove(key);
            }
            LastMessage = "Thumbnail deleted.";
        }

        /// <summary>Returns all stored thumbnail keys found on disk.</summary>
        public List<ThumbnailEntry> GetAllEntries()
        {
            var list = new List<ThumbnailEntry>();
            if (!Directory.Exists(ThumbnailDir)) return list;

            foreach (string f in Directory.GetFiles(ThumbnailDir, "*.png"))
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                string[] parts = stem.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int cat) && int.TryParse(parts[1], out int id))
                    list.Add(new ThumbnailEntry { Key = stem, Category = cat, Id = id });
            }
            return list;
        }

        // ── Capture coroutine ─────────────────────────────────────────────────

        private IEnumerator CaptureCoroutine(int cat, int id)
        {
            IsCapturing = true;
            yield return new WaitForEndOfFrame();

            try
            {
                const int SIZE = 256;
                int sw = Screen.width, sh = Screen.height;
                int cx = Mathf.Max(0, (sw - SIZE) / 2);
                int cy = Mathf.Max(0, (sh - SIZE) / 2);
                int cw = Mathf.Min(SIZE, sw - cx);
                int ch = Mathf.Min(SIZE, sh - cy);

                // Read full screen
                var screenTex = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                screenTex.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                screenTex.Apply();

                // Crop centre 256x256
                Color[] pixels = screenTex.GetPixels(cx, cy, cw, ch);
                Destroy(screenTex);

                var thumb = new Texture2D(SIZE, SIZE, TextureFormat.RGB24, false);
                thumb.SetPixels(0, 0, cw, ch, pixels);
                thumb.Apply();

                // Save
                byte[] png  = ImageConversion.EncodeToPNG(thumb);
                string key  = MakeKey(cat, id);
                string path = Path.Combine(ThumbnailDir, key + ".png");
                File.WriteAllBytes(path, png);

                // Update cache
                if (_cache.ContainsKey(key) && _cache[key] != null)
                    Destroy(_cache[key]);
                _cache[key] = thumb;

                LastMessage = "Thumbnail saved!";
                AccessoryGizmoPlugin.Logger.LogInfo("[ThumbnailManager] Saved: " + path);
            }
            catch (Exception ex)
            {
                LastMessage = "Capture failed: " + ex.Message;
                AccessoryGizmoPlugin.Logger.LogError("[ThumbnailManager] Capture error: " + ex.Message);
            }

            IsCapturing = false;
        }
    }

    public struct ThumbnailEntry
    {
        public string Key;
        public int    Category;
        public int    Id;
    }
}
