using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class UniversalVolumeControllerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.itsjustliliana.universalvolumecontroller";
    public const string PluginName = "Universal Volume Controller";
    public const string PluginVersion = "1.1.0";

    internal static UniversalVolumeControllerPlugin Instance = null!;
    internal static ManualLogSource Log = null!;

    private const int MinPercent = 1;
    private const int MaxPercent = 100;

    private enum SoundCategory
    {
        Item,
        Environment,
        Other
    }

    private Harmony _harmony = null!;
    private bool _uiVisible;
    private Rect _windowRect;
    private bool _windowRectInitialized;
    private bool _draggingWindow;
    private Vector2 _windowDragOffset;
    private bool _resizingWindow;
    private float _resizeStartHeight;
    private float _resizeStartMouseY;
    private bool _settingsVisible;
    private int _selectedTab;
    private Vector2 _scroll;
    private string _searchQuery = string.Empty;
    private bool _isAppFocused = true;
    private bool _cursorStateCaptured;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockState;
    private bool _realtimeRefreshEnabled = true;
    private bool _loggedRealtimeFailure;
    private Coroutine _discoEndOfFrameEnforceCoroutine;
    private bool _isQuitting;
    private float _nextRealtimeRefreshAt;
    private float _nextRealtimeScanAt;

    private readonly object _lock = new object();
    private readonly Dictionary<string, ConfigEntry<int>> _itemEntries = new Dictionary<string, ConfigEntry<int>>();
    private readonly Dictionary<string, ConfigEntry<int>> _environmentEntries = new Dictionary<string, ConfigEntry<int>>();
    private readonly Dictionary<string, ConfigEntry<int>> _otherEntries = new Dictionary<string, ConfigEntry<int>>();
    private readonly Dictionary<string, int> _lastItemNonZeroPercent = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _lastEnvironmentNonZeroPercent = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _lastOtherNonZeroPercent = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
    private ConfigEntry<bool> _categoryEnableTipTarget = null!;
    private string _categoryEnableTipText = string.Empty;
    private float _categoryEnableTipUntil;
    private int _lastGlobalNonZeroPercent = MaxPercent;

    internal ConfigEntry<bool> ModEnabled = null!;
    internal ConfigEntry<int> GlobalPercent = null!;
    internal ConfigEntry<bool> ItemSoundsEnabled = null!;
    internal ConfigEntry<bool> EnvironmentSoundsEnabled = null!;
    internal ConfigEntry<bool> OtherSoundsEnabled = null!;
    internal ConfigEntry<int> ThemeColorIndex = null!;
    internal ConfigEntry<int> OpacityPercent = null!;
    internal ConfigEntry<bool> DebugAudioDiscovery = null!;
    internal ConfigEntry<bool> DebugAudioResolution = null!;
    internal ConfigEntry<bool> DebugInfoByDefault = null!;

    private bool _stylesInitialized;
    private Texture2D _bgTex = null!;
    private Texture2D _panelTex = null!;
    private Texture2D _accentTex = null!;
    private Texture2D _buttonTex = null!;
    private Texture2D _buttonActiveTex = null!;
    private Texture2D _sliderTex = null!;
    private Texture2D _sliderThumbTex = null!;
    private GUIStyle _windowStyle = null!;
    private GUIStyle _headerStyle = null!;
    private GUIStyle _labelStyle = null!;
    private GUIStyle _buttonStyle = null!;
    private GUIStyle _tabStyle = null!;
    private GUIStyle _tabActiveStyle = null!;
    private GUIStyle _tabDimStyle = null!;
    private GUIStyle _sliderStyle = null!;
    private GUIStyle _sliderThumbStyle = null!;
    private GUIStyle _searchFieldStyle = null!;
    private GUIStyle _scrollViewStyle = null!;
    private GUIStyle _vScrollbarStyle = null!;
    private GUIStyle _vScrollbarThumbStyle = null!;
    private GUIStyle _valueStyle = null!;
    private GUIStyle _rowBoxStyle = null!;
    private Texture2D _tabDimTex = null!;
    private Texture2D _scrollbarTrackTex = null!;
    private Texture2D _searchBgTex = null!;
    private int _appliedThemeIndex = -1;
    private int _appliedOpacityPercent = -1;
    private float _nextDiscoProbeAt;
    private bool _sceneHasDiscoBall;
    private bool _dumpedDiscoAudioGraph;
    private bool _debugAudioPlayback;
    private bool _inDepthDebugAudioPlayback;
    private bool _infoDebugAudioPlayback;

    private const float WindowDragStripHeight = 28f;
    private const float WindowResizeGripHeight = 20f;
    private const float CategoryEnableTipDuration = 5f;
    private const string SearchBoxControlName = "UniversalVolumeController_SearchBox";

    private static readonly HashSet<string> SplitWords = new HashSet<string>
    {
        "air", "horn", "clown", "disco", "ball", "double", "winged", "bird", "ship", "main",
        "entrance", "exit", "fire", "apparatus", "beacon", "camera", "helmet", "turret", "mine",
        "door", "battery", "flashlight", "boombox", "walkie", "talkie", "generator", "warning",
        "big", "breaker", "box", "canvas", "crawler", "docile", "locust", "bees", "container",
        "teleporter", "teleport", "teleporta", "player", "players", "light", "speaker", "music",
        "terminal", "monitor", "switch", "lever", "coil", "head", "mask", "hose", "metal"
    };

    private static readonly string[] EnvironmentKeywords =
    {
        "ambience", "ambient", "outside", "outdoor", "wind", "rain", "storm", "thunder", "forest",
        "cave", "factory", "birds", "nature", "ocean", "water", "drip", "reverb",
        "ship", "hangar", "thruster", "engine", "landing", "turbulence", "door"
    };

    private static readonly string[] OtherKeywords =
    {
        "terminal", "intercom", "alarm", "speaker", "jingle", "theme", "ui", "menu", "button", "diegetic", "non-diegetic"
    };

    private static readonly string[] ThemeNames =
    {
        "Orange", "Crimson", "Scarlet", "Amber", "Lime", "Cyan", "Blue", "Indigo", "Magenta", "Rose"
    };

    private static readonly Color[] ThemeColors =
    {
        new Color(1.00f, 0.35f, 0.00f, 1f),
        new Color(0.85f, 0.08f, 0.16f, 1f),
        new Color(0.96f, 0.18f, 0.08f, 1f),
        new Color(0.92f, 0.62f, 0.08f, 1f),
        new Color(0.45f, 0.80f, 0.12f, 1f),
        new Color(0.12f, 0.78f, 0.86f, 1f),
        new Color(0.22f, 0.50f, 0.92f, 1f),
        new Color(0.42f, 0.33f, 0.92f, 1f),
        new Color(0.80f, 0.24f, 0.82f, 1f),
        new Color(0.95f, 0.30f, 0.52f, 1f)
    };

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        ModEnabled = Config.Bind("General", "Enabled", true, "Enable or disable this mod entirely.");
        GlobalPercent = Config.Bind("General", "GlobalPercent", 100, new ConfigDescription("Global volume percentage.", new AcceptableValueRange<int>(0, MaxPercent)));
        ItemSoundsEnabled = Config.Bind("Categories", "ItemSoundsEnabled", true, "Enable Item Sounds category.");
        EnvironmentSoundsEnabled = Config.Bind("Categories", "EnvironmentSoundsEnabled", false, "Enable Environment category.");
        OtherSoundsEnabled = Config.Bind("Categories", "OtherSoundsEnabled", false, "Enable Other sounds category.");
        ThemeColorIndex = Config.Bind("UI", "ThemeColorIndex", 0, new ConfigDescription("Theme color index (0-9).", new AcceptableValueRange<int>(0, 9)));
        OpacityPercent = Config.Bind("UI", "OpacityPercent", 80, new ConfigDescription("Menu opacity in percent (25-100).", new AcceptableValueRange<int>(25, 100)));
        DebugAudioDiscovery = Config.Bind("Debug", "AudioDiscoveryLogs", false, "Log newly tracked audio sources and resolved keys.");
        DebugAudioResolution = Config.Bind("Debug", "AudioResolutionLogs", false, "Log category/key/multiplier decisions for tracked audio sources.");
        DebugInfoByDefault = Config.Bind("Debug", "PlaybackInfoByDefault", false, "Enable additional playback info logs by default for F5 audio debugging.");
        _debugAudioPlayback = false;
        _inDepthDebugAudioPlayback = false;
        _infoDebugAudioPlayback = DebugInfoByDefault.Value;
        if (GlobalPercent.Value > 0)
        {
            _lastGlobalNonZeroPercent = Mathf.Clamp(GlobalPercent.Value, MinPercent, MaxPercent);
        }

        BuildDefaultAliases();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} loaded. Toggle UI with F10.");
        Logger.LogInfo("Volume changes are client-side only (local AudioSource volume).");
        Logger.LogInfo("F9+F10 prints all active audio sources. F5 toggles playback logs, LeftAlt+F5 toggles in-depth logs, LeftCtrl+F5 toggles info logs, LeftShift+F5 prints managed sound groups.");

        if (DebugAudioDiscovery.Value || DebugAudioResolution.Value)
        {
            Logger.LogInfo($"[Debug] discovery={DebugAudioDiscovery.Value} resolution={DebugAudioResolution.Value} items={ItemSoundsEnabled.Value} env={EnvironmentSoundsEnabled.Value} other={OtherSoundsEnabled.Value} opacity={OpacityPercent.Value} theme={ThemeColorIndex.Value}");
        }

        _discoEndOfFrameEnforceCoroutine = StartCoroutine(DiscoEndOfFrameEnforceLoop());
    }

    private void OnDestroy()
    {
        SetMenuVisible(false);

        if (_discoEndOfFrameEnforceCoroutine != null)
        {
            StopCoroutine(_discoEndOfFrameEnforceCoroutine);
            _discoEndOfFrameEnforceCoroutine = null;
        }

        if (_bgTex != null)
        {
            Destroy(_bgTex);
            Destroy(_panelTex);
            Destroy(_accentTex);
            Destroy(_buttonTex);
            Destroy(_buttonActiveTex);
            Destroy(_sliderTex);
            Destroy(_sliderThumbTex);
            Destroy(_tabDimTex);
            Destroy(_scrollbarTrackTex);
            Destroy(_searchBgTex);
        }

        if (!ReferenceEquals(Instance, this))
        {
            return;
        }

        if (_isQuitting && _harmony != null)
        {
            _harmony.UnpatchSelf();
        }
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    private void Update()
    {
        if (!_isAppFocused)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (IsAudioDumpComboPressed(keyboard))
        {
            bool includeAllSources = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            DumpAllActiveAudioSources(includeHierarchy: false, includeExtraInfo: false, includeAllSources: includeAllSources);
            return;
        }

        if (keyboard != null && keyboard.f10Key.wasPressedThisFrame)
        {
            SetMenuVisible(!_uiVisible);
        }

        HandleAudioDebugHotkeys(keyboard);

        if (_uiVisible && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetMenuVisible(false);
        }

        if (_realtimeRefreshEnabled && Time.unscaledTime >= _nextRealtimeRefreshAt)
        {
            _nextRealtimeRefreshAt = Time.unscaledTime + 0.08f;
            try
            {
                VolumeRuntime.RefreshKnownSources();
            }
            catch (Exception ex)
            {
                if (!_loggedRealtimeFailure)
                {
                    _loggedRealtimeFailure = true;
                    Logger.LogError($"Realtime refresh hit an exception and will retry automatically: {ex}");
                }
            }
        }

        if (_realtimeRefreshEnabled && Time.unscaledTime >= _nextRealtimeScanAt)
        {
            _nextRealtimeScanAt = Time.unscaledTime + 1.0f;
            try
            {
                VolumeRuntime.ScanActiveSources();
            }
            catch (Exception ex)
            {
                if (!_loggedRealtimeFailure)
                {
                    _loggedRealtimeFailure = true;
                    Logger.LogError($"Realtime scan hit an exception and will retry automatically: {ex}");
                }
            }
        }

        if (_uiVisible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        if (!_isAppFocused || !_realtimeRefreshEnabled)
        {
            return;
        }

        try
        {
            VolumeRuntime.RefreshDiscoSourcesLate();
        }
        catch
        {
            // Keep frame loop resilient; Update() path already logs refresh/scan exceptions.
        }
    }

    private IEnumerator DiscoEndOfFrameEnforceLoop()
    {
        WaitForEndOfFrame wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;

            if (!_isAppFocused || !_realtimeRefreshEnabled)
            {
                continue;
            }

            try
            {
                VolumeRuntime.RefreshDiscoSourcesLate();
            }
            catch
            {
                // Keep this hard-enforcement loop resilient.
            }
        }
    }

    private void HandleAudioDebugHotkeys(Keyboard keyboard)
    {
        if (keyboard == null || !keyboard.f5Key.wasPressedThisFrame)
        {
            return;
        }

        bool altHeld = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
        bool ctrlHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        bool shiftHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (shiftHeld)
        {
            PrintAllManagedSoundGroups();
            return;
        }

        if (altHeld)
        {
            ToggleInDepthAudioDebug();
            return;
        }

        if (ctrlHeld)
        {
            ToggleInfoAudioDebug();
            return;
        }

        ToggleAudioDebug();
    }

    private static bool IsAudioDumpComboPressed(Keyboard keyboard)
    {
        if (keyboard == null)
        {
            return false;
        }

        bool bothDown = keyboard.f9Key.isPressed && keyboard.f10Key.isPressed;
        bool eitherPressedThisFrame = keyboard.f9Key.wasPressedThisFrame || keyboard.f10Key.wasPressedThisFrame;
        return bothDown && eitherPressedThisFrame;
    }
    private void ToggleAudioDebug()
    {
        _debugAudioPlayback = !_debugAudioPlayback;
        if (!_debugAudioPlayback)
        {
            _inDepthDebugAudioPlayback = false;
        }

        Logger.LogInfo($"[AudioDebug] playback logs {(_debugAudioPlayback ? "enabled" : "disabled")}");
    }

    private void ToggleInDepthAudioDebug()
    {
        _debugAudioPlayback = !_debugAudioPlayback;
        _inDepthDebugAudioPlayback = _debugAudioPlayback;
        _infoDebugAudioPlayback = _debugAudioPlayback;
        Logger.LogInfo($"[AudioDebug] in-depth playback logs {(_debugAudioPlayback ? "enabled" : "disabled")}");
    }

    private void ToggleInfoAudioDebug()
    {
        _infoDebugAudioPlayback = !_infoDebugAudioPlayback;
        Logger.LogInfo($"[AudioDebug] informational playback logs {(_infoDebugAudioPlayback ? "enabled" : "disabled")}");
    }

    private void DumpAllActiveAudioSources(bool includeHierarchy, bool includeExtraInfo, bool includeAllSources)
    {
        AudioSource[] sources = UnityEngine.Object.FindObjectsOfType<AudioSource>(true);

        Logger.LogInfo(" ");
        Logger.LogInfo($"[AudioDump] Active audio snapshot started. sources={sources.Length} includeHierarchy={includeHierarchy} includeExtraInfo={includeExtraInfo} includeAllSources={includeAllSources}");

        int activePlayingCount = 0;
        int printed = 0;
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null)
            {
                continue;
            }

            bool active = source.gameObject != null && source.gameObject.activeInHierarchy;
            bool isPlaying = source.isPlaying;
            if (active && isPlaying)
            {
                activePlayingCount++;
            }

            if (!(active && isPlaying))
            {
                continue;
            }

            printed += LogAudioDumpLine(source, includeHierarchy, includeExtraInfo);
        }

        if (includeAllSources)
        {
            Logger.LogInfo("[AudioDump] Full source list follows (including non-playing sources)");
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null)
                {
                    continue;
                }

                bool active = source.gameObject != null && source.gameObject.activeInHierarchy;
                bool isPlaying = source.isPlaying;
                if (active && isPlaying)
                {
                    continue;
                }

                printed += LogAudioDumpLine(source, includeHierarchy, includeExtraInfo);
            }
        }

        Logger.LogInfo($"[AudioDump] Active audio snapshot finished. activeAndPlaying={activePlayingCount}/{sources.Length} printed={printed}");
        Logger.LogInfo(" ");
    }

    private int LogAudioDumpLine(AudioSource source, bool includeHierarchy, bool includeExtraInfo)
    {
        if (source == null)
        {
            return 0;
        }

        bool active = source.gameObject != null && source.gameObject.activeInHierarchy;
        bool isPlaying = source.isPlaying;
        string sourceName = source.gameObject != null ? source.gameObject.name : "(none)";
        string clipName = source.clip != null ? source.clip.name : "(null)";
        string root = source.transform != null && source.transform.root != null ? source.transform.root.name : "(none)";
        string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";

        Logger.LogInfo($"[AudioDump] id={source.GetInstanceID()} name={sourceName} clip={clipName} playing={isPlaying} active={active} enabled={source.enabled} root={root} volume={source.volume:0.000} pitch={source.pitch:0.000} mute={source.mute} loop={source.loop}");

        int lines = 1;
        if (includeExtraInfo)
        {
            Logger.LogInfo($"[AudioDumpInfo] playOnAwake={source.playOnAwake} spatialBlend={source.spatialBlend:0.00} priority={source.priority} time={source.time:0.000} timeSamples={source.timeSamples} doppler={source.dopplerLevel:0.00} spread={source.spread:0.00} mixer={mixer}");
            lines++;
        }

        if (includeHierarchy)
        {
            Logger.LogInfo($"[AudioDumpPath] {GetHierarchyPath(source.transform)}");
            lines++;
        }

        return lines;
    }

    private void PrintAllManagedSoundGroups()
    {
        lock (_lock)
        {
            Logger.LogInfo(" ");
            Logger.LogInfo("[AudioDebug] Printing all currently managed sound groups...");

            PrintManagedGroup("Item", _itemEntries, _lastItemNonZeroPercent);
            PrintManagedGroup("Environment", _environmentEntries, _lastEnvironmentNonZeroPercent);
            PrintManagedGroup("Other", _otherEntries, _lastOtherNonZeroPercent);

            Logger.LogInfo($"[AudioDebug] GlobalPercent={GlobalPercent.Value}% ModEnabled={ModEnabled.Value} Item={ItemSoundsEnabled.Value} Environment={EnvironmentSoundsEnabled.Value} Other={OtherSoundsEnabled.Value}");
            Logger.LogInfo("[AudioDebug] Finished printing managed sound groups.");
            Logger.LogInfo(" ");
        }
    }

    private void PrintManagedGroup(string label, Dictionary<string, ConfigEntry<int>> entries, Dictionary<string, int> lastNonZero)
    {
        Logger.LogInfo($"[AudioDebug] {label} groups: {entries.Count}");
        foreach (KeyValuePair<string, ConfigEntry<int>> pair in entries.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            int remembered = lastNonZero.TryGetValue(pair.Key, out int cached) ? cached : pair.Value.Value;
            Logger.LogInfo($"[AudioDebug] - {label}/{pair.Key} = {pair.Value.Value}% lastNonZero={remembered}%");
        }
    }

    internal void LogPlaybackEvent(string methodName, AudioSource source, AudioClip clip, float? volumeScale = null, Vector3? position = null)
    {
        if (!_debugAudioPlayback)
        {
            return;
        }

        string clipName = clip != null ? clip.name : "(null)";
        string sourceName = source != null && source.gameObject != null ? source.gameObject.name : "(none)";
        string positionText = position.HasValue ? $" pos={position.Value}" : string.Empty;
        string scaleText = volumeScale.HasValue ? $" scale={volumeScale.Value:0.###}" : string.Empty;

        if (!_inDepthDebugAudioPlayback)
        {
            Logger.LogInfo($"[AudioDebug] {methodName} source={sourceName} clip={clipName}{scaleText}{positionText}");
        }
        else
        {
            string path = source != null ? GetHierarchyPath(source.transform) : "(none)";
            Logger.LogInfo($"[AudioDebug] {methodName} source={sourceName} path={path} clip={clipName}{scaleText}{positionText}");
        }

        if (_infoDebugAudioPlayback && source != null)
        {
            string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";
            Logger.LogInfo($"[AudioDebugInfo] enabled={source.enabled} active={source.gameObject.activeInHierarchy} mute={source.mute} loop={source.loop} playOnAwake={source.playOnAwake} spatialBlend={source.spatialBlend:0.00} volume={source.volume:0.000} mixer={mixer}");
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        _isAppFocused = hasFocus;
        if (!hasFocus)
        {
            SetMenuVisible(false);
            return;
        }

        _nextRealtimeRefreshAt = Time.unscaledTime + 0.1f;
        _nextRealtimeScanAt = Time.unscaledTime + 0.5f;
    }

    private void OnApplicationPause(bool paused)
    {
        _isAppFocused = !paused;
        if (paused)
        {
            SetMenuVisible(false);
        }
    }

    private void OnGUI()
    {
        if (!_uiVisible)
        {
            return;
        }

        EnsureStyles();

        float width = Mathf.Min(980f, Screen.width - 24f);
        float minHeight = Mathf.Min(680f, Screen.height - 24f);
        float maxHeight = minHeight * 2f;
        if (!_windowRectInitialized)
        {
            _windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - minHeight) * 0.5f, width, minHeight);
            _windowRectInitialized = true;
        }
        else
        {
            _windowRect.width = width;
            _windowRect.height = Mathf.Clamp(_windowRect.height, minHeight, maxHeight);
        }

        HandleWindowDrag(_windowRect);
        HandleWindowResize(_windowRect, minHeight, maxHeight);

        _windowRect.x = Mathf.Clamp(_windowRect.x, 12f, Mathf.Max(12f, Screen.width - _windowRect.width - 12f));
        _windowRect.y = Mathf.Clamp(_windowRect.y, 12f, Mathf.Max(12f, Screen.height - 80f));

        Rect rect = _windowRect;

        GUI.Box(rect, GUIContent.none, _windowStyle);
        GUI.Box(new Rect(rect.x + 12f, rect.y + rect.height - 8f, rect.width - 24f, 4f), GUIContent.none);

        GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f));
        GUILayout.Label("Universal Volume Controller", _headerStyle);

        GUILayout.BeginHorizontal();
        DrawTabButton("Item Sounds", 0);
        DrawTabButton("Environment", 1);
        DrawTabButton("Other", 2);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Settings", _buttonStyle, GUILayout.Width(130f), GUILayout.Height(34f)))
        {
            _settingsVisible = !_settingsVisible;
        }

        if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(44f), GUILayout.Height(34f)))
        {
            SetMenuVisible(false);
        }

        GUILayout.EndHorizontal();

        if (_settingsVisible)
        {
            DrawSettingsPanel();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(8f);
        DrawMasterControls();

        GUILayout.Space(8f);
        DrawActiveTab();
        GUILayout.EndArea();
    }

    private void HandleWindowDrag(Rect windowRect)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        Rect dragStrip = new Rect(windowRect.x + 12f, windowRect.y + 8f, windowRect.width - 24f, WindowDragStripHeight);

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && dragStrip.Contains(currentEvent.mousePosition))
        {
            _draggingWindow = true;
            _windowDragOffset = currentEvent.mousePosition - new Vector2(_windowRect.x, _windowRect.y);
            currentEvent.Use();
            return;
        }

        if (_draggingWindow && currentEvent.type == EventType.MouseDrag)
        {
            _windowRect.position = currentEvent.mousePosition - _windowDragOffset;
            currentEvent.Use();
            return;
        }

        if (_draggingWindow && (currentEvent.type == EventType.MouseUp || currentEvent.rawType == EventType.MouseUp))
        {
            _draggingWindow = false;
            currentEvent.Use();
        }
    }

    private void HandleWindowResize(Rect windowRect, float minHeight, float maxHeight)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        Rect resizeGrip = new Rect(
            windowRect.x + 12f,
            windowRect.y + windowRect.height - WindowResizeGripHeight,
            windowRect.width - 24f,
            WindowResizeGripHeight + 6f
        );

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && resizeGrip.Contains(currentEvent.mousePosition))
        {
            _resizingWindow = true;
            _resizeStartHeight = _windowRect.height;
            _resizeStartMouseY = currentEvent.mousePosition.y;
            currentEvent.Use();
            return;
        }

        if (_resizingWindow && currentEvent.type == EventType.MouseDrag)
        {
            float delta = currentEvent.mousePosition.y - _resizeStartMouseY;
            _windowRect.height = Mathf.Clamp(_resizeStartHeight + delta, minHeight, maxHeight);
            currentEvent.Use();
            return;
        }

        if (_resizingWindow && (currentEvent.type == EventType.MouseUp || currentEvent.rawType == EventType.MouseUp))
        {
            _resizingWindow = false;
            currentEvent.Use();
        }
    }

    private void DrawMasterControls()
    {
        GUILayout.BeginVertical(_rowBoxStyle);

        DrawToggleButtonRow("Mod Enabled", ModEnabled);
        DrawToggleButtonRow("Item Sounds Enabled", ItemSoundsEnabled);
        DrawToggleButtonRow("Environment Category Enabled", EnvironmentSoundsEnabled);
        DrawToggleButtonRow("Other Category Enabled", OtherSoundsEnabled);

        GUILayout.BeginHorizontal(_rowBoxStyle, GUILayout.Height(38f));
        GUILayout.Label("Search", _labelStyle, GUILayout.Width(160f));
        GUI.SetNextControlName(SearchBoxControlName);
        string nextSearch = GUILayout.TextField(_searchQuery, _searchFieldStyle, GUILayout.Height(30f), GUILayout.ExpandWidth(true));
        if (string.IsNullOrWhiteSpace(nextSearch) && GUI.GetNameOfFocusedControl() != SearchBoxControlName)
        {
            Rect lastRect = GUILayoutUtility.GetLastRect();
            GUI.Label(new Rect(lastRect.x + 8f, lastRect.y + 4f, lastRect.width - 8f, lastRect.height - 4f), "Search sounds...", _valueStyle);
        }

        if (!string.Equals(nextSearch, _searchQuery, StringComparison.Ordinal))
        {
            _searchQuery = nextSearch;
            _scroll = Vector2.zero;
        }

        if (GUILayout.Button("Clear", _buttonStyle, GUILayout.Width(90f), GUILayout.Height(30f)))
        {
            _searchQuery = string.Empty;
            _scroll = Vector2.zero;
        }

        GUILayout.EndHorizontal();

        DrawGlobalPercentRow();
        GUILayout.EndVertical();
    }

    private void DrawSettingsPanel()
    {
        GUILayout.BeginVertical(_rowBoxStyle);
        GUILayout.Label("Settings", _labelStyle);

        GUILayout.Label("Theme Color", _valueStyle);
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 5; col++)
            {
                int index = row * 5 + col;
                if (index >= ThemeNames.Length)
                {
                    continue;
                }

                GUIStyle colorButtonStyle = ThemeColorIndex.Value == index ? _tabActiveStyle : _buttonStyle;
                if (GUILayout.Button(ThemeNames[index], colorButtonStyle, GUILayout.Width(150f), GUILayout.Height(30f)))
                {
                    ThemeColorIndex.Value = index;
                    Config.Save();
                    ApplyThemeNow();
                }
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal(_rowBoxStyle, GUILayout.Height(44f));
        GUILayout.Label("Opacity", _labelStyle, GUILayout.Width(220f));
        int currentOpacity = Mathf.Clamp(OpacityPercent.Value, 25, 100);
        int sliderOpacity = Mathf.RoundToInt(GUILayout.HorizontalSlider(currentOpacity, 25f, 100f, _sliderStyle, _sliderThumbStyle, GUILayout.Width(420f), GUILayout.Height(22f)));
        if (sliderOpacity != currentOpacity)
        {
            OpacityPercent.Value = sliderOpacity;
            Config.Save();
            ApplyThemeNow();
        }

        GUILayout.Label($"{currentOpacity}%", _valueStyle, GUILayout.Width(78f));
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawActiveTab()
    {
        SoundCategory category = GetCategoryByTab(_selectedTab);
        Dictionary<string, ConfigEntry<int>> dictionary = GetDictionaryByCategory(category);
        bool categoryEnabled = IsCategoryEnabled(category);

        GUILayout.BeginVertical(_rowBoxStyle);
        GUILayout.Label(GetCategoryTitle(category), _labelStyle);
        if (!categoryEnabled)
        {
            GUILayout.Label($"{GetCategoryTitle(category)} is disabled. Enable its category toggle above to apply volume changes.", _valueStyle);
        }

        List<string> keys;
        lock (_lock)
        {
            keys = dictionary.Keys
                .Where(k => MatchesSearch(k))
                .OrderBy(k => k)
                .ToList();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Mute All", _buttonStyle, GUILayout.Width(140f), GUILayout.Height(34f)))
        {
            if (!categoryEnabled)
            {
                ShowEnableCategoryTip(category);
            }
            else
            {
                SetCategoryMuteState(category, dictionary, true);
            }
        }

        if (GUILayout.Button("Unmute All", _buttonStyle, GUILayout.Width(160f), GUILayout.Height(34f)))
        {
            if (!categoryEnabled)
            {
                ShowEnableCategoryTip(category);
            }
            else
            {
                SetCategoryMuteState(category, dictionary, false);
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        _scroll = GUILayout.BeginScrollView(
            _scroll,
            false,
            true,
            GUIStyle.none,
            _vScrollbarStyle,
            _scrollViewStyle,
            GUILayout.ExpandHeight(true)
        );
        if (keys.Count == 0)
        {
            GUILayout.Label("No sounds found for this tab with current search.", _valueStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            return;
        }

        foreach (string key in keys)
        {
            ConfigEntry<int> entry;
            lock (_lock)
            {
                dictionary.TryGetValue(key, out entry);
            }

            if (entry == null)
            {
                continue;
            }

            DrawCategoryPercentRow(category, key, entry, categoryEnabled);
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawGlobalPercentRow()
    {
        GUILayout.BeginHorizontal(_rowBoxStyle, GUILayout.Height(44f));
        GUILayout.Label("Global", _labelStyle, GUILayout.Width(220f));

        int current = GlobalPercent.Value;
        int visualValue = Mathf.Clamp(current, 0, MaxPercent);
        int sliderValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(visualValue, 0f, MaxPercent, _sliderStyle, _sliderThumbStyle, GUILayout.Height(22f), GUILayout.Width(420f)));
        if (sliderValue != visualValue)
        {
            GlobalPercent.Value = sliderValue;
            if (sliderValue > 0)
            {
                _lastGlobalNonZeroPercent = Mathf.Clamp(sliderValue, MinPercent, MaxPercent);
            }

            Config.Save();
            ApplyVolumeRefreshNow();
        }

        bool muted = current <= 0;
        GUILayout.Label(muted ? "MUTE" : $"{current}%", _valueStyle, GUILayout.Width(78f));
        if (GUILayout.Button(muted ? "Unmute" : "Mute", _buttonStyle, GUILayout.Width(84f), GUILayout.Height(30f)))
        {
            if (muted)
            {
                GlobalPercent.Value = Mathf.Clamp(_lastGlobalNonZeroPercent, MinPercent, MaxPercent);
            }
            else
            {
                _lastGlobalNonZeroPercent = Mathf.Clamp(current, MinPercent, MaxPercent);
                GlobalPercent.Value = 0;
            }

            Config.Save();
            ApplyVolumeRefreshNow();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawCategoryPercentRow(SoundCategory category, string key, ConfigEntry<int> entry, bool categoryEnabled)
    {
        GUILayout.BeginHorizontal(_rowBoxStyle, GUILayout.Height(44f));
        GUILayout.Label(ToDisplayName(key), _labelStyle, GUILayout.Width(220f));

        int visualValue = Mathf.Clamp(entry.Value, 0, MaxPercent);
        int sliderValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(visualValue, 0f, MaxPercent, _sliderStyle, _sliderThumbStyle, GUILayout.Height(22f), GUILayout.Width(420f)));
        if (sliderValue != visualValue)
        {
            if (!categoryEnabled)
            {
                ShowEnableCategoryTip(category);
            }
            else
            {
                entry.Value = sliderValue;
                if (sliderValue > 0)
                {
                    RememberLastNonZero(category, key, sliderValue);
                }

                Config.Save();
                ApplyVolumeRefreshNow();
            }
        }

        bool muted = entry.Value <= 0;
        GUILayout.Label(muted ? "MUTE" : $"{entry.Value}%", _valueStyle, GUILayout.Width(78f));
        if (GUILayout.Button(muted ? "Unmute" : "Mute", _buttonStyle, GUILayout.Width(84f), GUILayout.Height(30f)))
        {
            if (!categoryEnabled)
            {
                ShowEnableCategoryTip(category);
            }
            else
            {
                if (muted)
                {
                    entry.Value = GetLastNonZero(category, key);
                }
                else
                {
                    RememberLastNonZero(category, key, entry.Value);
                    entry.Value = 0;
                }

                Config.Save();
                ApplyVolumeRefreshNow();
            }
        }

        GUILayout.EndHorizontal();
    }

    private void SetCategoryMuteState(SoundCategory category, Dictionary<string, ConfigEntry<int>> dictionary, bool mute)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, ConfigEntry<int>> pair in dictionary)
            {
                if (mute)
                {
                    if (pair.Value.Value > 0)
                    {
                        RememberLastNonZero(category, pair.Key, pair.Value.Value);
                    }

                    pair.Value.Value = 0;
                }
                else
                {
                    pair.Value.Value = GetLastNonZero(category, pair.Key);
                }
            }
        }

        Config.Save();
        ApplyVolumeRefreshNow();
    }

    private void ApplyVolumeRefreshNow()
    {
        try
        {
            VolumeRuntime.RefreshKnownSources();
        }
        catch
        {
            // Ignore one-off refresh failures from UI interactions.
        }
    }

    private void RememberLastNonZero(SoundCategory category, string key, int value)
    {
        if (value <= 0)
        {
            return;
        }

        int clamped = Mathf.Clamp(value, MinPercent, MaxPercent);
        Dictionary<string, int> cache = GetLastNonZeroCache(category);
        cache[key] = clamped;
    }

    private int GetLastNonZero(SoundCategory category, string key)
    {
        Dictionary<string, int> cache = GetLastNonZeroCache(category);
        if (cache.TryGetValue(key, out int value) && value > 0)
        {
            return Mathf.Clamp(value, MinPercent, MaxPercent);
        }

        return MaxPercent;
    }

    private Dictionary<string, int> GetLastNonZeroCache(SoundCategory category)
    {
        return category switch
        {
            SoundCategory.Item => _lastItemNonZeroPercent,
            SoundCategory.Environment => _lastEnvironmentNonZeroPercent,
            _ => _lastOtherNonZeroPercent
        };
    }

    private void SetMenuVisible(bool visible)
    {
        if (!visible)
        {
            _draggingWindow = false;
            _resizingWindow = false;
        }

        if (_uiVisible == visible)
        {
            return;
        }

        if (!visible)
        {
            _settingsVisible = false;
        }

        _uiVisible = visible;
        if (visible)
        {
            _previousCursorVisible = Cursor.visible;
            _previousCursorLockState = Cursor.lockState;
            _cursorStateCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ApplyPlayerControlLock(true);
            return;
        }

        if (_cursorStateCaptured)
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockState;
            _cursorStateCaptured = false;
        }

        ApplyPlayerControlLock(false);
    }

    private void ApplyPlayerControlLock(bool locked)
    {
        object localPlayer = GetLocalPlayerController();
        if (localPlayer == null)
        {
            return;
        }

        // Lightweight lock: avoid Disable/EnablePlayerControls calls because they can conflict with FOV mods.
        SetBoolIfExists(localPlayer, "disableMoveInput", locked);
        SetBoolIfExists(localPlayer, "disableLookInput", locked);
        SetBoolIfExists(localPlayer, "isTypingChat", locked);
    }

    private object GetLocalPlayerController()
    {
        Type gmType = AccessTools.TypeByName("GameNetworkManager");
        if (gmType != null)
        {
            object gmInstance = AccessTools.Property(gmType, "Instance")?.GetValue(null)
                ?? AccessTools.Field(gmType, "Instance")?.GetValue(null);
            if (gmInstance != null)
            {
                object fromGm = AccessTools.Field(gmType, "localPlayerController")?.GetValue(gmInstance)
                    ?? AccessTools.Property(gmType, "localPlayerController")?.GetValue(gmInstance);
                if (fromGm != null)
                {
                    return fromGm;
                }
            }
        }

        Type sorType = AccessTools.TypeByName("StartOfRound");
        if (sorType != null)
        {
            object sorInstance = AccessTools.Property(sorType, "Instance")?.GetValue(null)
                ?? AccessTools.Field(sorType, "Instance")?.GetValue(null);
            if (sorInstance != null)
            {
                object fromSor = AccessTools.Field(sorType, "localPlayerController")?.GetValue(sorInstance)
                    ?? AccessTools.Property(sorType, "localPlayerController")?.GetValue(sorInstance);
                if (fromSor != null)
                {
                    return fromSor;
                }
            }
        }

        return null;
    }

    private static void SetBoolIfExists(object target, string fieldName, bool value)
    {
        FieldInfo field = AccessTools.Field(target.GetType(), fieldName);
        if (field != null && field.FieldType == typeof(bool))
        {
            field.SetValue(target, value);
            return;
        }

        PropertyInfo property = AccessTools.Property(target.GetType(), fieldName);
        if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
        {
            property.SetValue(target, value);
        }
    }

    private static string ToDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        string spaced = value.Trim();
        spaced = spaced.Replace("_", " ").Replace("-", " ");
        spaced = Regex.Replace(spaced, "([a-z])([A-Z])", "$1 $2");
        spaced = Regex.Replace(spaced, "([A-Za-z])([0-9])", "$1 $2");
        spaced = Regex.Replace(spaced, "([0-9])([A-Za-z])", "$1 $2");
        spaced = Regex.Replace(spaced, "\\s+", " ").Trim();

        List<string> words = new List<string>();
        foreach (string token in spaced.Split(' '))
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            foreach (string piece in SplitLowercaseToken(token.ToLowerInvariant()))
            {
                words.Add(UppercaseFirst(piece));
            }
        }

        return words.Count == 0 ? "Unknown" : string.Join(" ", words);
    }

    private static IEnumerable<string> SplitLowercaseToken(string token)
    {
        if (token.Length < 7 || token.Any(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            yield return token;
            yield break;
        }

        int n = token.Length;
        int[] prev = Enumerable.Repeat(-1, n + 1).ToArray();
        prev[0] = 0;
        for (int i = 0; i < n; i++)
        {
            if (prev[i] == -1)
            {
                continue;
            }

            for (int j = i + 1; j <= n; j++)
            {
                string part = token.Substring(i, j - i);
                if (!SplitWords.Contains(part))
                {
                    continue;
                }

                if (prev[j] == -1)
                {
                    prev[j] = i;
                }
            }
        }

        if (prev[n] == -1)
        {
            string[] suffixes = { "container", "teleporter", "teleporta", "breakerbox", "door", "box", "bees", "crawler", "horn", "ball" };
            for (int s = 0; s < suffixes.Length; s++)
            {
                string suffix = suffixes[s];
                if (token.Length > suffix.Length + 2 && token.EndsWith(suffix, StringComparison.Ordinal))
                {
                    string head = token.Substring(0, token.Length - suffix.Length);
                    if (SplitWords.Contains(head) || head.Length >= 3)
                    {
                        yield return head;
                        yield return suffix;
                        yield break;
                    }
                }
            }

            yield return token;
            yield break;
        }

        List<string> parts = new List<string>();
        for (int i = n; i > 0; i = prev[i])
        {
            int p = prev[i];
            parts.Add(token.Substring(p, i - p));
        }

        parts.Reverse();
        foreach (string part in parts)
        {
            yield return part;
        }
    }

    private static string UppercaseFirst(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        if (word.Length == 1)
        {
            return char.ToUpperInvariant(word[0]).ToString();
        }

        return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
    }

    private bool MatchesSearch(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            return true;
        }

        string q = _searchQuery.Trim();
        if (q.Length == 0)
        {
            return true;
        }

        string display = ToDisplayName(rawKey);
        return rawKey.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TabHasSearchMatches(int tab)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            return true;
        }

        SoundCategory category = GetCategoryByTab(tab);
        Dictionary<string, ConfigEntry<int>> dictionary = GetDictionaryByCategory(category);
        lock (_lock)
        {
            return dictionary.Keys.Any(MatchesSearch);
        }
    }

    private void DrawTabButton(string title, int index)
    {
        bool hasMatches = TabHasSearchMatches(index);
        GUIStyle style = _selectedTab == index ? _tabActiveStyle : (hasMatches ? _tabStyle : _tabDimStyle);
        if (GUILayout.Button(title, style, GUILayout.Width(220f), GUILayout.Height(38f)))
        {
            _settingsVisible = false;
            _selectedTab = index;
        }
    }

    private void DrawToggleButtonRow(string label, ConfigEntry<bool> entry)
    {
        GUILayout.BeginHorizontal(_rowBoxStyle, GUILayout.Height(38f));
        GUILayout.Label(label, _labelStyle, GUILayout.Width(320f));
        if (GUILayout.Button(entry.Value ? "On" : "Off", _buttonStyle, GUILayout.Width(84f), GUILayout.Height(30f)))
        {
            entry.Value = !entry.Value;
            Config.Save();
        }

        Rect buttonRect = GUILayoutUtility.GetLastRect();
        if (_categoryEnableTipTarget == entry && Time.unscaledTime <= _categoryEnableTipUntil)
        {
            Rect tipRect = new Rect(buttonRect.x - 270f, buttonRect.y + 4f, 260f, Mathf.Max(22f, buttonRect.height));
            GUI.Label(tipRect, _categoryEnableTipText + " ->", _valueStyle);
        }

        GUILayout.EndHorizontal();
    }

    private void ShowEnableCategoryTip(SoundCategory category)
    {
        ConfigEntry<bool> target = GetCategoryToggleEntry(category);
        if (target == null)
        {
            return;
        }

        _categoryEnableTipTarget = target;
        _categoryEnableTipText = "Enable " + GetCategoryTitle(category) + " above";
        _categoryEnableTipUntil = Time.unscaledTime + CategoryEnableTipDuration;
    }

    private ConfigEntry<bool> GetCategoryToggleEntry(SoundCategory category)
    {
        return category switch
        {
            SoundCategory.Item => ItemSoundsEnabled,
            SoundCategory.Environment => EnvironmentSoundsEnabled,
            _ => OtherSoundsEnabled
        };
    }

    private SoundCategory GetCategoryByTab(int tab)
    {
        if (tab == 0)
        {
            return SoundCategory.Item;
        }

        if (tab == 1)
        {
            return SoundCategory.Environment;
        }

        return SoundCategory.Other;
    }

    private Dictionary<string, ConfigEntry<int>> GetDictionaryByCategory(SoundCategory category)
    {
        return category switch
        {
            SoundCategory.Item => _itemEntries,
            SoundCategory.Environment => _environmentEntries,
            _ => _otherEntries
        };
    }

    private bool IsCategoryEnabled(SoundCategory category)
    {
        return category switch
        {
            SoundCategory.Item => ItemSoundsEnabled.Value,
            SoundCategory.Environment => EnvironmentSoundsEnabled.Value,
            _ => OtherSoundsEnabled.Value
        };
    }

    private static string GetCategoryTitle(SoundCategory category)
    {
        return category switch
        {
            SoundCategory.Item => "Item Sounds",
            SoundCategory.Environment => "Environment",
            _ => "Other"
        };
    }

    private void EnsureStyles()
    {
        if (_stylesInitialized)
        {
            ApplyThemeNow();
            return;
        }

        _windowStyle = new GUIStyle(GUI.skin.box);
        _windowStyle.border = new RectOffset(2, 2, 2, 2);

        _headerStyle = new GUIStyle(GUI.skin.label);
        _headerStyle.fontSize = 36;
        _headerStyle.alignment = TextAnchor.MiddleCenter;
        _headerStyle.normal.textColor = new Color(1f, 0.40f, 0.05f, 1f);
        _headerStyle.fontStyle = FontStyle.Bold;

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = 22;
        _labelStyle.normal.textColor = new Color(1f, 0.92f, 0.86f, 1f);

        _valueStyle = new GUIStyle(_labelStyle);
        _valueStyle.fontSize = 18;
        _valueStyle.alignment = TextAnchor.MiddleCenter;

        _buttonStyle = new GUIStyle(GUI.skin.button);
        _buttonStyle.normal.textColor = Color.white;
        _buttonStyle.fontSize = 20;
        _buttonStyle.margin = new RectOffset(4, 4, 4, 4);

        _tabStyle = new GUIStyle(_buttonStyle);
        _tabStyle.fontSize = 22;

        _tabActiveStyle = new GUIStyle(_tabStyle);
        _tabActiveStyle.normal.textColor = new Color(0.12f, 0.02f, 0f, 1f);

        _tabDimStyle = new GUIStyle(_tabStyle);
        _tabDimStyle.normal.textColor = new Color(0.78f, 0.58f, 0.52f, 1f);

        _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
        _sliderStyle.fixedHeight = 20f;
        _sliderStyle.margin = new RectOffset(6, 6, 8, 8);

        _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        _sliderThumbStyle.fixedWidth = 20f;
        _sliderThumbStyle.fixedHeight = 20f;
        _sliderThumbStyle.margin = new RectOffset(0, 0, 0, 0);

        _searchFieldStyle = new GUIStyle(GUI.skin.textField);
        _searchFieldStyle.normal.textColor = new Color(1f, 0.92f, 0.86f, 1f);
        _searchFieldStyle.focused.textColor = new Color(1f, 0.92f, 0.86f, 1f);
        _searchFieldStyle.fontSize = 18;

        _scrollViewStyle = new GUIStyle(GUI.skin.scrollView);

        _vScrollbarStyle = new GUIStyle(GUI.skin.verticalScrollbar);
        _vScrollbarStyle.fixedWidth = 14f;

        _vScrollbarThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb);
        _vScrollbarThumbStyle.fixedWidth = 14f;

        _rowBoxStyle = new GUIStyle(GUI.skin.box);
        _rowBoxStyle.margin = new RectOffset(2, 2, 2, 2);
        _rowBoxStyle.padding = new RectOffset(8, 8, 6, 6);

        ApplyThemeNow();
        _stylesInitialized = true;
    }

    private void ApplyThemeNow()
    {
        int themeIndex = Mathf.Clamp(ThemeColorIndex.Value, 0, ThemeColors.Length - 1);
        int opacity = Mathf.Clamp(OpacityPercent.Value, 25, 100);
        if (themeIndex == _appliedThemeIndex && opacity == _appliedOpacityPercent)
        {
            return;
        }

        _appliedThemeIndex = themeIndex;
        _appliedOpacityPercent = opacity;

        Color accent = ThemeColors[themeIndex];
        float alpha = opacity / 100f;

        Color neutral = new Color(0.07f, 0.05f, 0.07f, 1f);
        Color bg = Color.Lerp(neutral, accent, 0.18f);
        bg.a = 0.90f * alpha;
        Color panel = Color.Lerp(neutral, accent, 0.28f);
        panel.a = 0.95f * alpha;
        Color button = Color.Lerp(neutral, accent, 0.40f);
        button.a = 0.98f * alpha;
        Color buttonActive = Color.Lerp(accent, Color.white, 0.12f);
        buttonActive.a = 1f * alpha;
        Color slider = Color.Lerp(neutral, accent, 0.34f);
        slider.a = 0.96f * alpha;
        Color dim = Color.Lerp(neutral, accent, 0.16f);
        dim.a = 0.86f * alpha;
        Color scrollTrack = Color.Lerp(neutral, accent, 0.24f);
        scrollTrack.a = 1f * alpha;
        Color searchBg = Color.Lerp(neutral, accent, 0.14f);
        searchBg.a = 1f * alpha;

        float textAlpha = Mathf.Clamp01(alpha * 0.95f);
        Color headerText = new Color(accent.r, accent.g, accent.b, textAlpha);
        Color bodyText = new Color(1f, 0.96f, 0.94f, textAlpha);
        Color mutedText = new Color(0.84f, 0.76f, 0.74f, textAlpha);

        ReplaceTexture(ref _bgTex, bg);
        ReplaceTexture(ref _panelTex, panel);
        ReplaceTexture(ref _accentTex, new Color(accent.r, accent.g, accent.b, 1f * alpha));
        ReplaceTexture(ref _buttonTex, button);
        ReplaceTexture(ref _buttonActiveTex, buttonActive);
        ReplaceTexture(ref _sliderTex, slider);
        ReplaceTexture(ref _sliderThumbTex, new Color(accent.r, accent.g * 0.95f, accent.b * 0.70f, 1f * alpha));
        ReplaceTexture(ref _tabDimTex, dim);
        ReplaceTexture(ref _scrollbarTrackTex, scrollTrack);
        ReplaceTexture(ref _searchBgTex, searchBg);

        _windowStyle.normal.background = _bgTex;
        _headerStyle.normal.textColor = headerText;
        _labelStyle.normal.textColor = bodyText;
        _valueStyle.normal.textColor = bodyText;
        _buttonStyle.normal.background = _buttonTex;
        _buttonStyle.active.background = _buttonActiveTex;
        _buttonStyle.hover.background = _buttonActiveTex;
        _buttonStyle.focused.background = _buttonActiveTex;
        _buttonStyle.normal.textColor = bodyText;
        _tabStyle.normal.background = _panelTex;
        _tabStyle.active.background = _buttonActiveTex;
        _tabStyle.hover.background = _buttonActiveTex;
        _tabStyle.focused.background = _buttonActiveTex;
        _tabStyle.normal.textColor = bodyText;
        _tabActiveStyle.normal.background = _accentTex;
        _tabActiveStyle.active.background = _accentTex;
        _tabActiveStyle.hover.background = _accentTex;
        _tabActiveStyle.focused.background = _accentTex;
        _tabActiveStyle.normal.textColor = new Color(0.10f, 0.08f, 0.08f, textAlpha);
        _tabDimStyle.normal.background = _tabDimTex;
        _tabDimStyle.active.background = _tabDimTex;
        _tabDimStyle.hover.background = _tabDimTex;
        _tabDimStyle.focused.background = _tabDimTex;
        _tabDimStyle.normal.textColor = mutedText;
        _sliderStyle.normal.background = _sliderTex;
        _sliderStyle.active.background = _sliderTex;
        _sliderStyle.hover.background = _sliderTex;
        _sliderStyle.focused.background = _sliderTex;
        _sliderThumbStyle.normal.background = _sliderThumbTex;
        _sliderThumbStyle.active.background = _sliderThumbTex;
        _sliderThumbStyle.hover.background = _sliderThumbTex;
        _sliderThumbStyle.focused.background = _sliderThumbTex;
        _searchFieldStyle.normal.background = _searchBgTex;
        _searchFieldStyle.focused.background = _searchBgTex;
        _scrollViewStyle.normal.background = _panelTex;
        _vScrollbarStyle.normal.background = _scrollbarTrackTex;
        _vScrollbarThumbStyle.normal.background = _accentTex;
        _vScrollbarThumbStyle.active.background = _accentTex;
        _vScrollbarThumbStyle.hover.background = _accentTex;
        _vScrollbarThumbStyle.focused.background = _accentTex;
        _rowBoxStyle.normal.background = _panelTex;

        GUI.skin.verticalScrollbar = _vScrollbarStyle;
        GUI.skin.verticalScrollbarThumb = _vScrollbarThumbStyle;
    }

    private void ReplaceTexture(ref Texture2D target, Color color)
    {
        if (target != null)
        {
            Destroy(target);
        }

        target = MakeTex(color);
    }

    private static Texture2D MakeTex(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
    }

    internal string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return "(none)";
        }

        List<string> parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    internal float GetMultiplierForSource(AudioSource source, AudioClip explicitClip = null)
    {
        if (!ModEnabled.Value || source == null)
        {
            return 1f;
        }

        if (!TryResolveGroup(source, explicitClip, out SoundCategory category, out string groupKey))
        {
            return 1f;
        }

        ConfigEntry<int> entry = GetOrCreateGroupEntry(category, groupKey);
        bool categoryEnabled = IsCategoryEnabled(category);
        if (!categoryEnabled)
        {
            return 1f;
        }

        float globalMultiplier = (GlobalPercent.Value <= 0 ? 0f : Mathf.Clamp(GlobalPercent.Value, MinPercent, MaxPercent)) / 100f;
        float groupMultiplier = (entry.Value <= 0 ? 0f : Mathf.Clamp(entry.Value, MinPercent, MaxPercent)) / 100f;
        return Mathf.Clamp(groupMultiplier * globalMultiplier, 0f, 2f);
    }

    internal bool IsDiscoSource(AudioSource source, AudioClip explicitClip = null)
    {
        if (source == null)
        {
            return false;
        }

        return TryResolveGroup(source, explicitClip, out SoundCategory category, out string groupKey)
            && category == SoundCategory.Item
            && string.Equals(groupKey, "discoball", StringComparison.Ordinal);
    }

    internal bool ShouldForceDiscoMute(AudioSource source, AudioClip explicitClip = null)
    {
        if (!ModEnabled.Value || !ItemSoundsEnabled.Value)
        {
            return false;
        }

        ConfigEntry<int> discoEntry = GetOrCreateGroupEntry(SoundCategory.Item, "discoball");
        if (discoEntry.Value > 0)
        {
            return false;
        }

        AudioClip clip = explicitClip ?? source?.clip;
        string clipKey = NormalizePreserveDigits(clip != null ? clip.name : string.Empty);
        if (clipKey.StartsWith("boomboxmusic", StringComparison.Ordinal)
            || clipKey.StartsWith("boombox6questionmark", StringComparison.Ordinal)
            || clipKey.Contains("discoballmusic"))
        {
            return true;
        }

        string objectName = source != null && source.gameObject != null ? source.gameObject.name : string.Empty;
        string rootName = source != null && source.transform != null && source.transform.root != null ? source.transform.root.name : string.Empty;
        string path = source != null ? GetHierarchyPath(source.transform) : string.Empty;
        string combined = (objectName + " " + rootName + " " + path).ToLowerInvariant();

        if (IsDiscoBallText(combined))
        {
            return true;
        }

        if (SceneHasDiscoBall())
        {
            bool looksLikeShipLightPath = (combined.Contains("ship") && combined.Contains("light"))
                || combined.Contains("shiplight")
                || combined.Contains("ship lights")
                || combined.Contains("light switch")
                || combined.Contains("lightswitch")
                || combined.Contains("lightswitchcontainer")
                || combined.Contains("breakerbox")
                || combined.Contains("breaker box");

            if (looksLikeShipLightPath)
            {
                return true;
            }
        }

        return false;
    }

    internal string BuildDebugResolutionLine(AudioSource source, float multiplier)
    {
        if (source == null)
        {
            return "[AudioResolution] null source";
        }

        bool resolved = TryResolveGroup(source, null, out SoundCategory category, out string key);
        string path = GetHierarchyPath(source.transform);
        string root = source.transform != null && source.transform.root != null ? source.transform.root.name : "(none)";
        string clip = source.clip != null ? source.clip.name : "(null)";
        string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";
        string categoryText = resolved ? GetCategoryTitle(category) : "Unresolved";
        string keyText = resolved ? key : "(none)";

        return $"[AudioResolution] id={source.GetInstanceID()} category={categoryText} key={keyText} mult={multiplier:0.000} enabled(mod={ModEnabled.Value},item={ItemSoundsEnabled.Value},env={EnvironmentSoundsEnabled.Value},other={OtherSoundsEnabled.Value}) obj={source.gameObject.name} path={path} root={root} clip={clip} mixer={mixer} playOnAwake={source.playOnAwake} loop={source.loop} mute={source.mute} spatialBlend={source.spatialBlend:0.00} volume={source.volume:0.000}";
    }

    private ConfigEntry<int> GetOrCreateGroupEntry(SoundCategory category, string groupKey)
    {
        Dictionary<string, ConfigEntry<int>> dictionary = GetDictionaryByCategory(category);
        string section = GetCategoryTitle(category);

        lock (_lock)
        {
            if (dictionary.TryGetValue(groupKey, out ConfigEntry<int> existing))
            {
                RememberLastNonZero(category, groupKey, existing.Value);
                return existing;
            }

            ConfigEntry<int> created = Config.Bind(
                section,
                groupKey,
                100,
                new ConfigDescription($"Volume percent for '{groupKey}'. 0 = muted, 1-100 by slider.", new AcceptableValueRange<int>(0, MaxPercent))
            );

            dictionary[groupKey] = created;
            RememberLastNonZero(category, groupKey, created.Value);
            return created;
        }
    }

    private bool TryResolveGroup(AudioSource source, AudioClip explicitClip, out SoundCategory category, out string groupKey)
    {
        category = SoundCategory.Item;
        groupKey = string.Empty;

        if (source == null)
        {
            return false;
        }

        AudioClip resolvedClip = explicitClip != null ? explicitClip : source.clip;
        string clipKey = NormalizePreserveDigits(resolvedClip != null ? resolvedClip.name : string.Empty);
        if (clipKey.StartsWith("boomboxmusic6", StringComparison.Ordinal)
            || clipKey.StartsWith("boomboxmusic", StringComparison.Ordinal)
            || clipKey.StartsWith("boombox6questionmark", StringComparison.Ordinal)
            || clipKey.Contains("discoballmusic"))
        {
            category = SoundCategory.Item;
            groupKey = "discoball";
            return true;
        }

        if (TryForceDiscoBallKey(source, resolvedClip, out string forcedKey))
        {
            category = SoundCategory.Item;
            groupKey = forcedKey;
            return true;
        }

        string raw = string.Empty;

        NoisemakerProp noisemaker = source.GetComponentInParent<NoisemakerProp>();
        if (noisemaker != null && noisemaker.itemProperties != null && !string.IsNullOrWhiteSpace(noisemaker.itemProperties.itemName))
        {
            raw = noisemaker.itemProperties.itemName;
            category = SoundCategory.Item;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            GrabbableObject grabbable = source.GetComponentInParent<GrabbableObject>();
            if (grabbable != null && grabbable.itemProperties != null && !string.IsNullOrWhiteSpace(grabbable.itemProperties.itemName))
            {
                raw = grabbable.itemProperties.itemName;
                category = SoundCategory.Item;
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            PlaceableShipObject placeable = source.GetComponentInParent<PlaceableShipObject>();
            if (placeable != null)
            {
                raw = placeable.gameObject.name;
                category = SoundCategory.Item;
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = source.transform.root != null ? source.transform.root.name : source.gameObject.name;
            category = DetermineNonItemCategory(raw, source);
        }

        string normalized = Normalize(raw);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (category == SoundCategory.Item && _aliases.TryGetValue(normalized, out string aliasTarget))
        {
            normalized = aliasTarget;
        }

        groupKey = normalized;
        return true;
    }

    private bool TryForceDiscoBallKey(AudioSource source, AudioClip explicitClip, out string forcedKey)
    {
        forcedKey = string.Empty;
        string clip = explicitClip != null ? explicitClip.name : (source.clip != null ? source.clip.name : string.Empty);
        string sourceName = source.gameObject != null ? source.gameObject.name : string.Empty;
        string rootName = source.transform != null && source.transform.root != null ? source.transform.root.name : string.Empty;
        string combined = (sourceName + " " + rootName + " " + clip).ToLowerInvariant();
        bool looksLikeShipLightPath = (combined.Contains("ship") && combined.Contains("light"))
            || combined.Contains("shiplight")
            || combined.Contains("ship lights")
            || combined.Contains("light switch")
            || combined.Contains("lightswitch")
            || combined.Contains("set ship lights rpc")
            || combined.Contains("toggling ship lights rpc")
            || combined.Contains("breakerbox")
            || combined.Contains("breaker box");

        if (!looksLikeShipLightPath)
        {
            return false;
        }

        if (!HasDiscoBallControlContext())
        {
            return false;
        }

        forcedKey = "discoball";
        return true;
    }

    private bool HasDiscoBallControlContext()
    {
        lock (_lock)
        {
            if (_itemEntries.ContainsKey("discoball"))
            {
                return true;
            }
        }

        return SceneHasDiscoBall();
    }

    private bool SceneHasDiscoBall()
    {
        if (Time.unscaledTime < _nextDiscoProbeAt)
        {
            return _sceneHasDiscoBall;
        }

        _nextDiscoProbeAt = Time.unscaledTime + 2.0f;
        _sceneHasDiscoBall = false;

        PlaceableShipObject[] placeables = UnityEngine.Object.FindObjectsOfType<PlaceableShipObject>();
        for (int i = 0; i < placeables.Length; i++)
        {
            PlaceableShipObject p = placeables[i];
            if (p == null)
            {
                continue;
            }

            string n = p.gameObject.name.ToLowerInvariant();
            if (n.Contains("discoball") || n.Contains("disco ball") || (n.Contains("disco") && n.Contains("ball")))
            {
                _sceneHasDiscoBall = true;
                DumpDiscoBallAudioGraph(p.transform);
                if (DebugAudioDiscovery.Value)
                {
                    Logger.LogInfo($"[DiscoProbe] found placeable='{p.gameObject.name}' path={GetHierarchyPath(p.transform)}");
                }
                break;
            }
        }

        if (DebugAudioDiscovery.Value && !_sceneHasDiscoBall)
        {
            Logger.LogInfo($"[DiscoProbe] no disco ball found among {placeables.Length} PlaceableShipObject(s)");
        }

        return _sceneHasDiscoBall;
    }

    private void DumpDiscoBallAudioGraph(Transform discoTransform)
    {
        if (_dumpedDiscoAudioGraph || discoTransform == null)
        {
            return;
        }

        _dumpedDiscoAudioGraph = true;

        try
        {
            AudioSource[] sources = discoTransform.GetComponentsInChildren<AudioSource>(true);
            Logger.LogInfo($"[DiscoDump] root={GetHierarchyPath(discoTransform)} audioSources={sources.Length}");

            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null)
                {
                    continue;
                }

                string path = GetHierarchyPath(source.transform);
                string clip = source.clip != null ? source.clip.name : "(null)";
                string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";
                Logger.LogInfo($"[DiscoDump] source={source.gameObject.name} path={path} clip={clip} mixer={mixer} enabled={source.enabled} active={source.gameObject.activeInHierarchy} playOnAwake={source.playOnAwake} loop={source.loop} mute={source.mute} spatialBlend={source.spatialBlend:0.00} volume={source.volume:0.000}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[DiscoDump] failed to dump disco audio graph: {ex.Message}");
        }
    }

    private static SoundCategory DetermineNonItemCategory(string rawName, AudioSource source)
    {
        string sourceName = source != null && source.gameObject != null ? source.gameObject.name : string.Empty;
        string rootName = source != null && source.transform != null && source.transform.root != null ? source.transform.root.name : string.Empty;
        string clipName = source != null && source.clip != null ? source.clip.name : string.Empty;
        string combined = (rawName + " " + sourceName + " " + rootName + " " + clipName).ToLowerInvariant();

        if (IsDiscoBallText(combined))
        {
            return SoundCategory.Item;
        }

        if (rootName.Equals("Environment", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("environment"))
        {
            return SoundCategory.Environment;
        }

        if (rootName.Equals("Systems", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("systems"))
        {
            return SoundCategory.Other;
        }

        if (ContainsAnyKeyword(combined, EnvironmentKeywords))
        {
            return SoundCategory.Environment;
        }

        if (ContainsAnyKeyword(combined, OtherKeywords))
        {
            return SoundCategory.Other;
        }

        return SoundCategory.Other;
    }

    private static bool IsDiscoBallText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.Contains("discoball")
            || value.Contains("disco ball")
            || (value.Contains("disco") && value.Contains("ball"));
    }

    private static bool ContainsAnyKeyword(string value, string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (value.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string s = value.Trim().ToLowerInvariant();
        s = s.Replace("(clone)", string.Empty).Trim();

        int i = s.Length - 1;
        while (i >= 0 && char.IsDigit(s[i]))
        {
            i--;
        }

        if (i < s.Length - 1)
        {
            s = s.Substring(0, i + 1).TrimEnd('_', '-', ' ');
        }

        while (s.Contains("  "))
        {
            s = s.Replace("  ", " ");
        }

        return s;
    }

    private static string NormalizePreserveDigits(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string s = value.Trim().ToLowerInvariant();
        s = s.Replace("(clone)", string.Empty).Trim();
        s = s.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return s;
    }

    private void BuildDefaultAliases()
    {
        _aliases["air horn"] = "airhorn";
        _aliases["loudhorn"] = "airhorn";
        _aliases["disco ball"] = "discoball";
        _aliases["disco_ball"] = "discoball";
        _aliases["discoballcontainer"] = "discoball";
        _aliases["disco ball container"] = "discoball";
        _aliases["disco"] = "discoball";
        _aliases["boomboxmusic6"] = "discoball";
        _aliases["boombox6questionmark"] = "discoball";
        _aliases["disco ball music"] = "discoball";
        _aliases["discoballmusic"] = "discoball";
        _aliases["clown horn"] = "clownhorn";
    }

}

internal static class VolumeRuntime
{
    private static readonly Dictionary<int, float> BaseVolumeBySource = new Dictionary<int, float>();
    private static readonly Dictionary<int, float> LastAppliedMultiplierBySource = new Dictionary<int, float>();
    private static readonly Dictionary<int, AudioSource> KnownSources = new Dictionary<int, AudioSource>();
    private static readonly Dictionary<int, string> LastResolutionBySource = new Dictionary<int, string>();
    private static readonly HashSet<int> LoggedScanSources = new HashSet<int>();
    private static readonly Dictionary<int, bool> PreviousMuteBySource = new Dictionary<int, bool>();
    private static readonly object Sync = new object();

    internal static void ApplyForPlay(AudioSource source, string playbackMethod)
    {
        TrackSource(source);

        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        if (plugin == null || source == null)
        {
            return;
        }

        plugin.LogPlaybackEvent(playbackMethod, source, source.clip);

        if (plugin.ShouldForceDiscoMute(source))
        {
            int discoId = source.GetInstanceID();
            lock (Sync)
            {
                if (!PreviousMuteBySource.ContainsKey(discoId))
                {
                    PreviousMuteBySource[discoId] = source.mute;
                }

                source.mute = true;
                LastAppliedMultiplierBySource[discoId] = 0f;
            }

            return;
        }

        int restoreId = source.GetInstanceID();
        lock (Sync)
        {
            if (PreviousMuteBySource.TryGetValue(restoreId, out bool previousMute))
            {
                source.mute = previousMute;
                PreviousMuteBySource.Remove(restoreId);
            }
        }

        float multiplier = plugin.GetMultiplierForSource(source);
        bool lockManagedBaseVolume = plugin.IsDiscoSource(source, source.clip);
        int id = source.GetInstanceID();
        lock (Sync)
        {
            if (!BaseVolumeBySource.TryGetValue(id, out float baseVolume))
            {
                baseVolume = source.volume;
                BaseVolumeBySource[id] = baseVolume;
                LastAppliedMultiplierBySource[id] = 1f;
            }
            else
            {
                float lastMult = LastAppliedMultiplierBySource.TryGetValue(id, out float lm) ? Mathf.Max(0.0001f, lm) : 1f;
                float expected = baseVolume * lastMult;

                // If another system changed source.volume, absorb that into base volume.
                if (!lockManagedBaseVolume && Mathf.Abs(source.volume - expected) > 0.01f)
                {
                    baseVolume = source.volume / lastMult;
                    BaseVolumeBySource[id] = baseVolume;
                }
            }

            source.volume = Mathf.Clamp(baseVolume * multiplier, 0f, 2f);
            LastAppliedMultiplierBySource[id] = multiplier;
        }

        if (plugin.DebugAudioResolution.Value)
        {
            string resolution = plugin.BuildDebugResolutionLine(source, multiplier);
            bool shouldLog = false;
            lock (Sync)
            {
                if (!LastResolutionBySource.TryGetValue(id, out string previous) || !string.Equals(previous, resolution, StringComparison.Ordinal))
                {
                    LastResolutionBySource[id] = resolution;
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                UniversalVolumeControllerPlugin.Log.LogInfo(resolution);
            }
        }
    }

    internal static void ApplyForOneShot(ref float volumeScale, AudioSource source, AudioClip clip)
    {
        TrackSource(source);

        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        if (plugin == null || source == null)
        {
            return;
        }

        plugin.LogPlaybackEvent("PlayOneShot", source, clip, volumeScale);

        if (plugin.ShouldForceDiscoMute(source, clip))
        {
            volumeScale = 0f;
            return;
        }

        float multiplier = plugin.GetMultiplierForSource(source, clip);
        volumeScale = Mathf.Clamp(volumeScale * multiplier, 0f, 2f);
    }

    internal static void ApplyForClipAtPoint(ref float volume, AudioClip clip, Vector3 position)
    {
        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        if (plugin == null)
        {
            return;
        }

        plugin.LogPlaybackEvent("PlayClipAtPoint", null, clip, volume, position);

        if (plugin.ShouldForceDiscoMute(null, clip))
        {
            volume = 0f;
        }
    }

    internal static void ScanActiveSources()
    {
        AudioSource[] sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        for (int i = 0; i < sources.Length; i++)
        {
            try
            {
                AudioSource source = sources[i];
                TrackSource(source);

                if (plugin != null && plugin.DebugAudioDiscovery.Value && source != null)
                {
                    int id = source.GetInstanceID();
                    bool shouldLog = false;
                    lock (Sync)
                    {
                        if (LoggedScanSources.Add(id))
                        {
                            shouldLog = true;
                        }
                    }

                    if (shouldLog)
                    {
                        string root = SafeGetRootName(source);
                        string path = plugin.GetHierarchyPath(source != null ? source.transform : null);
                        string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";
                        string clip = source.clip != null ? source.clip.name : "(null)";
                        bool active = source.gameObject != null && source.gameObject.activeInHierarchy;
                        UniversalVolumeControllerPlugin.Log.LogInfo($"[AudioScan] id={id} name={SafeGetGameObjectName(source)} path={path} root={root} clip={clip} mixer={mixer} playOnAwake={source.playOnAwake} loop={source.loop} mute={source.mute} enabled={source.enabled} active={active} spatialBlend={source.spatialBlend:0.00} volume={source.volume:0.000}");
                    }
                }
            }
            catch
            {
                // Ignore individual bad sources to keep scan loop alive.
            }
        }

        if (plugin != null && plugin.DebugAudioDiscovery.Value)
        {
            UniversalVolumeControllerPlugin.Log.LogInfo($"[AudioScan] totalSources={sources.Length}");
        }
    }

    internal static void RefreshKnownSources()
    {
        AudioSource[] snapshot;
        lock (Sync)
        {
            snapshot = KnownSources.Values.ToArray();
        }

        for (int i = 0; i < snapshot.Length; i++)
        {
            AudioSource source = snapshot[i];
            if (source == null)
            {
                continue;
            }

            try
            {
                ApplyForPlay(source, "Refresh");
            }
            catch
            {
                // Ignore individual bad sources to keep refresh loop alive.
            }
        }

        List<int> stale = null;
        lock (Sync)
        {
            foreach (KeyValuePair<int, AudioSource> pair in KnownSources)
            {
                if (pair.Value != null)
                {
                    continue;
                }

                stale ??= new List<int>();
                stale.Add(pair.Key);
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    int id = stale[i];
                    KnownSources.Remove(id);
                    BaseVolumeBySource.Remove(id);
                    LastAppliedMultiplierBySource.Remove(id);
                    LastResolutionBySource.Remove(id);
                    LoggedScanSources.Remove(id);
                    PreviousMuteBySource.Remove(id);
                }
            }
        }
    }

    internal static void RefreshDiscoSourcesLate()
    {
        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        if (plugin == null)
        {
            return;
        }

        AudioSource[] snapshot;
        lock (Sync)
        {
            snapshot = KnownSources.Values.ToArray();
        }

        for (int i = 0; i < snapshot.Length; i++)
        {
            AudioSource source = snapshot[i];
            if (source == null)
            {
                continue;
            }

            try
            {
                if (!plugin.IsDiscoSource(source, source.clip))
                {
                    continue;
                }

                ApplyForPlay(source, "LateRefresh");
            }
            catch
            {
                // Ignore per-source issues to preserve late refresh loop.
            }
        }
    }

    private static void TrackSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        int id = source.GetInstanceID();
        bool added = false;
        lock (Sync)
        {
            if (KnownSources.TryGetValue(id, out AudioSource existing) && existing == source)
            {
                return;
            }

            KnownSources[id] = source;
            added = true;
        }

        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        if (added && plugin != null && plugin.DebugAudioDiscovery.Value)
        {
            try
            {
                string root = SafeGetRootName(source);
                string path = plugin.GetHierarchyPath(source.transform);
                string mixer = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "(none)";
                UniversalVolumeControllerPlugin.Log.LogInfo($"[AudioDiscovery] id={id} name={SafeGetGameObjectName(source)} path={path} root={root} clip={(source.clip != null ? source.clip.name : "(null)")} mixer={mixer} playOnAwake={source.playOnAwake} loop={source.loop} mute={source.mute} spatialBlend={source.spatialBlend:0.00} volume={source.volume:0.000}");
            }
            catch (Exception ex)
            {
                UniversalVolumeControllerPlugin.Log.LogWarning($"[AudioDiscovery] failed to describe source {id}: {ex.Message}");
            }
        }
    }

    private static string SafeGetGameObjectName(AudioSource source)
    {
        try
        {
            return source != null && source.gameObject != null ? source.gameObject.name : "(none)";
        }
        catch
        {
            return "(missing)";
        }
    }

    private static string SafeGetRootName(AudioSource source)
    {
        try
        {
            Transform transform = source != null ? source.transform : null;
            if (transform != null && transform.root != null)
            {
                return transform.root.name;
            }
        }
        catch
        {
        }

        return "(none)";
    }
}

[HarmonyPatch(typeof(AudioSource))]
internal static class AudioSourcePlayPatch
{
    [HarmonyPatch(nameof(AudioSource.Play), new Type[] { })]
    [HarmonyPrefix]
    private static void PlayPrefix(AudioSource __instance)
    {
        VolumeRuntime.ApplyForPlay(__instance, "Play");
    }

    [HarmonyPatch(nameof(AudioSource.PlayDelayed), new[] { typeof(float) })]
    [HarmonyPrefix]
    private static void PlayDelayedPrefix(AudioSource __instance)
    {
        VolumeRuntime.ApplyForPlay(__instance, "PlayDelayed");
    }
}

[HarmonyPatch(typeof(AudioSource))]
internal static class AudioSourceOneShotPatch
{
    [HarmonyPatch(nameof(AudioSource.PlayClipAtPoint), new[] { typeof(AudioClip), typeof(Vector3), typeof(float) })]
    [HarmonyPrefix]
    private static void PlayClipAtPointPrefix(AudioClip clip, Vector3 position, ref float volume)
    {
        VolumeRuntime.ApplyForClipAtPoint(ref volume, clip, position);
    }

    [HarmonyPatch(nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip) })]
    [HarmonyPrefix]
    private static bool PlayOneShotSimplePrefix(AudioSource __instance, AudioClip clip)
    {
        UniversalVolumeControllerPlugin plugin = UniversalVolumeControllerPlugin.Instance;
        plugin?.LogPlaybackEvent("PlayOneShot", __instance, clip, 1f);
        if (plugin != null && plugin.ShouldForceDiscoMute(__instance, clip))
        {
            return false;
        }

        return true;
    }

    [HarmonyPatch(nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) })]
    [HarmonyPrefix]
    private static void PlayOneShotPrefix(AudioSource __instance, AudioClip clip, ref float volumeScale)
    {
        VolumeRuntime.ApplyForOneShot(ref volumeScale, __instance, clip);
    }

    [HarmonyPatch("PlayOneShotHelper", new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) })]
    [HarmonyPrefix]
    private static void PlayOneShotHelperPrefix(AudioSource source, AudioClip clip, ref float volumeScale)
    {
        VolumeRuntime.ApplyForOneShot(ref volumeScale, source, clip);
    }
}
