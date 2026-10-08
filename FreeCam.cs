using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Hypostasis.Game.Structures;

namespace Cammy;

public static unsafe class FreeCam
{
    public const string ControlsString = "Additional Controls:" +
        //"\nMove Keybinds - Move," +
        //"\nJump / Ascend - Up," +
        //"\nDescend - Down," +
        "\nShift (Hold) - Speed up" +
        "\nZoom / Controller Zoom (Autorun + Look Up / Down) - Change Speed" +
        "\nCycle through Enemies (Nearest to Farthest) / Controller Select HUD - Lock" +
        "\nCycle through Enemies (Farthest to Nearest) / Controller Open Main Menu - Stop";

    public static bool Enabled => gameCamera != null;
    public static Vector3 Position => position;

    private static GameCamera* gameCamera;
    private static bool locked = false;
    private static float speed = 1;
    private static Vector3 position;
    private static bool onDeath = false;
    private static bool onDeathActivated = false;
    private static CameraConfigPreset prevPresetOverride;
    private static float prevZoom = 0;
    private static float prevFoV = 0;
    private static bool displayedControls = false;
    private static readonly List<(Vector3, Vector2)> savedPositions = [];
    private static float advancedControlsAlpha;
    private static Vector3 velocity;
    private static bool pathPlaying = false;
    private static float pathTime;
    private static bool playKeyHeld;
    private static bool resetKeyHeld;

    private enum FreeCamBindings
    {
        Forward,
        Backward,
        Left,
        Left2,
        Right,
        Right2,
        Ascend,
        Ascend2,
        Descend,
        ToggleLock,
        EndFreeCam,
        ControllerAscend,
        ControllerDescend,
        ControllerToggleLock,
        ControllerAdjustSpeedModifier,
        ControllerEndFreeCam
    }

    private static readonly Dictionary<FreeCamBindings, uint> keybindings = new()
    {
        [FreeCamBindings.Forward] = 321, // Move Forward
        [FreeCamBindings.Backward] = 322, // Move Back
        [FreeCamBindings.Left] = 323,  // Move
        [FreeCamBindings.Left2] = 325, // Strafe Left
        [FreeCamBindings.Right] = 324, // Move
        [FreeCamBindings.Right2] = 326, // Strafe Right
        [FreeCamBindings.Ascend] = 348, // Jump
        [FreeCamBindings.Ascend2] = 449, // Ascend
        [FreeCamBindings.Descend] = 448, // Descent
        [FreeCamBindings.ToggleLock] = 366, // Cycle through Enemies (Nearest to Farthest)
        [FreeCamBindings.EndFreeCam] = 367, // Cycle through Enemies (Farthest to Nearest)
        [FreeCamBindings.ControllerAscend] = 5, // Controller Jump
        [FreeCamBindings.ControllerDescend] = 2, // Controller Cancel
        [FreeCamBindings.ControllerAdjustSpeedModifier] = 17, // Controller Autorun
        [FreeCamBindings.ControllerToggleLock] = 433, // Controller Select HUD
        [FreeCamBindings.ControllerEndFreeCam] = 35 // Controller Open Main Menu
    };

    private static readonly CameraConfigPreset freeCamPreset = new()
    {
        MinVRotation = -1.559f,
        MaxVRotation = 1.559f,
        MinZoom = 0.06f,
        MaxZoom = 0.06f,
        UseStartZoom = true,
        StartZoom = 0.06f,
        ZoomDelta = 0
    };

    public static void Toggle(bool death = false)
    {
        var isMainMenu = !DalamudApi.Condition.Any();
        if (!Enabled)
        {
            EnableInputBlockers();

            gameCamera = isMainMenu ? Common.CameraManager->menuCamera : Common.CameraManager->worldCamera;

            locked = false;
            speed = 1;
            velocity = Vector3.Zero;
            pathPlaying = false;
            position = new(gameCamera->viewX, gameCamera->viewY, gameCamera->viewZ);
            onDeath = death;
            prevPresetOverride = PresetManager.PresetOverride;
            prevZoom = gameCamera->currentZoom;
            prevFoV = gameCamera->currentFoV;
            advancedControlsAlpha = 1;

            freeCamPreset.MinFoV = freeCamPreset.MaxFoV = gameCamera->currentFoV;
            freeCamPreset.Apply();
            gameCamera->mode = 1;
            Game.cameraNoClippyReplacer.Enable();

            if (!isMainMenu)
            {
                Game.ForceDisableMovement++;

                if (!death && !displayedControls)
                {
                    DalamudApi.ShowNotification(ControlsString, NotificationType.Info, 10_000);
                    displayedControls = true;
                }
            }
            else
            {
                gameCamera->lockPosition = 0;
            }
        }
        else
        {
            DisableInputBlockers();
            pathPlaying = false;

            if (!isMainMenu)
            {
                if (!locked && Game.ForceDisableMovement > 0)
                    Game.ForceDisableMovement--;
                PresetManager.DefaultPreset.Apply();
                PresetManager.DisableCameraPresets();
                PresetManager.CurrentPreset = prevPresetOverride;
            }

            gameCamera->currentZoom = gameCamera->interpolatedZoom = prevZoom;
            gameCamera->currentFoV = prevFoV;
            gameCamera = null;
            if (!Cammy.Config.EnableCameraNoClippy)
                Game.cameraNoClippyReplacer.Disable();
        }

        if (!isMainMenu) return;

        static void ToggleAddonVisible(string name)
        {
            var addon = DalamudApi.GameGui.GetAddonByName(name, 1);
            if (addon == nint.Zero) return;
            ((AtkUnitBase*)addon.Address)->IsVisible ^= true;
        }

        ToggleAddonVisible("_TitleRights");
        ToggleAddonVisible("_TitleRevision");
        ToggleAddonVisible("_TitleMenu");
        ToggleAddonVisible("_TitleLogo");
    }

    public static void CheckDeath()
    {
        var dead = DalamudApi.Condition[ConditionFlag.Unconscious];
        if (onDeathActivated)
        {
            onDeathActivated = dead;
            if (!onDeathActivated && onDeath && Enabled)
                Toggle(true);
            return;
        }

        if (!dead) return;

        if (!Enabled)
            Toggle(true);
        onDeathActivated = true;
    }

    public static void Update()
    {
        if (Cammy.Config.DeathCamMode == Configuration.DeathCamSetting.FreeCam)
            CheckDeath();

        if (!Enabled) return;

        if (InputData.isInputIDPressed.Original(Common.InputData, keybindings[FreeCamBindings.ToggleLock]) || InputData.isInputIDReleased.Original(Common.InputData, keybindings[FreeCamBindings.ControllerToggleLock]))
        {
            locked ^= true;
            if (locked && Game.ForceDisableMovement > 0)
                Game.ForceDisableMovement--;
            else
                Game.ForceDisableMovement++;

            if (locked)
                DisableInputBlockers();
            else
                EnableInputBlockers();
        }

        var loggedIn = DalamudApi.ClientState.IsLoggedIn;

        if (InputData.isInputIDPressed.Original(Common.InputData, keybindings[FreeCamBindings.EndFreeCam]) || InputData.isInputIDPressed.Original(Common.InputData, keybindings[FreeCamBindings.ControllerEndFreeCam]) || (loggedIn ? !locked && Game.ForceDisableMovement == 0 : DalamudApi.GameGui.GetAddonByName("Title") == nint.Zero))
        {
            Toggle();
            return;
        }

        var dt = (float)DalamudApi.Framework.UpdateDelta.TotalSeconds;

        // Read every frame so that the held state stays current even while typing
        var playPressed = WasHotkeyPressed(Cammy.Config.FreeCamPathPlayKey, ref playKeyHeld);
        var resetPressed = WasHotkeyPressed(Cammy.Config.FreeCamPathResetKey, ref resetKeyHeld);
        if (!RaptureAtkModule.Instance()->AtkModule.IsTextInputActive())
        {
            if (resetPressed)
                ResetPath();
            if (playPressed)
                TogglePath();
        }

        if (pathPlaying)
        {
            UpdatePath(dt);
            return;
        }

        if (locked)
        {
            velocity = Vector3.Zero;
            return;
        }

        var movePos = Vector3.Zero;

        var analogInputX = InputData.getAxisInput.Original(Common.InputData, 4) / 100f; // Controller Move Forward / Back
        if (analogInputX != 0)
            movePos.X = analogInputX;

        var analogInputZ = InputData.getAxisInput.Original(Common.InputData, 3) / 100f; // Controller Move Left / Right
        if (analogInputZ != 0)
            movePos.Z = -analogInputZ;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Forward]) || InputData.isInputIDHeld.Original(Common.InputData, 36) && InputData.isInputIDHeld.Original(Common.InputData, 37)) // Left + Right Click
            movePos.X += 1;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Backward]))
            movePos.X -= 1;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Left]) || InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Left2]))
            movePos.Z += 1;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Right]) || InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Right2]))
            movePos.Z -= 1;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Ascend]) || InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Ascend2]) || InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.ControllerAscend]))
            movePos.Y += 1;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.Descend]) || InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.ControllerDescend]))
            movePos.Y -= 1;

        var mouseWheelStatus = InputData.GetMouseWheelStatus();
        if (mouseWheelStatus != 0)
            speed *= 1 + 0.2f * mouseWheelStatus;

        if (InputData.isInputIDHeld.Original(Common.InputData, keybindings[FreeCamBindings.ControllerAdjustSpeedModifier]))
        {
            switch (InputData.getAxisInput.Original(Common.InputData, 6) / 100f) // Controller Move Camera Up / Down
            {
                case >= 0.6f:
                    speed *= 1 + 1.5f * (float)DalamudApi.Framework.UpdateDelta.TotalSeconds;
                    break;
                case <= -0.6f:
                    speed *= 1 - 1.5f * (float)DalamudApi.Framework.UpdateDelta.TotalSeconds;
                    break;
            }
        }

        if (movePos == Vector3.Zero && velocity == Vector3.Zero) return;

        movePos *= 20 * speed;

        if (ImGui.GetIO().KeyShift) // Shift
            movePos *= 10;
        const float halfPI = MathF.PI / 2f;
        var hAngle = gameCamera->currentHRotation + halfPI;
        var vAngle = gameCamera->currentVRotation;
        var direction = new Vector3(MathF.Cos(hAngle) * MathF.Cos(vAngle), MathF.Sin(vAngle), -(MathF.Sin(hAngle) * MathF.Cos(vAngle)));

        var amount = direction * movePos.X;
        var targetVelocity = new Vector3(
            amount.X + movePos.Z * MathF.Sin(gameCamera->currentHRotation - halfPI),
            amount.Y + movePos.Y,
            amount.Z + movePos.Z * MathF.Cos(gameCamera->currentHRotation - halfPI));

        velocity = Vector3.Lerp(velocity, targetVelocity, Easing.SmoothingFactor(Cammy.Config.FreeCamMovementSmoothing, dt));
        if (movePos == Vector3.Zero && velocity.LengthSquared() < 0.0001f)
        {
            velocity = Vector3.Zero;
            return;
        }

        var x = velocity.X * dt;
        var y = velocity.Y * dt;
        var z = velocity.Z * dt;

        if (loggedIn)
        {
            position.X += x;
            position.Y += y;
            position.Z += z;
        }
        else
        {
            gameCamera->lookAtX += x;
            gameCamera->lookAtY = gameCamera->lookAtY2 += y;
            gameCamera->lookAtZ += z;
        }
    }

    private static void UpdatePath(float dt)
    {
        if (savedPositions.Count < 2 || !DalamudApi.ClientState.IsLoggedIn)
        {
            pathPlaying = false;
            return;
        }

        pathTime += dt / Math.Max(Cammy.Config.FreeCamPathDuration, 0.01f);
        if (pathTime >= 1)
        {
            if (Cammy.Config.FreeCamPathLoop)
            {
                pathTime %= 1;
            }
            else
            {
                pathTime = 1;
                pathPlaying = false;
            }
        }

        ApplyPath(pathTime);
    }

    private static void ApplyPath(float t)
    {
        var count = savedPositions.Count;
        if (count < 2) return;

        // Unwrap the horizontal rotations so that the camera always takes the shortest way around
        var rotations = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var rot = savedPositions[i].Item2;
            var h = float.DegreesToRadians(rot.X);
            if (i > 0)
                h = rotations[i - 1].X + WrapAngle(h - float.DegreesToRadians(savedPositions[i - 1].Item2.X));
            rotations[i] = new Vector3(h, float.DegreesToRadians(rot.Y), 0);
        }

        var s = Easing.Ease(t, Cammy.Config.FreeCamPathCurve, Cammy.Config.FreeCamPathDirection) * (count - 1);
        var segment = Math.Min((int)s, count - 2);
        var local = s - segment;

        int Index(int i) => Math.Clamp(i, 0, count - 1);
        Vector3 KeyPosition(int i) => savedPositions[Index(i)].Item1;
        Vector3 KeyRotation(int i) => rotations[Index(i)];

        position = Easing.Spline(KeyPosition(segment - 1), KeyPosition(segment), KeyPosition(segment + 1), KeyPosition(segment + 2), local, Cammy.Config.FreeCamPathCurvature);
        var rotation = Easing.Spline(KeyRotation(segment - 1), KeyRotation(segment), KeyRotation(segment + 1), KeyRotation(segment + 2), local);
        gameCamera->currentHRotation = WrapAngle(rotation.X);
        gameCamera->currentVRotation = Math.Clamp(rotation.Y, freeCamPreset.MinVRotation, freeCamPreset.MaxVRotation);
    }

    private static float WrapAngle(float a) => MathF.IEEERemainder(a, MathF.Tau);

    private static bool CanPlayPath => savedPositions.Count >= 2 && DalamudApi.ClientState.IsLoggedIn;

    private static void TogglePath()
    {
        if (pathPlaying)
        {
            pathPlaying = false;
            return;
        }

        if (!CanPlayPath) return;

        pathPlaying = true;
        if (pathTime >= 1)
            pathTime = 0;
        velocity = Vector3.Zero;
    }

    private static void ResetPath()
    {
        if (savedPositions.Count == 0 || !DalamudApi.ClientState.IsLoggedIn) return;

        pathPlaying = false;
        pathTime = 0;
        GoToSavedPosition(savedPositions[0].Item1, savedPositions[0].Item2);
    }

    private static void GoToSavedPosition(Vector3 pos, Vector2 rot)
    {
        position = pos;
        velocity = Vector3.Zero;
        gameCamera->currentHRotation = float.DegreesToRadians(rot.X);
        gameCamera->currentVRotation = float.DegreesToRadians(rot.Y);
    }

    private static bool WasHotkeyPressed(VirtualKey key, ref bool held)
    {
        var down = key != VirtualKey.NO_KEY && DalamudApi.KeyState.IsVirtualKeyValid(key) && DalamudApi.KeyState[key];
        var pressed = down && !held;
        held = down;
        return pressed;
    }

    private static string GetHotkeyTooltip(VirtualKey key) => key == VirtualKey.NO_KEY ? "No hotkey set" : $"Hotkey: {key.GetFancyName()}";

    private static void DrawPathControls()
    {
        var save = false;
        var canPlay = CanPlayPath;

        ImGui.BeginDisabled(!canPlay);

        if (ImGui.Button(pathPlaying ? "Pause Path" : "Play Path"))
            TogglePath();
        ImGuiEx.SetItemTooltip(GetHotkeyTooltip(Cammy.Config.FreeCamPathPlayKey));

        ImGui.SameLine();

        if (ImGui.Button("Reset"))
            ResetPath();
        ImGuiEx.SetItemTooltip(GetHotkeyTooltip(Cammy.Config.FreeCamPathResetKey));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.SliderFloat("##PathTime", ref pathTime, 0, 1, "%.2f"))
            ApplyPath(pathTime);

        ImGui.EndDisabled();

        if (!canPlay)
            ImGui.TextDisabled("Save at least two positions to play a path.");

        var width = ImGui.GetContentRegionAvail().X / 2;
        ImGui.SetNextItemWidth(width);
        save |= ImGui.DragFloat("Duration", ref Cammy.Config.FreeCamPathDuration, 0.1f, 0.1f, 600, "%.1f s");
        ImGui.SameLine();
        save |= ImGui.Checkbox("Loop", ref Cammy.Config.FreeCamPathLoop);

        ImGui.SetNextItemWidth(width);
        if (ImGui.SliderFloat("Curvature", ref Cammy.Config.FreeCamPathCurvature, 0, 3, "%.2f"))
        {
            save = true;
            if (!pathPlaying && canPlay)
                ApplyPath(pathTime);
        }
        ImGuiEx.SetItemTooltip("How much the path curves through each saved position.\n0 = straight lines (stops at each point), 1 = default, higher = wider curves.");

        ImGui.SetNextItemWidth(width / 2);
        save |= ImGuiEx.EnumCombo("##PathCurve", ref Cammy.Config.FreeCamPathCurve);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(width / 2);
        save |= ImGuiEx.EnumCombo("Easing##PathDirection", ref Cammy.Config.FreeCamPathDirection);

        if (save)
            Cammy.Config.Save();
    }

    public static void Draw()
    {
        if (!Enabled || !Cammy.Config.EnableAdvancedFreeCamControls) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(300, 320) * ImGuiHelpers.GlobalScale, Vector2.PositiveInfinity);

        var useAlpha = Cammy.Config.FadeOutAdvancedFreeCamControls;
        var dt = ImGui.GetIO().DeltaTime;
        using var _ = ImGuiEx.StyleVarBlock.Begin(ImGuiStyleVar.Alpha, advancedControlsAlpha, useAlpha);
        if (!ImGui.Begin("Advanced Controls"))
        {
            if (useAlpha)
                advancedControlsAlpha = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.RectOnly) ? 1 : Math.Max(advancedControlsAlpha - dt / 2, 0.001f);
            ImGui.End();
            return;
        }

        ImGui.DragFloat3("Position", ref position, 0.1f);

        var rotation = new Vector2(float.RadiansToDegrees(Common.CameraManager->worldCamera->currentHRotation), float.RadiansToDegrees(Common.CameraManager->worldCamera->currentVRotation));
        if (ImGui.DragFloat2("Rotation", ref rotation, 0.1f))
        {
            Common.CameraManager->worldCamera->currentHRotation = float.DegreesToRadians(rotation.X);
            Common.CameraManager->worldCamera->currentVRotation = float.DegreesToRadians(rotation.Y);
        }

        if (ImGui.Button("Save Position"))
            savedPositions.Add((position, rotation));

        ImGui.SameLine();

        ImGui.Button("Clear");
        if (ImGui.BeginPopupContextItem(ImU8String.Empty, ImGuiPopupFlags.MouseButtonLeft))
        {
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Selectable(FontAwesomeIcon.TrashAlt.ToIconString()))
                savedPositions.Clear();
            ImGui.PopFont();
            ImGui.EndPopup();
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Fade Window", ref Cammy.Config.FadeOutAdvancedFreeCamControls))
        {
            advancedControlsAlpha = 1;
            Cammy.Config.Save();
        }

        ImGui.Separator();

        DrawPathControls();

        ImGui.Separator();

        ImGui.BeginChild("SavedFreeCamPositions");

        for (int i = 0; i < savedPositions.Count; i++)
        {
            var (pos, rot) = savedPositions[i];

            var clicked = ImGui.Selectable($"P: {pos:F1} R: {rot:F1}##{i}");
            if (ImGui.BeginPopupContextItem(ImU8String.Empty))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Selectable(FontAwesomeIcon.TrashAlt.ToIconString()))
                    savedPositions.RemoveAt(i);
                ImGui.PopFont();
                ImGui.EndPopup();
            }

            if (!clicked) continue;

            pathPlaying = false;
            GoToSavedPosition(pos, rot);
        }

        ImGui.EndChild();

        if (useAlpha)
            advancedControlsAlpha = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.RectOnly) ? 1 : Math.Max(advancedControlsAlpha - dt / 2, 0.001f);

        ImGui.End();
    }

    // Obnoxious
    private static void EnableInputBlockers()
    {
        if (!InputData.isInputIDHeld.IsHooked)
            InputData.isInputIDHeld.CreateHook((inputData, inputID) => !keybindings.ContainsValue(inputID) && InputData.isInputIDHeld.Original(inputData, inputID));
        InputData.isInputIDHeld.Hook.Enable();

        if (!InputData.isInputIDPressed.IsHooked)
            InputData.isInputIDPressed.CreateHook((inputData, inputID) => !keybindings.ContainsValue(inputID) && InputData.isInputIDPressed.Original(inputData, inputID));
        InputData.isInputIDPressed.Hook.Enable();

        if (!InputData.isInputIDLongPressed.IsHooked)
            InputData.isInputIDLongPressed.CreateHook((inputData, inputID) => !keybindings.ContainsValue(inputID) && InputData.isInputIDLongPressed.Original(inputData, inputID));
        InputData.isInputIDLongPressed.Hook.Enable();

        if (!InputData.isInputIDReleased.IsHooked)
            InputData.isInputIDReleased.CreateHook((inputData, inputID) => !keybindings.ContainsValue(inputID) && InputData.isInputIDReleased.Original(inputData, inputID));
        InputData.isInputIDReleased.Hook.Enable();

        if (!InputData.getAxisInput.IsHooked)
            InputData.getAxisInput.CreateHook((inputData, inputID) => inputID is not (3 or 4) ? InputData.getAxisInput.Original(inputData, inputID) : 0);
        InputData.getAxisInput.Hook.Enable();

        if (!EmoteController.cancelEmote.IsHooked) 
            EmoteController.cancelEmote.CreateHook((_, _) => false);
        EmoteController.cancelEmote.Hook.Enable();
    }

    private static void DisableInputBlockers()
    {
        InputData.isInputIDHeld.Hook.Disable();
        InputData.isInputIDPressed.Hook.Disable();
        InputData.isInputIDLongPressed.Hook.Disable();
        InputData.isInputIDReleased.Hook.Disable();
        InputData.getAxisInput.Hook.Disable();
        EmoteController.cancelEmote.Hook.Disable();
    }
}
