using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using Project.Code.Core.Player.Controllers;
using Project.Code.Core.Player.Movement;
using Project.Code.Gameplay.Controllers;
using Project.Code.Gameplay.Data;
using Project.Code.Gameplay.Interactions;
using Project.Code.Gameplay.Interactions.Pickups;
using Project.Code.Gameplay.Player;
using Project.Code.Gameplay.Player.Controllers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KotamonDevCheat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "local.kotamon.devcheat";
    public const string PluginName = "Kotamon Dev Cheat";
    public const string PluginVersion = "0.3.11";

    internal static ManualLogSource ModLog { get; private set; }
    internal static ConfigFile ModConfig { get; private set; }

    internal static ConfigEntry<KeyCode> MenuKey { get; private set; }
    internal static ConfigEntry<KeyCode> NoclipKey { get; private set; }
    internal static ConfigEntry<KeyCode> WorldSpeedKey { get; private set; }
    internal static ConfigEntry<KeyCode> EspKey { get; private set; }
    internal static ConfigEntry<KeyCode> AutoCleanupKey { get; private set; }
    internal static ConfigEntry<KeyCode> BagAlwaysFullKey { get; private set; }
    internal static ConfigEntry<KeyCode> MaxCollectionKey { get; private set; }
    internal static ConfigEntry<KeyCode> CollectAllTapesKey { get; private set; }

    internal static ConfigEntry<float> NoclipSpeed { get; private set; }
    internal static ConfigEntry<float> WorldSpeedValue { get; private set; }
    internal static ConfigEntry<float> EspDistance { get; private set; }
    internal static ConfigEntry<int> MoneyTarget { get; private set; }

    internal static ConfigEntry<bool> WorldSpeedEnabled { get; private set; }
    internal static ConfigEntry<bool> EspEnabled { get; private set; }
    internal static ConfigEntry<bool> BagAlwaysFullEnabled { get; private set; }

    private CheatBehaviour _behaviour;

    public override void Load()
    {
        ModLog = Log;
        ModConfig = Config;

        MenuKey = Config.Bind("Hotkeys", "Menu", KeyCode.Insert, "Open or close the cheat menu.");
        NoclipKey = Config.Bind("Hotkeys", "Noclip", KeyCode.F1, "Toggle noclip.");
        WorldSpeedKey = Config.Bind("Hotkeys", "WorldSpeed", KeyCode.F2, "Toggle selected world speed.");
        EspKey = Config.Bind("Hotkeys", "ESP", KeyCode.F3, "Toggle junk and card ESP.");
        AutoCleanupKey = Config.Bind("Hotkeys", "AutoCleanup", KeyCode.F4, "Collect all cards, then delete all remaining junk.");
        BagAlwaysFullKey = Config.Bind("Hotkeys", "BagAlwaysFull", KeyCode.F5, "Toggle always-full bag.");
        MaxCollectionKey = Config.Bind("Hotkeys", "MaxCollection", KeyCode.F6, "Complete the card collection at maximum quality.");
        CollectAllTapesKey = Config.Bind("Hotkeys", "CollectAllTapes", KeyCode.F7, "Unlock every cassette in the tape player.");

        NoclipSpeed = Config.Bind("Values", "NoclipSpeed", 10f, "Noclip movement speed.");
        WorldSpeedValue = Config.Bind("Values", "WorldSpeed", 2f, "Time.timeScale while WorldSpeed is enabled.");
        EspDistance = Config.Bind("Values", "EspDistance", 75f, "Maximum ESP distance in metres.");
        MoneyTarget = Config.Bind("Values", "MoneyTarget", 100000, "Exact money amount applied by the menu button.");

        WorldSpeedEnabled = Config.Bind("Toggles", "WorldSpeed", false, "Persist WorldSpeed state.");
        EspEnabled = Config.Bind("Toggles", "ESP", false, "Persist ESP state.");
        BagAlwaysFullEnabled = Config.Bind("Toggles", "BagAlwaysFull", false, "Keep the active junk bag full.");

        ClampConfiguration();
        _behaviour = AddComponent<CheatBehaviour>();
        Log.LogInfo($"Kotamon Dev Cheat {PluginVersion} loaded. {MenuKey.Value}=Menu, {NoclipKey.Value}=Noclip, " +
            $"{WorldSpeedKey.Value}=WorldSpeed, {EspKey.Value}=ESP, {AutoCleanupKey.Value}=Auto Cleanup, " +
            $"{BagAlwaysFullKey.Value}=Full Bag, {MaxCollectionKey.Value}=Max Collection, " +
            $"{CollectAllTapesKey.Value}=All Tapes.");
    }

    public override bool Unload()
    {
        if (_behaviour != null)
            _behaviour.Shutdown();

        if (_behaviour != null)
            Object.Destroy(_behaviour);

        _behaviour = null;
        return true;
    }

    internal static void SaveConfig()
    {
        try
        {
            ModConfig.Save();
        }
        catch (Exception exception)
        {
            ModLog.LogWarning($"Could not save config: {exception.Message}");
        }
    }

    private static void ClampConfiguration()
    {
        NoclipSpeed.Value = Mathf.Clamp(NoclipSpeed.Value, 1f, 50f);
        WorldSpeedValue.Value = Mathf.Clamp(WorldSpeedValue.Value, 0.1f, 5f);
        EspDistance.Value = Mathf.Clamp(EspDistance.Value, 10f, 200f);
        MoneyTarget.Value = Math.Max(0, Math.Min(999999999, MoneyTarget.Value));
        SaveConfig();
    }
}

public sealed class CheatBehaviour : MonoBehaviour
{
    private const float EspRefreshInterval = 0.4f;
    private const int EspMaxTargets = 96;

    private readonly List<JunkPickup> _espTargets = new();
    private readonly HashSet<int> _zoneCardInstanceIds = new();
    private readonly HashSet<int> _zonePartInstanceIds = new();
    private readonly HashSet<int> _zoneCollectibleInstanceIds = new();

    private PlayerNoClipController _nativeNoclip;
    private PlayerCharacterController _player;
    private PlayerMovementController _movement;
    private CharacterController _characterController;
    private PlayerCollectionController _collectionController;
    private PlayerPickupController _pickupController;
    private ParametersController _parametersController;
    private PlayerCameraController _playerCameraController;
    private Camera _camera;

    private bool _fallbackNoclip;
    private bool _movementWasEnabled;
    private bool _characterControllerWasEnabled;
    private bool _menuOpen;
    private bool _previousCursorVisible;
    private bool _cameraControllerWasEnabled;
    private bool _cameraControllerSuppressed;
    private bool _guiErrorLogged;
    private bool _glLineUnavailable;
    private CursorLockMode _previousCursorLock;
    private BindingAction _bindingAction;

    private float _nextEspRefresh;
    private float _nextFragmentRefresh;
    private float _nextBagFillRefresh;
    private float _nextAutomationErrorLog;
    private int _cleanupCardsRemaining;
    private int _cleanupTrashRemaining;
    private int _fragmentPartsCount;
    private int _fragmentPartsNeeded = 5;
    private int _lastCleanupFragmentsCollected;
    private int _lastTapesUnlocked;
    private int _lastMoneyValue = -1;
    private float _fragmentSecondsRemaining = -1f;
    private string _cleanupPhase = "Idle";

    private Material _lineMaterial;
    private Rect _menuRect = new(395f, 20f, 590f, 650f);
    private Rect _statusRect = new(15f, 15f, 375f, 237f);
    private DragTarget _dragTarget;
    private Vector2 _dragOffset;

    private enum BindingAction
    {
        None,
        Menu,
        Noclip,
        WorldSpeed,
        Esp,
        AutoCleanup,
        BagAlwaysFull,
        MaxCollection,
        CollectAllTapes
    }

    private enum DragTarget
    {
        None,
        Menu,
        Status
    }

    private enum CardTargetKind
    {
        None,
        DirtyCard,
        CardFragment,
        Figurine,
        CardBox
    }

    public CheatBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    public void Update()
    {
        if (_bindingAction == BindingAction.None && Input.GetKeyDown(Plugin.MenuKey.Value))
            SetMenuOpen(!_menuOpen);

        if (_menuOpen)
            MaintainMenuInputCapture();

        if (_bindingAction == BindingAction.None)
            ProcessHotkeys();

        ApplyNoclipSpeed();

        if (_fallbackNoclip)
            UpdateFallbackNoclip();

        if (Plugin.BagAlwaysFullEnabled.Value)
            MaintainBagFull();

        if (Plugin.WorldSpeedEnabled.Value)
            Time.timeScale = Plugin.WorldSpeedValue.Value;

        if (Plugin.EspEnabled.Value && Time.realtimeSinceStartup >= _nextEspRefresh)
            RefreshEspTargets();

        // Fragment HUD data is requested only while the menu is visible.  This
        // keeps world loading independent from optional UI-only state reads.
        if (_menuOpen && Time.realtimeSinceStartup >= _nextFragmentRefresh)
            RefreshFragmentState();

    }

    public void LateUpdate()
    {
        if (_menuOpen)
            MaintainMenuInputCapture();
    }

    public void OnGUI()
    {
        try
        {
            GUI.depth = -1000;
            CaptureRebindEvent();

            if (Plugin.EspEnabled.Value)
                DrawEsp();

            if (_menuOpen)
                DrawMenu();

            DrawCompactStatus();
        }
        catch (Exception exception)
        {
            if (_guiErrorLogged)
                return;

            _guiErrorLogged = true;
            Plugin.ModLog.LogError($"Menu/ESP drawing failed: {exception}");
        }
    }

    [HideFromIl2Cpp]
    internal void Shutdown()
    {
        SetMenuOpen(false);

        if (_fallbackNoclip)
            SetFallbackNoclip(false);

        try
        {
            if (_nativeNoclip != null && _nativeNoclip.isNoclip)
                _nativeNoclip.Toggle();
        }
        catch (Exception exception)
        {
            Plugin.ModLog.LogWarning($"Could not disable native noclip during unload: {exception.Message}");
        }

        if (_lineMaterial != null)
            Object.Destroy(_lineMaterial);

        Time.timeScale = 1f;
    }

    [HideFromIl2Cpp]
    private void ProcessHotkeys()
    {
        if (Input.GetKeyDown(Plugin.NoclipKey.Value))
            ToggleNoclip();

        if (Input.GetKeyDown(Plugin.WorldSpeedKey.Value))
            SetWorldSpeedEnabled(!Plugin.WorldSpeedEnabled.Value);

        if (Input.GetKeyDown(Plugin.EspKey.Value))
            SetEspEnabled(!Plugin.EspEnabled.Value);

        if (Input.GetKeyDown(Plugin.AutoCleanupKey.Value))
            RunAutoCleanup();

        if (Input.GetKeyDown(Plugin.BagAlwaysFullKey.Value))
            SetBagAlwaysFullEnabled(!Plugin.BagAlwaysFullEnabled.Value);

        if (Input.GetKeyDown(Plugin.MaxCollectionKey.Value))
            CompleteMaxCollection();

        if (Input.GetKeyDown(Plugin.CollectAllTapesKey.Value))
            CollectAllTapes();
    }

    [HideFromIl2Cpp]
    private void SetMenuOpen(bool open)
    {
        if (_menuOpen == open)
            return;

        _menuOpen = open;
        _bindingAction = BindingAction.None;

        if (open)
        {
            _previousCursorVisible = Cursor.visible;
            _previousCursorLock = Cursor.lockState;
            MaintainMenuInputCapture();
        }
        else
        {
            RestoreCameraController();
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLock;
            _dragTarget = DragTarget.None;
        }
    }

    [HideFromIl2Cpp]
    private void MaintainMenuInputCapture()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        try
        {
            if (_cameraControllerSuppressed && _playerCameraController == null)
                _cameraControllerSuppressed = false;

            if (!_cameraControllerSuppressed)
            {
                _playerCameraController = Object.FindObjectOfType<PlayerCameraController>();
                if (_playerCameraController != null)
                {
                    _cameraControllerWasEnabled = _playerCameraController.enabled;
                    _playerCameraController.enabled = false;
                    _cameraControllerSuppressed = true;
                }
            }
            else if (_playerCameraController.enabled)
            {
                _playerCameraController.enabled = false;
            }
        }
        catch (Exception exception)
        {
            LogAutomationError("Menu input capture", exception);
        }
    }

    [HideFromIl2Cpp]
    private void RestoreCameraController()
    {
        try
        {
            if (_cameraControllerSuppressed && _playerCameraController != null)
                _playerCameraController.enabled = _cameraControllerWasEnabled;
        }
        catch (Exception exception)
        {
            Plugin.ModLog.LogWarning($"Could not restore player camera: {exception.Message}");
        }

        _cameraControllerSuppressed = false;
        _playerCameraController = null;
    }

    [HideFromIl2Cpp]
    private void ToggleNoclip()
    {
        try
        {
            _nativeNoclip = Object.FindObjectOfType<PlayerNoClipController>();
            if (_nativeNoclip != null)
            {
                if (_fallbackNoclip)
                    SetFallbackNoclip(false);

                _nativeNoclip.moveSpeed = Plugin.NoclipSpeed.Value;
                _nativeNoclip.Toggle();
                Plugin.ModLog.LogInfo($"Noclip: {(_nativeNoclip.isNoclip ? "ON" : "OFF")}, speed={Plugin.NoclipSpeed.Value:0.0}");
                return;
            }
        }
        catch (Exception exception)
        {
            Plugin.ModLog.LogWarning($"Native noclip failed, using fallback: {exception.Message}");
        }

        SetFallbackNoclip(!_fallbackNoclip);
    }

    [HideFromIl2Cpp]
    private bool IsNoclipEnabled()
    {
        try
        {
            return (_nativeNoclip != null && _nativeNoclip.isNoclip) || _fallbackNoclip;
        }
        catch
        {
            _nativeNoclip = null;
            return _fallbackNoclip;
        }
    }

    [HideFromIl2Cpp]
    private void ApplyNoclipSpeed()
    {
        try
        {
            if (_nativeNoclip != null)
                _nativeNoclip.moveSpeed = Plugin.NoclipSpeed.Value;
        }
        catch
        {
            _nativeNoclip = null;
        }
    }

    [HideFromIl2Cpp]
    private void SetFallbackNoclip(bool enabled)
    {
        if (enabled)
        {
            _player = Object.FindObjectOfType<PlayerCharacterController>();
            if (_player == null)
            {
                Plugin.ModLog.LogWarning("Noclip: PlayerCharacterController was not found.");
                return;
            }

            _movement = _player.GetComponent<PlayerMovementController>();
            _characterController = _player.GetComponent<CharacterController>();

            if (_movement != null)
            {
                _movementWasEnabled = _movement.enabled;
                _movement.enabled = false;
            }

            if (_characterController != null)
            {
                _characterControllerWasEnabled = _characterController.enabled;
                _characterController.enabled = false;
            }

            _fallbackNoclip = true;
        }
        else
        {
            if (_movement != null)
                _movement.enabled = _movementWasEnabled;

            if (_characterController != null)
                _characterController.enabled = _characterControllerWasEnabled;

            _fallbackNoclip = false;
        }

        Plugin.ModLog.LogInfo($"Noclip fallback: {(_fallbackNoclip ? "ON" : "OFF")}");
    }

    [HideFromIl2Cpp]
    private void UpdateFallbackNoclip()
    {
        if (_player == null)
        {
            SetFallbackNoclip(false);
            return;
        }

        _camera = Camera.main;
        if (_camera == null)
            return;

        var direction = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) direction += _camera.transform.forward;
        if (Input.GetKey(KeyCode.S)) direction -= _camera.transform.forward;
        if (Input.GetKey(KeyCode.D)) direction += _camera.transform.right;
        if (Input.GetKey(KeyCode.A)) direction -= _camera.transform.right;
        if (Input.GetKey(KeyCode.Space)) direction += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl)) direction -= Vector3.up;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        var boost = Input.GetKey(KeyCode.LeftShift) ? 3f : 1f;
        _player.transform.position += direction.normalized * Plugin.NoclipSpeed.Value * boost * Time.unscaledDeltaTime;
    }

    [HideFromIl2Cpp]
    private void SetWorldSpeedEnabled(bool enabled)
    {
        Plugin.WorldSpeedEnabled.Value = enabled;
        Time.timeScale = enabled ? Plugin.WorldSpeedValue.Value : 1f;
        Plugin.SaveConfig();
        Plugin.ModLog.LogInfo($"WorldSpeed: {(enabled ? $"ON ({Plugin.WorldSpeedValue.Value:0.00}x)" : "OFF")}");
    }

    [HideFromIl2Cpp]
    private void SetEspEnabled(bool enabled)
    {
        Plugin.EspEnabled.Value = enabled;
        _nextEspRefresh = 0f;
        if (!enabled)
            _espTargets.Clear();
        Plugin.SaveConfig();
        Plugin.ModLog.LogInfo($"ESP: {(enabled ? "ON" : "OFF")}");
    }

    [HideFromIl2Cpp]
    private void SetBagAlwaysFullEnabled(bool enabled)
    {
        Plugin.BagAlwaysFullEnabled.Value = enabled;
        _nextBagFillRefresh = 0f;
        Plugin.SaveConfig();
        Plugin.ModLog.LogInfo($"Always Full Bag: {(enabled ? "ON" : "OFF")}");
    }

    [HideFromIl2Cpp]
    private void MaintainBagFull()
    {
        if (Time.realtimeSinceStartup < _nextBagFillRefresh)
            return;

        _nextBagFillRefresh = Time.realtimeSinceStartup + 0.2f;
        try
        {
            if (_pickupController == null)
                _pickupController = Object.FindObjectOfType<PlayerPickupController>();
            if (_pickupController == null)
                return;

            _pickupController.EnsureBagInHands();
            var bag = _pickupController._activeBag;
            if (bag != null && bag.HaveSpace)
                bag.SetSaveCount(999999);
        }
        catch (Exception exception)
        {
            LogAutomationError("Always Full Bag", exception);
        }
    }

    [HideFromIl2Cpp]
    private void CompleteMaxCollection()
    {
        try
        {
            if (_collectionController == null)
                _collectionController = Object.FindObjectOfType<PlayerCollectionController>();
            if (_collectionController == null)
                throw new InvalidOperationException("PlayerCollectionController was not found.");

            _collectionController.EnsureAlbumContainsAllCards();
            var cards = _collectionController.Cards;
            var upgraded = 0;
            if (cards != null)
            {
                for (var index = 0; index < cards.Count; index++)
                {
                    var card = cards[index];
                    if (card == null)
                        continue;

                    card.Quality = EQualityType.Foil;
                    card.Count = Math.Max(1, card.Count);
                    upgraded++;
                }
            }

            _collectionController.CheckAllSets();
            _collectionController.Save();
            Plugin.ModLog.LogInfo($"Max collection completed: {upgraded} cards upgraded to Foil.");
        }
        catch (Exception exception)
        {
            LogAutomationError("Max collection", exception);
        }
    }

    [HideFromIl2Cpp]
    private void CollectAllTapes()
    {
        try
        {
            var players = Object.FindObjectsByType<TapePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (players == null || players.Length == 0)
                throw new InvalidOperationException("TapePlayer was not found in the current scene.");

            var tapeIds = new HashSet<int>();
            var tapesUnlocked = 0;
            for (var playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                var player = players[playerIndex];
                if (player == null || player._items == null)
                    continue;

                for (var itemIndex = 0; itemIndex < player._items.Count; itemIndex++)
                {
                    var item = player._items[itemIndex];
                    var pickupData = item == null ? null : item.PickupData;
                    if (pickupData == null || !tapeIds.Add(pickupData.GetInstanceID()))
                        continue;

                    TapePlayer.AddCollectedTape(pickupData, true);
                    tapesUnlocked++;
                }
            }

            if (tapesUnlocked == 0)
                throw new InvalidOperationException("No configured cassette entries were initialized by TapePlayer.");

            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                if (player == null)
                    continue;

                player.RefreshItems();
                player.Save();
            }

            _lastTapesUnlocked = tapesUnlocked;
            Plugin.ModLog.LogInfo($"All cassette entries unlocked: {_lastTapesUnlocked}.");
        }
        catch (Exception exception)
        {
            LogAutomationError("Collect all tapes", exception);
        }
    }

    [HideFromIl2Cpp]
    private void PrepareAutoCleanup()
    {
        _cleanupPhase = "Scanning";
        _lastCleanupFragmentsCollected = 0;
    }

    [HideFromIl2Cpp]
    private void ApplyMoneyTarget()
    {
        try
        {
            _parametersController = Object.FindObjectOfType<ParametersController>();
            if (_parametersController == null)
            {
                Plugin.ModLog.LogWarning("Set Money failed: ParametersController was not found.");
                return;
            }

            Plugin.MoneyTarget.Value = Math.Max(0, Math.Min(999999999, Plugin.MoneyTarget.Value));
            _parametersController.SetParameter(ParameterType.Money, Plugin.MoneyTarget.Value);
            _parametersController.Save();
            _lastMoneyValue = _parametersController.GetValue(ParameterType.Money);
            Plugin.SaveConfig();
            Plugin.ModLog.LogInfo($"Money set to {_lastMoneyValue}.");
        }
        catch (Exception exception)
        {
            LogAutomationError("Set Money", exception);
        }
    }

    [HideFromIl2Cpp]
    private void RefreshFragmentState()
    {
        _nextFragmentRefresh = Time.realtimeSinceStartup + 0.25f;

        try
        {
            if (_parametersController == null)
                _parametersController = Object.FindObjectOfType<ParametersController>();
            if (_pickupController == null)
                _pickupController = Object.FindObjectOfType<PlayerPickupController>();
            if (_collectionController == null)
                _collectionController = Object.FindObjectOfType<PlayerCollectionController>();

            if (_parametersController != null)
                _fragmentPartsCount = _parametersController.GetValue(ParameterType.DirtyPartsCount);

            var dirtyPart = _collectionController == null || _collectionController._cardsSettings == null
                ? null
                : _collectionController._cardsSettings.DirtyPart;
            if (dirtyPart != null && dirtyPart.NeedCount > 0)
                _fragmentPartsNeeded = dirtyPart.NeedCount;

            _fragmentSecondsRemaining = _pickupController == null
                ? -1f
                : Mathf.Max(0f, (float)_pickupController._needPartTimer - (float)_pickupController._takingTimer);
        }
        catch (Exception exception)
        {
            LogAutomationError("Fragment state", exception);
        }
    }

    [HideFromIl2Cpp]
    private int CollectInstantFragment(JunkPickup pickup)
    {
        if (pickup == null || !pickup.isActiveAndEnabled)
            return 0;

        if (_pickupController == null)
            throw new InvalidOperationException("PlayerPickupController was not found.");

        CollectNativePickup(pickup, CardTargetKind.CardFragment);
        return 1;
    }

    [HideFromIl2Cpp]
    private void CollectNativePickup(JunkPickup pickup, CardTargetKind kind)
    {
        if (pickup == null || !pickup.isActiveAndEnabled)
            return;
        if (_pickupController == null)
            throw new InvalidOperationException("PlayerPickupController was not found.");

        if (kind != CardTargetKind.DirtyCard && kind != CardTargetKind.CardFragment)
            throw new InvalidOperationException($"Unsupported native pickup kind: {kind}.");

        // Use the same entry point as a normal interact press.  It performs
        // the game's own type dispatch, including the fragment pickup event
        // that increments DirtyPartsCount.  Do not destroy a special pickup
        // manually: a rejected native pickup must remain in the world.
        if (!_pickupController.Pick(pickup, true))
            throw new InvalidOperationException($"Native pickup was rejected for {kind}.");
    }

    [HideFromIl2Cpp]
    private void RefreshEspTargets()
    {
        _nextEspRefresh = Time.realtimeSinceStartup + EspRefreshInterval;
        _espTargets.Clear();
        RefreshZoneCardIds();

        try
        {
            var found = Object.FindObjectsByType<JunkPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var index = 0; index < found.Length; index++)
            {
                var target = found[index];
                if (target == null)
                    continue;

                var kind = ClassifyCardTarget(target);
                if (kind == CardTargetKind.DirtyCard || kind == CardTargetKind.CardFragment ||
                    kind == CardTargetKind.Figurine)
                    _espTargets.Add(target);
            }

            _camera = Camera.main;
            if (_camera != null)
            {
                var cameraPosition = _camera.transform.position;
                _espTargets.Sort((left, right) =>
                    GetSafeDistanceSquared(left, cameraPosition).CompareTo(GetSafeDistanceSquared(right, cameraPosition)));
            }
        }
        catch (Exception exception)
        {
            LogAutomationError("ESP refresh", exception);
        }
    }

    [HideFromIl2Cpp]
    private static float GetSafeDistanceSquared(JunkPickup target, Vector3 origin)
    {
        try
        {
            return target == null ? float.MaxValue : Vector3.SqrMagnitude(target.transform.position - origin);
        }
        catch
        {
            return float.MaxValue;
        }
    }

    [HideFromIl2Cpp]
    private void DrawEsp()
    {
        _camera = Camera.main;
        if (_camera == null)
            return;

        var origin = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var drawn = 0;

        DrawFragmentEspStatus();

        for (var index = 0; index < _espTargets.Count && drawn < EspMaxTargets; index++)
        {
            var target = _espTargets[index];
            if (target == null || !target.isActiveAndEnabled)
                continue;

            try
            {
                if (!TryGetScreenRect(target, _camera, out var rect, out var distance))
                    continue;

                if (distance > Plugin.EspDistance.Value)
                    continue;

                var kind = ClassifyCardTarget(target);
                if (kind != CardTargetKind.DirtyCard && kind != CardTargetKind.CardFragment &&
                    kind != CardTargetKind.Figurine)
                    continue;

                var color = GetEspColor(kind);
                DrawTargetBox(rect, color);
                DrawThinLine(origin, rect.center, color);

                var previousColor = GUI.color;
                GUI.color = color;
                GUI.Label(new Rect(rect.x, Math.Max(0f, rect.y - 20f), Math.Max(180f, rect.width + 90f), 20f),
                    GetTargetLabel(distance, kind));
                GUI.color = previousColor;
                drawn++;
            }
            catch
            {
                // Pooled pickups can become invalid between refresh and OnGUI.
            }
        }
    }

    [HideFromIl2Cpp]
    private void DrawFragmentEspStatus()
    {
        var timer = _fragmentSecondsRemaining >= 0f
            ? $"next in {_fragmentSecondsRemaining:0}s"
            : "timer unavailable";
        var text = $"CARD FRAGMENTS  {_fragmentPartsCount}/{_fragmentPartsNeeded}  |  {timer}";
        var rect = new Rect(Math.Max(0f, Screen.width * 0.5f - 165f), 12f, 330f, 24f);
        var previousColor = GUI.color;
        GUI.color = new Color(0.1f, 0.95f, 1f, 1f);
        GUI.Box(rect, text);
        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private static void DrawTargetBox(Rect rect, Color color)
    {
        var previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(rect, string.Empty);
        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private void DrawThinLine(Vector2 from, Vector2 to, Color color)
    {
        if (!_glLineUnavailable && Event.current != null && Event.current.type == EventType.Repaint)
        {
            try
            {
                if (_lineMaterial == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                        throw new InvalidOperationException("Hidden/Internal-Colored shader was not found.");

                    _lineMaterial = new Material(shader);
                    _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                }

                if (_lineMaterial.SetPass(0))
                {
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
                    GL.Begin(GL.LINES);
                    GL.Color(color);
                    GL.Vertex3(from.x, from.y, 0f);
                    GL.Vertex3(to.x, to.y, 0f);
                    GL.End();
                    GL.PopMatrix();
                    return;
                }
            }
            catch (Exception exception)
            {
                _glLineUnavailable = true;
                Plugin.ModLog.LogWarning($"GL ESP lines unavailable, using dotted fallback: {exception.Message}");
            }
        }

        if (_glLineUnavailable)
            DrawDottedFallback(from, to, color);
    }

    [HideFromIl2Cpp]
    private static void DrawDottedFallback(Vector2 from, Vector2 to, Color color)
    {
        var difference = to - from;
        var length = difference.magnitude;
        if (length < 1f)
            return;

        var steps = Mathf.Clamp((int)(length / 8f), 8, 128);
        var previousColor = GUI.color;
        GUI.color = color;

        for (var index = 0; index <= steps; index++)
        {
            var point = from + difference * (index / (float)steps);
            GUI.Label(new Rect(point.x - 4f, point.y - 9f, 10f, 18f), "*");
        }

        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private static bool TryGetScreenRect(JunkPickup target, Camera camera, out Rect rect, out float distance)
    {
        rect = default;
        var center = target.transform.position;
        distance = Vector3.Distance(camera.transform.position, center);

        var renderer = target.GetComponentInChildren<Renderer>();
        var bounds = renderer != null ? renderer.bounds : new Bounds(center, new Vector3(0.6f, 0.6f, 0.6f));
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var visibleCorners = 0;

        for (var index = 0; index < 8; index++)
        {
            var corner = bounds.center + new Vector3(
                (index & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                (index & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                (index & 4) == 0 ? -bounds.extents.z : bounds.extents.z);

            var screen = camera.WorldToScreenPoint(corner);
            if (screen.z <= 0f)
                continue;

            var guiY = Screen.height - screen.y;
            minX = Math.Min(minX, screen.x);
            minY = Math.Min(minY, guiY);
            maxX = Math.Max(maxX, screen.x);
            maxY = Math.Max(maxY, guiY);
            visibleCorners++;
        }

        if (visibleCorners == 0)
            return false;

        minX = Mathf.Clamp(minX, 0f, Screen.width);
        maxX = Mathf.Clamp(maxX, 0f, Screen.width);
        minY = Mathf.Clamp(minY, 0f, Screen.height);
        maxY = Mathf.Clamp(maxY, 0f, Screen.height);

        if (maxX - minX < 3f || maxY - minY < 3f)
            return false;

        rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    [HideFromIl2Cpp]
    private static Color GetEspColor(CardTargetKind kind)
    {
        if (kind == CardTargetKind.DirtyCard)
            return new Color(1f, 0.25f, 1f, 1f);
        if (kind == CardTargetKind.CardFragment)
            return new Color(0.1f, 0.95f, 1f, 1f);
        if (kind == CardTargetKind.Figurine)
            return new Color(0.3f, 1f, 0.25f, 1f);
        return Color.yellow;
    }

    [HideFromIl2Cpp]
    private static string GetTargetLabel(float distance, CardTargetKind kind)
    {
        if (kind == CardTargetKind.DirtyCard)
            return $"Dirty Card  {distance:0.0}m";
        if (kind == CardTargetKind.CardFragment)
            return $"Card Fragment  {distance:0.0}m";
        if (kind == CardTargetKind.Figurine)
            return $"Figurine  {distance:0.0}m";
        return $"Unknown  {distance:0.0}m";
    }

    [HideFromIl2Cpp]
    private void BuildCleanupBuckets(
        List<JunkPickup> dirtyCards,
        List<JunkPickup> fragments,
        List<JunkPickup> cardBoxes,
        List<JunkPickup> trash)
    {
        var protectedIds = new HashSet<int>();
        var dirtyCardIds = new HashSet<int>();
        var fragmentIds = new HashSet<int>();
        var cardBoxIds = new HashSet<int>();
        var trashIds = new HashSet<int>();

        // Fragments are the sole pickup category that does not expose a
        // reliable public data marker in this game build.  The zone's part
        // registry is populated by the native spawn path and is therefore the
        // authoritative source for both ESP and automatic collection.
        var zones = Object.FindObjectsByType<JunkZoneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
        {
            var zone = zones[zoneIndex];
            if (zone == null)
                continue;

            try
            {
                var parts = zone._partPickups;
                if (parts == null)
                    continue;

                for (var index = 0; index < parts.Count; index++)
                    AddProtectedPickup(parts[index], fragments, fragmentIds, protectedIds);
            }
            catch (Exception exception)
            {
                Plugin.ModLog.LogWarning($"Could not read this zone's fragment registry: {exception.Message}");
            }
        }

        var worldPickups = Object.FindObjectsByType<JunkPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var index = 0; index < worldPickups.Length; index++)
        {
            var pickup = worldPickups[index];
            switch (ClassifyCardTarget(pickup))
            {
                case CardTargetKind.DirtyCard:
                    AddProtectedPickup(pickup, dirtyCards, dirtyCardIds, protectedIds);
                    break;
                case CardTargetKind.CardFragment:
                    AddProtectedPickup(pickup, fragments, fragmentIds, protectedIds);
                    break;
                case CardTargetKind.CardBox:
                    AddProtectedPickup(pickup, cardBoxes, cardBoxIds, protectedIds);
                    break;
                case CardTargetKind.Figurine:
                    MarkPickupProtected(pickup, protectedIds);
                    break;
                default:
                    if (IsConfirmedTrash(pickup))
                        AddTrashPickup(pickup, trash, trashIds, protectedIds);
                    break;
            }
        }

        Plugin.ModLog.LogInfo($"Auto Cleanup buckets: cards={dirtyCards.Count}, fragments={fragments.Count}, " +
            $"cardBoxes={cardBoxes.Count}, normalTrash={trash.Count}, worldPickups={worldPickups.Length}, zones={zones.Length}.");
        LogPickupDiagnostics(worldPickups);
    }

    [HideFromIl2Cpp]
    private static void AddProtectedPickup(
        JunkPickup pickup,
        List<JunkPickup> bucket,
        HashSet<int> bucketIds,
        HashSet<int> protectedIds)
    {
        if (!TryGetLivePickupId(pickup, out var instanceId))
            return;

        protectedIds.Add(instanceId);
        if (bucketIds.Add(instanceId))
            bucket.Add(pickup);
    }

    [HideFromIl2Cpp]
    private static void MarkPickupProtected(JunkPickup pickup, HashSet<int> protectedIds)
    {
        if (TryGetLivePickupId(pickup, out var instanceId))
            protectedIds.Add(instanceId);
    }

    [HideFromIl2Cpp]
    private static void AddTrashPickup(
        JunkPickup pickup,
        List<JunkPickup> trash,
        HashSet<int> trashIds,
        HashSet<int> protectedIds)
    {
        if (!TryGetLivePickupId(pickup, out var instanceId) || protectedIds.Contains(instanceId))
            return;

        if (trashIds.Add(instanceId))
            trash.Add(pickup);
    }

    [HideFromIl2Cpp]
    private static bool IsConfirmedTrash(JunkPickup pickup)
    {
        try
        {
            if (pickup == null || pickup.Data == null || pickup.Data.JunkType != EJunkType.Common)
                return false;

            // In this build, disposable loose garbage is explicitly backed by
            // the ui_empty pickup data. A mere Common enum is not enough:
            // unlabelled card fragments also use it and must remain available
            // for normal/manual pickup until they can be identified reliably.
            return string.Equals(pickup.Data.Name, "ui_empty", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Unknown objects are never classified as deletion targets.
            return false;
        }
    }

    [HideFromIl2Cpp]
    private static bool TryGetLivePickupId(JunkPickup pickup, out int instanceId)
    {
        instanceId = 0;
        try
        {
            if (pickup == null || !pickup.isActiveAndEnabled)
                return false;

            instanceId = pickup.GetInstanceID();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void RunAutoCleanup()
    {
        PrepareAutoCleanup();

        try
        {
            RefreshZoneCardIds();
            var dirtyCards = new List<JunkPickup>();
            var fragments = new List<JunkPickup>();
            var cardBoxes = new List<JunkPickup>();
            var trash = new List<JunkPickup>();
            BuildCleanupBuckets(dirtyCards, fragments, cardBoxes, trash);

            _cleanupCardsRemaining = dirtyCards.Count + cardBoxes.Count + fragments.Count;
            _cleanupTrashRemaining = trash.Count;

            _collectionController = Object.FindObjectOfType<PlayerCollectionController>();
            _pickupController = Object.FindObjectOfType<PlayerPickupController>();
            if (cardBoxes.Count > 0 && _collectionController == null)
            {
                _cleanupPhase = "No collection controller";
                Plugin.ModLog.LogWarning("Auto Cleanup aborted before trash removal: PlayerCollectionController was not found.");
                return;
            }
            if (dirtyCards.Count + fragments.Count > 0 && _pickupController == null)
            {
                _cleanupPhase = "No pickup controller";
                Plugin.ModLog.LogWarning("Auto Cleanup aborted before trash removal: PlayerPickupController was not found.");
                return;
            }

            _cleanupPhase = "Cards";
            var cardsCollected = 0;
            var boxesCollected = 0;
            var cardFailure = false;
            for (var index = 0; index < dirtyCards.Count; index++)
            {
                var pickup = dirtyCards[index];
                if (pickup == null || !pickup.isActiveAndEnabled)
                    continue;

                try
                {
                    CollectNativePickup(pickup, CardTargetKind.DirtyCard);
                    cardsCollected++;
                }
                catch (Exception exception)
                {
                    cardFailure = true;
                    Plugin.ModLog.LogWarning($"Instant card collection failed for {DescribePickup(pickup)}: {exception.Message}");
                }
            }

            for (var index = 0; index < cardBoxes.Count; index++)
            {
                var pickup = cardBoxes[index];
                if (pickup == null || !pickup.isActiveAndEnabled)
                    continue;

                try
                {
                    _collectionController.TakeRandomCard();
                    RemoveWorldPickup(pickup);
                    boxesCollected++;
                }
                catch (Exception exception)
                {
                    cardFailure = true;
                    Plugin.ModLog.LogWarning($"Instant CardBox collection failed for {DescribePickup(pickup)}: {exception.Message}");
                }
            }

            if (cardFailure)
            {
                _cleanupPhase = "Card error; trash kept";
                _cleanupCardsRemaining = Math.Max(0, _cleanupCardsRemaining - cardsCollected - boxesCollected);
                return;
            }

            if (cardsCollected + boxesCollected > 0 && _collectionController != null)
                _collectionController.Save();

            _cleanupPhase = "Fragments";
            var fragmentsCollected = 0;
            for (var index = 0; index < fragments.Count; index++)
            {
                try
                {
                    fragmentsCollected += CollectInstantFragment(fragments[index]);
                }
                catch (Exception exception)
                {
                    _cleanupPhase = "Fragment error; trash kept";
                    Plugin.ModLog.LogWarning($"Instant card fragment collection failed: {exception.Message}");
                    return;
                }
            }
            _lastCleanupFragmentsCollected = fragmentsCollected;

            _cleanupPhase = "Trash";
            var trashRemoved = 0;
            for (var index = 0; index < trash.Count; index++)
            {
                var pickup = trash[index];
                if (pickup == null || !pickup.isActiveAndEnabled)
                    continue;

                RemoveWorldPickup(pickup);
                trashRemoved++;
            }

            _cleanupCardsRemaining = 0;
            _cleanupTrashRemaining = 0;
            _cleanupPhase = "Done";
            Plugin.ModLog.LogInfo($"Auto Cleanup completed in one pass: dirtyCards={cardsCollected}, cardBoxes={boxesCollected}, " +
                $"fragments={_lastCleanupFragmentsCollected}, trash={trashRemoved}.");
        }
        catch (Exception exception)
        {
            _cleanupPhase = "Error";
            LogAutomationError("Auto Cleanup", exception);
        }
    }

    [HideFromIl2Cpp]
    private static void RemoveWorldPickup(JunkPickup pickup)
    {
        if (pickup == null)
            return;

        try
        {
            pickup.Destroyed();
        }
        catch
        {
            // Explicit object destruction below guarantees removal.
        }

        try
        {
            if (pickup != null && pickup.gameObject != null)
                Object.Destroy(pickup.gameObject);
        }
        catch
        {
            // The game may already have released the pooled object.
        }
    }

    [HideFromIl2Cpp]
    private void RefreshZoneCardIds()
    {
        _zoneCardInstanceIds.Clear();
        _zonePartInstanceIds.Clear();
        _zoneCollectibleInstanceIds.Clear();

        try
        {
            var zones = Object.FindObjectsByType<JunkZoneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
            {
                var zone = zones[zoneIndex];
                if (zone == null)
                    continue;

                try
                {
                    var parts = zone._partPickups;
                    if (parts == null)
                        continue;

                    for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                    {
                        var part = parts[partIndex];
                        if (part != null)
                            _zonePartInstanceIds.Add(part.GetInstanceID());
                    }
                }
                catch (Exception exception)
                {
                    Plugin.ModLog.LogWarning($"Could not refresh this zone's fragment registry: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            LogAutomationError("Fragment registry scan", exception);
        }
    }

    [HideFromIl2Cpp]
    private CardTargetKind ClassifyCardTarget(JunkPickup pickup)
    {
        if (pickup == null)
            return CardTargetKind.None;

        var trackedAsCard = false;
        var trackedAsFragment = false;
        var trackedAsFigurine = false;
        var isCardBox = false;
        var isDirtyCard = false;
        var isFragment = false;
        var isFigurine = false;
        var junkType = EJunkType.Common;
        var hasJunkType = false;

        try
        {
            trackedAsCard = _zoneCardInstanceIds.Contains(pickup.GetInstanceID());
            trackedAsFragment = _zonePartInstanceIds.Contains(pickup.GetInstanceID());
            trackedAsFigurine = _zoneCollectibleInstanceIds.Contains(pickup.GetInstanceID());
        }
        catch
        {
            // Continue with data and name markers.
        }

        try
        {
            var data = pickup.Data;
            if (data != null)
            {
                junkType = data.JunkType;
                hasJunkType = true;
                ReadCardMarkers(data.Name, ref isCardBox, ref isDirtyCard, ref isFragment);
                ReadCardMarkers(data.name, ref isCardBox, ref isDirtyCard, ref isFragment);
                ReadCardMarkers(data.Description, ref isCardBox, ref isDirtyCard, ref isFragment);
                ReadFigurineMarkers(data.Name, ref isFigurine);
                ReadFigurineMarkers(data.name, ref isFigurine);
                ReadFigurineMarkers(data.Description, ref isFigurine);
            }
        }
        catch
        {
            // Continue with object names and the zone registry.
        }

        try
        {
            ReadCardMarkers(pickup.name, ref isCardBox, ref isDirtyCard, ref isFragment);
            ReadCardMarkers(pickup.gameObject.name, ref isCardBox, ref isDirtyCard, ref isFragment);
            ReadFigurineMarkers(pickup.name, ref isFigurine);
            ReadFigurineMarkers(pickup.gameObject.name, ref isFigurine);
        }
        catch
        {
            // Classification from data and the zone registry is still valid.
        }

        if ((hasJunkType && junkType == EJunkType.CardBox) || isCardBox)
            return CardTargetKind.CardBox;

        if (trackedAsFragment || isFragment || (hasJunkType && junkType == EJunkType.Part))
            return CardTargetKind.CardFragment;

        if (trackedAsCard || isDirtyCard || (hasJunkType && junkType == EJunkType.Card))
            return CardTargetKind.DirtyCard;

        if (trackedAsFigurine || isFigurine || (hasJunkType && junkType == EJunkType.Collectible))
            return CardTargetKind.Figurine;

        return CardTargetKind.None;
    }

    [HideFromIl2Cpp]
    private static void ReadCardMarkers(string value, ref bool isCardBox, ref bool isDirtyCard, ref bool isFragment)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);

        if (normalized.Contains("cardbox") || normalized.Contains("boxcard"))
        {
            isCardBox = true;
            return;
        }

        if (normalized.Contains("fragment") ||
            normalized.Contains("dirtypart") || normalized.Contains("partdirty") ||
            normalized.Contains("cardpart") || normalized.Contains("partcard") ||
            normalized.Contains("dirtypiece") || normalized.Contains("piecedirty") ||
            normalized.Contains("cardpiece") || normalized.Contains("piececard"))
        {
            isFragment = true;
            return;
        }

        if (normalized.Contains("carddirty") ||
            normalized.Contains("carddirt") ||
            normalized.Contains("dirtycard") ||
            normalized.Contains("dirtcard"))
            isDirtyCard = true;
    }

    [HideFromIl2Cpp]
    private static void ReadFigurineMarkers(string value, ref bool isFigurine)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);

        if (normalized.Contains("figurine") || normalized.Contains("figure") ||
            normalized.Contains("statue"))
            isFigurine = true;
    }

    [HideFromIl2Cpp]
    private void LogPickupDiagnostics(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<JunkPickup> pickups)
    {
        var limit = Math.Min(pickups.Length, 48);
        Plugin.ModLog.LogInfo($"Auto Cleanup scan: worldPickups={pickups.Length}. " +
            "Classification uses PickupData.JunkType and explicit object markers.");

        for (var index = 0; index < limit; index++)
        {
            var pickup = pickups[index];
            if (pickup == null)
                continue;

            var kind = ClassifyCardTarget(pickup);
            Plugin.ModLog.LogInfo($"Cleanup candidate [{index}]: kind={kind}, {DescribePickup(pickup)}");
        }
    }

    [HideFromIl2Cpp]
    private static string DescribePickup(JunkPickup pickup)
    {
        if (pickup == null)
            return "<destroyed>";

        var objectName = "?";
        var dataName = "?";
        var junkType = "?";
        var hasCardData = "?";
        var hasCollectibleData = "?";

        try { objectName = pickup.name; } catch { }
        try { dataName = pickup.Data == null ? "<null>" : pickup.Data.Name; } catch { }
        try { junkType = pickup.Data == null ? "<null>" : pickup.Data.JunkType.ToString(); } catch { }
        try { hasCardData = pickup.Data != null && pickup.Data.CardData != null ? "yes" : "no"; } catch { }
        try { hasCollectibleData = pickup.Data != null && pickup.Data.CollectibleData != null ? "yes" : "no"; } catch { }
        return $"object='{objectName}', data='{dataName}', junkType={junkType}, " +
            $"cardData={hasCardData}, collectibleData={hasCollectibleData}";
    }

    [HideFromIl2Cpp]
    private void LogAutomationError(string feature, Exception exception)
    {
        if (Time.realtimeSinceStartup < _nextAutomationErrorLog)
            return;

        _nextAutomationErrorLog = Time.realtimeSinceStartup + 5f;
        Plugin.ModLog.LogWarning($"{feature} failed: {exception.Message}");
    }

    [HideFromIl2Cpp]
    private void CaptureRebindEvent()
    {
        if (_bindingAction == BindingAction.None || Event.current == null)
            return;

        KeyCode key;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
        {
            key = Event.current.keyCode;
            if (key == KeyCode.Escape)
            {
                _bindingAction = BindingAction.None;
                return;
            }
        }
        else if (Event.current.type == EventType.MouseDown && Event.current.button >= 0 && Event.current.button <= 6)
        {
            key = (KeyCode)((int)KeyCode.Mouse0 + Event.current.button);
        }
        else
        {
            return;
        }

        SetBinding(_bindingAction, key);
        _bindingAction = BindingAction.None;
        Plugin.SaveConfig();
    }

    [HideFromIl2Cpp]
    private static void SetBinding(BindingAction action, KeyCode key)
    {
        switch (action)
        {
            case BindingAction.Menu: Plugin.MenuKey.Value = key; break;
            case BindingAction.Noclip: Plugin.NoclipKey.Value = key; break;
            case BindingAction.WorldSpeed: Plugin.WorldSpeedKey.Value = key; break;
            case BindingAction.Esp: Plugin.EspKey.Value = key; break;
            case BindingAction.AutoCleanup: Plugin.AutoCleanupKey.Value = key; break;
            case BindingAction.BagAlwaysFull: Plugin.BagAlwaysFullKey.Value = key; break;
            case BindingAction.MaxCollection: Plugin.MaxCollectionKey.Value = key; break;
            case BindingAction.CollectAllTapes: Plugin.CollectAllTapesKey.Value = key; break;
        }
    }

    [HideFromIl2Cpp]
    private void DrawMenu()
    {
        var x = _menuRect.x;
        var y = _menuRect.y;
        var width = _menuRect.width;
        var height = _menuRect.height;

        HandleWindowDrag(ref _menuRect, new Rect(x, y, width, 25f), DragTarget.Menu);
        x = _menuRect.x;
        y = _menuRect.y;

        GUI.Box(new Rect(x, y, width, height), $"Kotamon Dev Cheat v{Plugin.PluginVersion}");
        GUI.Label(new Rect(x + 20f, y + 28f, width - 40f, 20f), $"Drag title. Click Key to rebind. {Plugin.MenuKey.Value} closes menu.");

        DrawFeatureRow(x, y + 58f, "Noclip", IsNoclipEnabled(), Plugin.NoclipKey.Value, BindingAction.Noclip, ToggleNoclip);
        var noclipSpeed = DrawValueRow(x, y + 92f, "Speed", Plugin.NoclipSpeed.Value, 1f, 50f, 1f, "0");
        if (Math.Abs(noclipSpeed - Plugin.NoclipSpeed.Value) > 0.001f)
        {
            Plugin.NoclipSpeed.Value = noclipSpeed;
            ApplyNoclipSpeed();
            Plugin.SaveConfig();
        }

        DrawFeatureRow(x, y + 135f, "WorldSpeed", Plugin.WorldSpeedEnabled.Value, Plugin.WorldSpeedKey.Value, BindingAction.WorldSpeed,
            () => SetWorldSpeedEnabled(!Plugin.WorldSpeedEnabled.Value));
        var worldSpeed = DrawValueRow(x, y + 169f, "Multiplier", Plugin.WorldSpeedValue.Value, 0.1f, 5f, 0.25f, "0.00x");
        if (Math.Abs(worldSpeed - Plugin.WorldSpeedValue.Value) > 0.001f)
        {
            Plugin.WorldSpeedValue.Value = worldSpeed;
            if (Plugin.WorldSpeedEnabled.Value)
                Time.timeScale = worldSpeed;
            Plugin.SaveConfig();
        }

        DrawFeatureRow(x, y + 212f, "ESP boxes + lines", Plugin.EspEnabled.Value, Plugin.EspKey.Value, BindingAction.Esp,
            () => SetEspEnabled(!Plugin.EspEnabled.Value));
        var espDistance = DrawValueRow(x, y + 246f, "Distance", Plugin.EspDistance.Value, 10f, 200f, 5f, "0m");
        if (Math.Abs(espDistance - Plugin.EspDistance.Value) > 0.001f)
        {
            Plugin.EspDistance.Value = espDistance;
            Plugin.SaveConfig();
        }

        if (GUI.Button(new Rect(x + 20f, y + 289f, 285f, 28f), "Auto Cleanup: RUN NOW"))
            RunAutoCleanup();

        if (GUI.Button(new Rect(x + 320f, y + 289f, 245f, 28f), BindingText(BindingAction.AutoCleanup, Plugin.AutoCleanupKey.Value)))
            _bindingAction = BindingAction.AutoCleanup;

        DrawFeatureRow(x, y + 332f, "Always Full Bag", Plugin.BagAlwaysFullEnabled.Value, Plugin.BagAlwaysFullKey.Value,
            BindingAction.BagAlwaysFull, () => SetBagAlwaysFullEnabled(!Plugin.BagAlwaysFullEnabled.Value));

        if (GUI.Button(new Rect(x + 20f, y + 375f, 285f, 28f), "Max Card Collection: RUN NOW"))
            CompleteMaxCollection();
        if (GUI.Button(new Rect(x + 320f, y + 375f, 245f, 28f), BindingText(BindingAction.MaxCollection, Plugin.MaxCollectionKey.Value)))
            _bindingAction = BindingAction.MaxCollection;

        if (GUI.Button(new Rect(x + 20f, y + 418f, 285f, 28f), "All Cassettes: UNLOCK NOW"))
            CollectAllTapes();
        if (GUI.Button(new Rect(x + 320f, y + 418f, 245f, 28f), BindingText(BindingAction.CollectAllTapes, Plugin.CollectAllTapesKey.Value)))
            _bindingAction = BindingAction.CollectAllTapes;

        GUI.Label(new Rect(x + 20f, y + 465f, 140f, 25f), $"Money: {Plugin.MoneyTarget.Value}");
        if (GUI.Button(new Rect(x + 160f, y + 459f, 68f, 28f), "-10000"))
            Plugin.MoneyTarget.Value = Math.Max(0, Plugin.MoneyTarget.Value - 10000);
        if (GUI.Button(new Rect(x + 232f, y + 459f, 68f, 28f), "-1000"))
            Plugin.MoneyTarget.Value = Math.Max(0, Plugin.MoneyTarget.Value - 1000);
        if (GUI.Button(new Rect(x + 304f, y + 459f, 68f, 28f), "+1000"))
            Plugin.MoneyTarget.Value = Math.Min(999999999, Plugin.MoneyTarget.Value + 1000);
        if (GUI.Button(new Rect(x + 376f, y + 459f, 76f, 28f), "+10000"))
            Plugin.MoneyTarget.Value = Math.Min(999999999, Plugin.MoneyTarget.Value + 10000);
        if (GUI.Button(new Rect(x + 456f, y + 459f, 109f, 28f), "APPLY MONEY"))
            ApplyMoneyTarget();

        GUI.Label(new Rect(x + 20f, y + 507f, 170f, 25f), "Menu hotkey");
        if (GUI.Button(new Rect(x + 195f, y + 504f, 180f, 28f), BindingText(BindingAction.Menu, Plugin.MenuKey.Value)))
            _bindingAction = BindingAction.Menu;

        if (GUI.Button(new Rect(x + 390f, y + 504f, 165f, 28f), "Reset TimeScale"))
            SetWorldSpeedEnabled(false);

        GUI.Label(new Rect(x + 20f, y + 554f, width - 40f, 72f),
            "Auto Cleanup: cards -> fragments -> trash. If a fragment cannot be verified, trash is kept.\n" +
            "Max Collection unlocks every card at Foil quality. All Cassettes unlocks every tape.\n" +
            "ESP: cards magenta, fragments cyan, figurines green.");
    }

    [HideFromIl2Cpp]
    private void DrawFeatureRow(float x, float y, string name, bool enabled, KeyCode key, BindingAction action, Action toggle)
    {
        if (GUI.Button(new Rect(x + 20f, y, 285f, 28f), $"{name}: {(enabled ? "ON" : "OFF")}"))
            toggle();

        if (GUI.Button(new Rect(x + 320f, y, 245f, 28f), BindingText(action, key)))
            _bindingAction = action;
    }

    [HideFromIl2Cpp]
    private static float DrawValueRow(float x, float y, string label, float value, float minimum, float maximum, float step, string format)
    {
        GUI.Label(new Rect(x + 40f, y, 190f, 25f), $"{label}: {value.ToString(format)}");

        if (GUI.Button(new Rect(x + 320f, y - 2f, 70f, 26f), $"-{step:0.##}"))
            value = Mathf.Clamp(value - step, minimum, maximum);

        if (GUI.Button(new Rect(x + 405f, y - 2f, 70f, 26f), $"+{step:0.##}"))
            value = Mathf.Clamp(value + step, minimum, maximum);

        if (GUI.Button(new Rect(x + 490f, y - 2f, 75f, 26f), "Reset"))
            value = DefaultValueFor(label);

        return value;
    }

    [HideFromIl2Cpp]
    private static float DefaultValueFor(string label)
    {
        return label switch
        {
            "Speed" => 10f,
            "Multiplier" => 2f,
            "Distance" => 75f,
            _ => 1f
        };
    }

    [HideFromIl2Cpp]
    private string BindingText(BindingAction action, KeyCode key)
    {
        return _bindingAction == action ? "PRESS KEY (Esc cancels)" : $"Key: {key}";
    }

    [HideFromIl2Cpp]
    private void HandleWindowDrag(ref Rect window, Rect titleBar, DragTarget target)
    {
        if (!_menuOpen || Event.current == null)
            return;

        var currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && titleBar.Contains(currentEvent.mousePosition))
        {
            _dragTarget = target;
            _dragOffset = currentEvent.mousePosition - new Vector2(window.x, window.y);
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && _dragTarget == target)
        {
            window.x = Mathf.Clamp(currentEvent.mousePosition.x - _dragOffset.x, 0f, Math.Max(0f, Screen.width - window.width));
            window.y = Mathf.Clamp(currentEvent.mousePosition.y - _dragOffset.y, 0f, Math.Max(0f, Screen.height - window.height));
            currentEvent.Use();
        }
        else if (currentEvent.rawType == EventType.MouseUp && _dragTarget == target)
        {
            _dragTarget = DragTarget.None;
        }
    }

    [HideFromIl2Cpp]
    private void DrawCompactStatus()
    {
        var x = _statusRect.x;
        var y = _statusRect.y;
        var width = _statusRect.width;

        HandleWindowDrag(ref _statusRect, new Rect(x, y, width, 25f), DragTarget.Status);
        x = _statusRect.x;
        y = _statusRect.y;

        GUI.Box(new Rect(x, y, width, _statusRect.height), $"Kotamon Dev Cheat [{Plugin.MenuKey.Value}]");
        GUI.Label(new Rect(x + 13f, y + 27f, 345f, 20f), $"{Plugin.NoclipKey.Value} Noclip: {(IsNoclipEnabled() ? "ON" : "OFF")}  speed {Plugin.NoclipSpeed.Value:0}");
        GUI.Label(new Rect(x + 13f, y + 49f, 345f, 20f), $"{Plugin.WorldSpeedKey.Value} WorldSpeed: {(Plugin.WorldSpeedEnabled.Value ? $"{Plugin.WorldSpeedValue.Value:0.00}x" : "OFF")}");
        GUI.Label(new Rect(x + 13f, y + 71f, 345f, 20f), $"{Plugin.EspKey.Value} ESP: {(Plugin.EspEnabled.Value ? $"ON ({_espTargets.Count})" : "OFF")}");
        GUI.Label(new Rect(x + 13f, y + 93f, 345f, 20f), $"{Plugin.AutoCleanupKey.Value} Auto Cleanup: {_cleanupPhase}");
        GUI.Label(new Rect(x + 13f, y + 115f, 345f, 20f),
            $"Cards: {_cleanupCardsRemaining}  Parts: {_fragmentPartsCount}/{_fragmentPartsNeeded} (+{_lastCleanupFragmentsCollected})  Trash: {_cleanupTrashRemaining}");
        GUI.Label(new Rect(x + 13f, y + 137f, 345f, 20f), $"Money: {(_lastMoneyValue >= 0 ? _lastMoneyValue.ToString() : "use menu to set")}");
        GUI.Label(new Rect(x + 13f, y + 159f, 345f, 20f), $"{Plugin.BagAlwaysFullKey.Value} Full Bag: {(Plugin.BagAlwaysFullEnabled.Value ? "ON" : "OFF")}");
        GUI.Label(new Rect(x + 13f, y + 181f, 345f, 20f), $"{Plugin.MaxCollectionKey.Value} Max Collection: press to complete");
        GUI.Label(new Rect(x + 13f, y + 203f, 345f, 20f), $"{Plugin.CollectAllTapesKey.Value} All Cassettes: {_lastTapesUnlocked}");
    }
}
