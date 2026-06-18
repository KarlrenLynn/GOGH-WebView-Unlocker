using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace GOGHWebViewUnlocker;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static ConfigEntry<string> CameraName;

    public override void Load()
    {
        Log = base.Log;
        CameraName = Config.Bind("General", "CameraName", "OBS Virtual Camera", "Webcam device name");
        Log.LogInfo($"v{PluginInfo.PLUGIN_VERSION} Camera: {CameraName.Value}");

        var ip = Path.Combine(Paths.GameRootPath, "BepInEx", "interop");
        foreach (var d in new[] { "Assembly-CSharp.dll", "DomainAssemblyDefinition.dll" })
        { var p = Path.Combine(ip, d); if (File.Exists(p)) try { Assembly.LoadFrom(p); } catch { } }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var an = asm.GetName().Name;
            if (an == null) continue;
            if (!an.StartsWith("Assembly-CSharp") && !an.StartsWith("DomainAssembly")) continue;
            Type[] t; try { t = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { t = ex.Types.Where(x => x != null).ToArray(); }
            catch { continue; }
            foreach (var x in t)
                if (x.Name == "RoomItemObjectHandler") Overlay.Hndl = x;
        }

        ClassInjector.RegisterTypeInIl2Cpp<Overlay>();
        var go = new GameObject("GOGH_Overlay");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Overlay>();
        Log.LogInfo("F9=Panel F10=Refresh");
    }
}

public class Overlay : MonoBehaviour
{
    public static Type Hndl;
    private static object _sharedWebCam;
    private static Texture _sharedTex;
    private static int _camW, _camH;
    private List<ScreenInfo> _screens = new();
    private bool _showPanel = true;
    private Vector2 _scrollPos;
    private int _seqIdx;
    private bool _initialized;

    class ScreenInfo
    {
        public GameObject go;
        public Material material;
        public bool streaming;
        public Texture originalTex;
        public Vector2 origScale = Vector2.one;
        public Vector2 origOffset = Vector2.zero;
    }

    void Start()
    {
        Plugin.Log.LogInfo("F9=Panel F10=Refresh");
        Invoke(nameof(RefreshList), 1f); // Auto-refresh after 1 second
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9)) _showPanel = !_showPanel;
        if (Input.GetKeyDown(KeyCode.F10)) RefreshList();
    }

    void RefreshList()
    {
        foreach (var s in _screens)
            if (s.streaming && s.material != null) { s.material.mainTexture = s.originalTex; s.material.SetTextureScale("_MainTex", s.origScale); s.material.SetTextureOffset("_MainTex", s.origOffset); s.material.SetTexture("_ChangeTex", null); s.streaming = false; }
        
        _screens.Clear();
        _seqIdx = 0;
        _initialized = true;
        if (Hndl == null) return;

        var gcm = typeof(GameObject).GetMethod("GetComponent", Type.EmptyTypes).MakeGenericMethod(Hndl);
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null) continue;
            var c = gcm.Invoke(go, null);
            if (c == null || !(c is MonoBehaviour)) continue;
            var rcs = go.GetComponentsInChildren<Renderer>(true);
            if (rcs.Length < 2) continue;

            var r = rcs[1];
            var mat = r.material;
            if (mat == null) continue;

            _screens.Add(new ScreenInfo { go = go, material = mat, originalTex = mat.mainTexture });
        }
        Plugin.Log.LogInfo($"Found {_screens.Count} screens");
    }

    void EnsureWebCam()
    {
        if (_sharedTex != null) return;
        try
        {
            Type wctType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            { try { foreach (var t in asm.GetTypes()) if (t.Name == "WebCamTexture") { wctType = t; break; } } catch { } if (wctType != null) break; }
            if (wctType == null) { Plugin.Log.LogError("WebCamTexture not found"); return; }

            var camName = Plugin.CameraName.Value;
            var ctor = wctType.GetConstructor(new[] { typeof(string) });
            _sharedWebCam = ctor != null ? ctor.Invoke(new object[] { camName }) : Activator.CreateInstance(wctType);
            wctType.GetMethod("Play")?.Invoke(_sharedWebCam, null);
            _sharedTex = _sharedWebCam as Texture;
            // Log camera dimensions
            var wp = wctType.GetProperty("width"); var hp = wctType.GetProperty("height");
            if (wp != null && hp != null) { _camW = (int)wp.GetValue(_sharedWebCam); _camH = (int)hp.GetValue(_sharedWebCam); }
            Plugin.Log.LogInfo($"WebCamTexture ready: {camName} ({_camW}x{_camH})");
        }
        catch (Exception ex) { Plugin.Log.LogError(ex.Message); }
    }

    void SetStream(ScreenInfo s, bool on)
    {
        if (s.material == null) return;
        if (on)
        {
            EnsureWebCam();
            s.origScale = s.material.GetTextureScale("_MainTex");
            s.origOffset = s.material.GetTextureOffset("_MainTex");
            s.material.mainTexture = _sharedTex;
            s.material.SetTexture("_ChangeTex", null);

            // Calculate aspect-correct tiling
            float texW = s.originalTex != null ? s.originalTex.width : 2048;
            float texH = s.originalTex != null ? s.originalTex.height : 2048;
            if (_camW > 0 && _camH > 0)
            {
                float screenAspect = texW / texH;
                float camAspect = (float)_camW / _camH;
                if (camAspect > screenAspect)
                {
                    // Webcam wider - scale X down (pillarbox)
                    float sx = screenAspect / camAspect;
                    s.material.SetTextureScale("_MainTex", new Vector2(sx, 1));
                    s.material.SetTextureOffset("_MainTex", new Vector2((1 - sx) / 2, 0));
                }
                else
                {
                    // Webcam taller - scale Y down (letterbox)
                    float sy = camAspect / screenAspect;
                    s.material.SetTextureScale("_MainTex", new Vector2(1, sy));
                    s.material.SetTextureOffset("_MainTex", new Vector2(0, (1 - sy) / 2));
                }
                Plugin.Log.LogInfo($"  Stream ON: screen={texW}x{texH} cam={_camW}x{_camH}" +
                    $" scale={s.material.GetTextureScale("_MainTex")} offset={s.material.GetTextureOffset("_MainTex")}");
            }
            else
            {
                s.material.SetTextureScale("_MainTex", Vector2.one);
                s.material.SetTextureOffset("_MainTex", Vector2.zero);
            }
            s.streaming = true;
        }
        else
        {
            s.material.mainTexture = s.originalTex;
            s.material.SetTextureScale("_MainTex", s.origScale);
            s.material.SetTextureOffset("_MainTex", s.origOffset);
            s.material.SetTexture("_ChangeTex", null);
            s.streaming = false;
        }
    }

    void SeqTest()
    {
        if (_screens.Count == 0) return;
        EnsureWebCam();
        if (_seqIdx > 0 && _seqIdx <= _screens.Count) SetStream(_screens[_seqIdx - 1], false);
        else if (_seqIdx == 0) SetStream(_screens[_screens.Count - 1], false);
        if (_seqIdx >= _screens.Count) _seqIdx = 0;
        SetStream(_screens[_seqIdx], true);
        Plugin.Log.LogInfo($"Seq [{_seqIdx + 1}/{_screens.Count}] {_screens[_seqIdx].go.name}");
        _seqIdx++;
    }

    void OnGUI()
    {
        if (!_showPanel || !_initialized || _screens.Count == 0) return;
        var h = Mathf.Min(550, 65 + _screens.Count * 26);
        GUI.Box(new Rect(10, 150, 350, h), $"Stream Control [{Plugin.CameraName.Value}]");

        var y = 22f;
        if (GUI.Button(new Rect(15, y, 70, 22), "Refresh")) RefreshList();
        if (GUI.Button(new Rect(90, y, 60, 22), "All ON")) { EnsureWebCam(); foreach (var s in _screens) SetStream(s, true); }
        if (GUI.Button(new Rect(155, y, 60, 22), "All OFF")) foreach (var s in _screens) SetStream(s, false);
        if (GUI.Button(new Rect(220, y, 60, 22), "Seq Test")) SeqTest();
        if (GUI.Button(new Rect(285, y, 50, 22), "Close")) _showPanel = false;

        y += 28;
        _scrollPos = GUI.BeginScrollView(new Rect(5, y, 340, h - y - 5), _scrollPos, new Rect(0, 0, 320, _screens.Count * 26));
        for (int i = 0; i < _screens.Count; i++)
        {
            var s = _screens[i];
            if (s.go == null || s.material == null) continue;
            var label = s.go.name.Length > 25 ? s.go.name.Substring(0, 25) : s.go.name;
            bool newVal = GUI.Toggle(new Rect(0, i * 26, 310, 24), s.streaming, label);
            if (newVal != s.streaming) SetStream(s, newVal);
        }
        GUI.EndScrollView();
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "com.gogh.webviewunlocker";
    public const string PLUGIN_NAME = "GOGH Stream Overlay";
    public const string PLUGIN_VERSION = "34.0.0";
}