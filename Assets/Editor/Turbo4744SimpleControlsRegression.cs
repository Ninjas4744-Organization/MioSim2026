using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Games.Rebuilt.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using MoSimCore.GameSelection;
using Ninjas4744.MoSim;
using RobotFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Exercises the real MoSim action map and scheduled robot in the Rebuilt scene.</summary>
[InitializeOnLoad]
public static class Turbo4744SimpleControlsRegression
{
    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string source,
            bundleSha256,
            runtimeDllSha256;
        public string editorRuntimeDllSha256;
        public List<string> checks = new List<string>();
        public List<string> errors = new List<string>();
        public List<string> editorServiceErrors = new List<string>();
    }

    private const string Pending = "Turbo.SimpleControlsPending";
    private static readonly string Out = Path.GetFullPath(
        Application.dataPath + "/../BuildArtifacts"
    );
    private static Keyboard keyboard;
    private static Gamepad gamepad;
    private static Turbo4744 robot;
    private static BaseTimerManager timer;
    private static Report report;
    private static IEnumerator<float> steps;
    private static float next;
    private static double began;
    private static AssetBundle bundle;

    static Turbo4744SimpleControlsRegression()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

    public static void Run()
    {
        Directory.CreateDirectory(Out);
        SessionState.SetString(Pending + ".company", PlayerSettings.companyName);
        SessionState.SetString(Pending + ".product", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboSimpleControls";
        SessionState.SetBool(Pending, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool(Pending, false))
            return;
        InputSystem.settings = Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings
            .EditorInputBehaviorInPlayMode
            .AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("TurboSimpleKeyboard");
        gamepad = InputSystem.AddDevice<Gamepad>("TurboSimpleGamepad");
        SelectedGameManager.PendingSelectedGameGuid = Resources
            .Load<GameOption>("GameOptions/Rebuilt")
            .GetGuid();
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = new Report();
            robot = null;
            steps = null;
            began = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                Pending + ".company",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString(
                Pending + ".product",
                "MoSimulator"
            );
            SessionState.SetBool(Pending, false);
            EditorApplication.Exit(SessionState.GetInt(Pending + ".exit", 1));
        }
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - began > 120)
                throw new TimeoutException("Simple controls regression exceeded 120 seconds");
            if (steps == null)
            {
                if (EditorApplication.timeSinceStartup - began < 5 || !BaseGameManager.Instance)
                    return;
                timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                Enable(true);
                foreach (var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))
                    old.gameObject.SetActive(false);
                foreach (var camera in Camera.allCameras)
                    camera.enabled = false;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 120;
                report.source = Ninjas4744Builder.Root + "/Turbo4744.asset";
                RebuiltRobotMetadataSO metadata;
                if (Environment.GetCommandLineArgs().Contains("-turboBundle"))
                {
                    report.source = Path.GetFullPath(
                        Application.dataPath
                            + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets"
                    );
                    bundle = AssetBundle.LoadFromFile(report.source);
                    if (!bundle)
                        throw new InvalidOperationException("Built mod bundle did not load");
                    report.bundleSha256 = Hash(report.source);
                    report.runtimeDllSha256 = Hash(
                        Path.Combine(Path.GetDirectoryName(report.source), "Ninjas4744.dll")
                    );
                    report.editorRuntimeDllSha256 = Hash(typeof(Turbo4744).Assembly.Location);
                    Turbo4744NetBurstBuilder.VerifyRuntime();
                    metadata = bundle.LoadAllAssets<RebuiltRobotMetadataSO>().Single();
                }
                else
                    metadata = AssetDatabase.LoadAssetAtPath<RebuiltRobotMetadataSO>(report.source);
                var actual = metadata.AlternateRobots.Single(v => v.DisplayName == "Actual Turbo");
                Application.logMessageReceived += Log;
                Check(
                    metadata.RobotName == "Turbo"
                        && metadata.RobotPrefab.GetComponent<Turbo4744>().simpleControls
                        && !actual.RobotPrefab.GetComponent<Turbo4744>().simpleControls,
                    "Turbo defaults to simple controls and Actual Turbo keeps team controls"
                );
                Spawn(metadata.RobotPrefab);
                steps = Steps(metadata).GetEnumerator();
                next = Time.time;
            }
            if (Time.time < next)
                return;
            if (steps.MoveNext())
                next = Time.time + steps.Current;
            else
                Finish();
        }
        catch (Exception error)
        {
            report.errors.Add(error.ToString());
            Finish();
        }
    }

    private static void Spawn(GameObject prefab)
    {
        robot = Object
            .Instantiate(prefab, new Vector3(6.8f, .04f, -2f), Quaternion.identity)
            .GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        // This is an empty-robot control fixture. Preloads normally become
        // live fuel through MoSim's spawn initializer, which this fixture skips.
        foreach (var preload in robot.preloads)
            preload.SetActive(false);
        var player = robot.GetComponent<PlayerInput>();
        player.neverAutoSwitchControlSchemes = true;
        player.actions.devices = new InputDevice[] { keyboard, gamepad };
    }

    private static IEnumerable<float> Steps(RebuiltRobotMetadataSO metadata)
    {
        yield return 1f;
        Check(
            robot.simpleControls && !robot.Deployed && !robot.Collecting,
            "Simple variant stays stowed on enable"
        );
        Keys(Key.LeftShift);
        yield return .25f;
        Check(robot.Deployed && robot.Collecting, "Intake press deploys and collects");
        yield return .25f;
        Check(robot.Deployed, "Holding intake does not toggle repeatedly");
        Keys();
        yield return .15f;
        Check(robot.Deployed && robot.Collecting, "Intake remains on after release");
        // Both events happen between physics polls: the performed edge must survive.
        Keys(Key.LeftShift);
        Keys();
        yield return .2f;
        Check(!robot.Deployed && !robot.Collecting, "Quick intake tap toggles off once");
        yield return .2f;
        Check(!robot.Deployed, "Quick tap is not processed twice");
        Keys(Key.K);
        yield return .2f;
        Check(
            robot.Deployed && robot.ShootingState != Turbo4744.ShootState.Idle,
            "Holding shoot temporarily opens intake and starts automatic aiming"
        );
        for (int i = 0; i < 80 && !robot.Feeding; i++)
            yield return .1f;
        Check(
            robot.Feeding && robot.AimReady && robot.TargetShooterRpm > 0,
            "Held automatic shoot aligns, spins up and enables feeding"
        );
        Keys();
        yield return .1f;
        CheckStopped("Releasing shoot stops feeding, aiming and shooter commands");
        Check(!robot.Deployed && !robot.Collecting, "Release restores toggled-off intake");
        Keys(Key.K);
        Keys();
        yield return .2f;
        CheckStopped("Quick shoot tap never latches shooting");
        Keys(Key.LeftShift);
        yield return .15f;
        Keys(Key.K);
        yield return .2f;
        Keys();
        yield return .15f;
        CheckStopped("Shoot release stops while intake is toggled on");
        Check(robot.Deployed && robot.Collecting, "Release preserves toggled-on intake");
        Keys(Key.E);
        yield return .2f;
        Check(
            robot.ShootingState == Turbo4744.ShootState.PrepareDelivery
                || robot.ShootingState == Turbo4744.ShootState.Delivery,
            "Auto Pass is held delivery"
        );
        Keys();
        yield return .1f;
        CheckStopped("Releasing Auto Pass stops");
        Keys(Key.Q);
        yield return .2f;
        Check(robot.TargetShooterRpm > 0, "Manual shoot is still available");
        Keys(Key.K);
        yield return .2f;
        Check(
            robot.ShootingState != Turbo4744.ShootState.Idle,
            "Switching held shoot actions does not stop the new shot"
        );
        Keys(Key.K, Key.Z);
        yield return .2f;
        CheckStopped("Stow cancels a held shot");
        Check(!robot.Deployed, "Stow closes intake");
        Keys(Key.K);
        yield return .2f;
        CheckStopped("Held trigger cannot undo Stow");
        Keys();
        yield return .1f;
        Keys(Key.K);
        yield return .2f;
        Check(
            robot.Deployed && robot.ShootingState != Turbo4744.ShootState.Idle,
            "Releasing and pressing trigger permits shooting again"
        );
        Enable(false);
        yield return .2f;
        CheckStopped("Disable stops all shooting");
        Check(
            robot.TargetIntakeMotorRps == 0 && robot.TargetIndexerMotorRps == 0,
            "Disable stops intake and indexer"
        );
        Keys();
        Enable(true);
        yield return .2f;
        CheckStopped("Re-enable without held input stays idle");

        Pad(new GamepadState { leftTrigger = 1 });
        yield return .2f;
        Pad(new GamepadState());
        yield return .15f;
        Check(robot.Deployed && robot.Collecting, "Gamepad intake trigger toggles on");
        Pad(new GamepadState { rightTrigger = 1 });
        yield return .2f;
        Check(
            robot.ShootingState != Turbo4744.ShootState.Idle,
            "Gamepad shoot trigger holds shooting"
        );
        Pad(new GamepadState());
        yield return .15f;
        CheckStopped("Gamepad trigger release stops shooting");
        Check(robot.Deployed, "Gamepad shooting preserves intake toggle");

        var shoot = robot.GetComponent<PlayerInput>().currentActionMap.FindAction("AutoShoot");
        int binding = shoot.bindings.ToList().FindIndex(b => b.path == "<Keyboard>/k");
        Check(binding >= 0, "MoSim shoot keyboard binding exists");
        shoot.ApplyBindingOverride(binding, "<Keyboard>/g");
        Keys(Key.G);
        yield return .2f;
        Check(
            robot.ShootingState != Turbo4744.ShootState.Idle,
            "MoSim shoot rebinding is respected"
        );
        Keys();
        yield return .15f;
        CheckStopped("Rebound shoot release stops shooting");
        shoot.RemoveBindingOverride(binding);

        // Outposts retain registered robots until the scene is torn down.
        robot.gameObject.SetActive(false);
        yield return .15f;
        Spawn(metadata.AlternateRobots.Single(v => v.DisplayName == "Actual Turbo").RobotPrefab);
        yield return .7f;
        Keys(Key.K);
        yield return .2f;
        Keys();
        yield return .2f;
        Check(
            robot.ShootingState != Turbo4744.ShootState.Idle,
            "Actual Turbo still latches shoot after release"
        );
        Keys(Key.Z);
        yield return .2f;
        CheckStopped("Actual Turbo still supports Stow");
        Keys();
    }

    private static void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private static void Pad(GamepadState state)
    {
        InputSystem.QueueStateEvent(gamepad, state);
        InputSystem.Update();
    }

    private static void Enable(bool enabled) =>
        typeof(BaseTimerManager)
            .GetProperty("CurrentRobotState")
            .SetValue(timer, enabled ? RobotState.Enabled : RobotState.Disabled);

    private static void CheckStopped(string label) =>
        Check(
            robot.ShootingState == Turbo4744.ShootState.Idle
                && !robot.Feeding
                && robot.TargetShooterRpm == 0
                && robot.TargetAcceleratorMotorRps == 0
                && robot.MoveSpeedMultiplier == 1,
            label
        );

    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
        report.checks.Add(label);
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter
                .ToString(sha.ComputeHash(stream))
                .Replace("-", "")
                .ToLowerInvariant();
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (Turbo4744BenchLog.RecordEditorServiceError(message, stack, report.editorServiceErrors))
            return;
        if ((type == LogType.Exception || type == LogType.Error) && report.errors.Count < 20)
            report.errors.Add(message + "\n" + stack);
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        report.passed = report.errors.Count == 0;
        File.WriteAllText(Out + "/simple-controls.json", JsonUtility.ToJson(report, true));
        SessionState.SetInt(Pending + ".exit", report.passed ? 0 : 1);
        if (bundle)
            bundle.Unload(false);
        EditorApplication.ExitPlaymode();
    }
}
