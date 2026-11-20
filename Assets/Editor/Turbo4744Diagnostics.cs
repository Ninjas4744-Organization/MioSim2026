using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.GamePieceSystem;
using GameSystems.Management;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using MoSimCore.GameSelection;
using Ninjas4744.MoSim;
using RobotFramework;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

/// <summary>Editor-only integration checks. This file is not part of the mod DLL.</summary>
[InitializeOnLoad]
public static class Turbo4744Diagnostics
{
    [Serializable] private sealed class Report
    {
        public bool completed,finiteAndStable,driveMoved,railsDeployed,pickedUp,shotReleased,disabledStopped;
        public bool inputsBound,endToEndCycle,stowed,preloadsRetained;
        public float driveDistance,intakeExtension,hopperExtension,maxFuelSpeed;
        public Vector3 finalPosition;
        public List<string> errors=new List<string>();
        public List<string> samples=new List<string>();
        public List<string> contacts=new List<string>();
    }
    private static Report report;
    private static Turbo4744 robot;
    private static Rigidbody body;
    private static Rigidbody collectedFuel;
    private static DriveController drive;
    private static Turbo4744BenchDriver input;
    private static BaseTimerManager timer;
    private static double startTime;
    private static float simulationStart;
    private static float intakeStart,hopperStart;
    private static Vector3 driveStart;
    private static readonly List<Rigidbody> fuel=new List<Rigidbody>();
    private static int phase;
    private static int sampleFrame;
    private static Keyboard testKeyboard;
    private static readonly string Artifacts=Path.GetFullPath(Application.dataPath+"/../BuildArtifacts");

    static Turbo4744Diagnostics()
    {
        EditorApplication.playModeStateChanged+=OnPlayMode;
    }

    public static void RunAndBuild()
    {
        Ninjas4744Builder.Assemble();
        SessionState.SetBool("Turbo.BuildAfterDiagnostics",true);
        Run();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareInput()
    {
        if(!SessionState.GetBool("Turbo.DiagnosticPending",false)) return;
        InputSystem.settings=Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testKeyboard=InputSystem.AddDevice<Keyboard>("Turbo4744TestKeyboard");
        var game=Resources.LoadAll<GameOption>("GameOptions").First(g=>g.GameYear==2026);
        SelectedGameManager.PendingSelectedGameGuid=game.GetGuid();
    }

    public static void Run()
    {
        if(!AssetDatabase.LoadAssetAtPath<GameObject>(Ninjas4744Builder.RobotPath))
            throw new InvalidOperationException("Assemble Turbo before running integration checks");
        Directory.CreateDirectory(Artifacts);
        SessionState.SetString("Turbo.OldCompany",PlayerSettings.companyName);
        SessionState.SetString("Turbo.OldProduct",PlayerSettings.productName);
        PlayerSettings.companyName="Ninjas4744ModTests";
        PlayerSettings.productName="Turbo4744Sandbox";
        SessionState.SetBool("Turbo.DiagnosticPending",true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame=Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if(!SessionState.GetBool("Turbo.DiagnosticPending",false)) return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            report=new Report();
            startTime=EditorApplication.timeSinceStartup;
            phase=-1;
            Application.logMessageReceived+=OnLog;
            EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName=SessionState.GetString("Turbo.OldCompany","CascadeStudios");
            PlayerSettings.productName=SessionState.GetString("Turbo.OldProduct","MoSimulator");
            SessionState.SetBool("Turbo.DiagnosticPending",false);
            int code=SessionState.GetInt("Turbo.DiagnosticExit",1);
            bool build=SessionState.GetBool("Turbo.BuildAfterDiagnostics",false);
            SessionState.SetBool("Turbo.BuildAfterDiagnostics",false);
            if(code==0 && build)
            {
                try { Ninjas4744Builder.Build(); }
                catch(Exception error){Debug.LogException(error);code=1;}
            }
            EditorApplication.Exit(code);
        }
    }

    private static void OnLog(string message,string stack,LogType type)
    {
        if((type==LogType.Exception||type==LogType.Error) && report!=null && report.errors.Count<30)
            report.errors.Add(message+"\n"+stack);
    }

    private static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-startTime>150)
                throw new TimeoutException("Rebuilt diagnostic run exceeded 150 seconds");
            if(phase<0)
            {
                if(EditorApplication.timeSinceStartup-startTime<5 || BaseGameManager.Instance==null) return;
                timer=BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                ForceEnabled(true);
                foreach(var camera in Camera.allCameras) camera.enabled=false;
                QualitySettings.vSyncCount=0;
                Application.targetFrameRate=120;
                foreach(var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None)) old.gameObject.SetActive(false);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Ninjas4744Builder.RobotPath);
                var instance=Object.Instantiate(prefab,new Vector3(-6f,.04f,-2f),Quaternion.identity);
                robot=instance.GetComponent<Turbo4744>();
                robot.SetAlliance(Alliance.Blue);
                report.inputsBound=robot.TranslateAction!=null && robot.RestartAction!=null;
                body=instance.GetComponent<Rigidbody>();
                drive=instance.GetComponent<DriveController>();
                input=instance.AddComponent<Turbo4744BenchDriver>();input.drive=drive;
                intakeStart=robot.intakeRail.transform.localPosition.z;
                hopperStart=robot.hopperRail.transform.localPosition.z;
                simulationStart=Time.time;
                if(robot.preloads.Length!=8||robot.preloads.Any(p=>!p.GetComponent<RebuiltGamePieceController>()||!p.GetComponent<Rigidbody>()))
                    throw new InvalidOperationException("MoSim preloads must be real fuel objects, not position markers");
                var preloadManager=Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
                var removable=Object.FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None)
                    .Where(f=>f.transform.position.x>0).Take(8).Select(f=>f.gameObject).ToArray();
                typeof(RebuiltFuelPreloadManager).GetField("removableFuel",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(preloadManager,removable);
                Object.FindFirstObjectByType<RebuiltSpawnInitializer>().HandleSpawnedRobots(new[]{robot});
                fuel.AddRange(robot.preloads.Select(p=>p.GetComponent<Rigidbody>()));
                phase=0;
                sampleFrame=-1;
                report.errors.Clear();
                return;
            }
            if(!robot) throw new InvalidOperationException("Turbo was destroyed during the diagnostic run");
            float elapsed=Time.time-simulationStart;
            int sample=(int)(elapsed*5);
            if(sample>sampleFrame)
            {
                sampleFrame=sample;
                report.samples.Add(elapsed.ToString("F2")+" robot "+body.position.ToString("F3")+" angles "+body.rotation.eulerAngles.ToString("F1")+" intake input "+robot.IntakeAction.IsPressed()+" shoot input "+robot.ManualShoot.IsPressed());
                if(elapsed<1f)foreach(var wheel in robot.GetComponentsInChildren<WheelCollider>())
                    if(wheel.GetGroundHit(out var hit))report.samples.Add("Wheel ground: "+hit.collider.name);
                foreach(var piece in fuel.Where(f=>f))
                    report.samples.Add(elapsed.ToString("F2")+" fuel "+fuel.IndexOf(piece)+" local "+robot.transform.InverseTransformPoint(piece.position).ToString("F3")+" vel "+piece.velocity.ToString("F3")+" shooter "+robot.CurrentShooterRpm+" real "+string.Join(",",robot.shooterRollers.Select(r=>r.realRpm.ToString("F0")))+" feeding "+robot.Feeding);
            }
            if(!Finite(body.position)||!Finite(body.velocity)||body.position.y<-.35f||body.velocity.magnitude>25f)
                throw new InvalidOperationException("Robot physics became unstable: "+body.position+" velocity "+body.velocity);
            if(elapsed<1.5f)
            {
                driveStart=body.position;
                Drive(0,0,0,DriveController.DrivePerspective.RobotRelative);
            }
            else if(elapsed<3.5f)
            {
                Drive(1.2f,0,0,DriveController.DrivePerspective.RobotRelative);
            }
            else if(elapsed<6f)
            {
                if(phase<1)
                {
                    report.driveDistance=Vector3.Distance(driveStart,body.position);
                    report.driveMoved=report.driveDistance>.5f;
                    report.preloadsRetained=fuel.Count==8 && fuel.All(f=>{
                        var p=robot.transform.InverseTransformPoint(f.position);
                        return Mathf.Abs(p.x)<.36f && p.y>.16f && p.y<.60f && p.z>.03f && p.z<.65f;
                    });
                    foreach(var piece in fuel)Object.Destroy(piece.gameObject);
                    fuel.Clear();
                    phase=1;
                }
                Drive(0,0,0,DriveController.DrivePerspective.RobotRelative);
                Press(Key.LeftShift);
            }
            else if(elapsed<9f)
            {
                if(phase<2)
                {
                    report.intakeExtension=robot.intakeRail.transform.localPosition.z-intakeStart;
                    report.hopperExtension=robot.hopperRail.transform.localPosition.z-hopperStart;
                    report.railsDeployed=Mathf.Abs(report.intakeExtension-.24f)<.035f && Mathf.Abs(report.hopperExtension-.22946f)<.035f;
                    SpawnFuel(new Vector3(0,.075f,.68f));
                    collectedFuel=fuel.Last();
                    phase=2;
                }
                Drive(.35f,0,0,DriveController.DrivePerspective.RobotRelative);
                foreach(var piece in fuel.Where(f=>f))
                {
                    var local=robot.transform.InverseTransformPoint(piece.position);
                    if(local.z<.42f && local.y>.16f && Mathf.Abs(local.x)<.35f) report.pickedUp=true;
                }
            }
            else if(elapsed<24f)
            {
                if(phase<3 && elapsed>9.4f)
                {
                    // A ball at the measured shooter channel checks the physical
                    // launch stage independently of floor-pickup tuning.
                    SpawnFuel(new Vector3(0,.461f,-.055f));
                    phase=3;
                }
                Drive(0,0,0,DriveController.DrivePerspective.RobotRelative);
                // Exercise the actual hard-pump request after automatic soft
                // pumping begins. A collected ball must travel the whole tray.
                if(elapsed>10.2f&&elapsed<10.35f)Press(Key.Q,Key.V);else Press(Key.Q);
                foreach(var piece in fuel.Where(f=>f))
                {
                    report.maxFuelSpeed=Mathf.Max(report.maxFuelSpeed,piece.velocity.magnitude);
                    var local=robot.transform.InverseTransformPoint(piece.position);
                    if(piece.velocity.magnitude>3f && local.y>.55f && local.z<-.2f)
                    {
                        report.shotReleased=true;
                        if(piece==collectedFuel)report.endToEndCycle=true;
                    }
                }
            }
            else if(elapsed<25f)
            {
                Press();
                ForceEnabled(false);
            }
            else if(elapsed<27.5f)
            {
                if(phase<4)
                {
                    report.disabledStopped=robot.CurrentShooterRpm==0f && !robot.Feeding &&
                        robot.intakeRollers.Concat(robot.floorRollers).Concat(robot.shooterRollers).Concat(robot.acceleratorRollers).All(r=>Mathf.Abs(r.realRpm)<1f);
                    ForceEnabled(true);
                    phase=4;
                }
                Press(Key.Z);
            }
            else
            {
                Press();
                report.stowed=Mathf.Abs(robot.intakeRail.transform.localPosition.z-intakeStart)<.035f &&
                    Mathf.Abs(robot.hopperRail.transform.localPosition.z-hopperStart)<.035f;
                report.finiteAndStable=Finite(body.position)&&Mathf.Abs(body.position.y)<.2f;
                report.finalPosition=body.position;
                report.completed=true;
                Finish();
            }
        }
        catch(Exception error)
        {
            report=report??new Report();
            report.errors.Add(error.ToString());
            Finish();
        }
    }

    private static void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(keys));
        InputSystem.Update();
    }
    private static void Drive(float x,float y,float omega,DriveController.DrivePerspective perspective)
    {input.vx=x;input.vy=y;input.omega=omega;}

    private static void SpawnFuel(Vector3 local)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
        var go=Object.Instantiate(prefab,robot.transform.TransformPoint(local),Quaternion.identity);
        var world=GameObject.FindWithTag("GamePieceWorld");
        if(world) go.transform.SetParent(world.transform,true);
        var rb=go.GetComponent<Rigidbody>()??go.GetComponentInChildren<Rigidbody>();
        if(!rb) throw new InvalidOperationException("Official fuel prefab has no Rigidbody");
        fuel.Add(rb);
        var contact=go.AddComponent<TurboFuelContactAudit>();
        contact.record=message=>{if(report.contacts.Count<180)report.contacts.Add((Time.time-simulationStart).ToString("F3")+" fuel "+fuel.IndexOf(rb)+" "+message);};
    }

    private static void ForceEnabled(bool enabled)
    {
        var property=typeof(BaseTimerManager).GetProperty("CurrentRobotState");
        property.SetValue(timer,enabled?RobotState.Enabled:RobotState.Disabled);
    }

    private static bool Finite(Vector3 v)=>!(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));

    private static void Finish()
    {
        EditorApplication.update-=Tick;
        Application.logMessageReceived-=OnLog;
        File.WriteAllText(Artifacts+"/physics-diagnostics.json",JsonUtility.ToJson(report,true));
        bool passed=report.completed&&report.finiteAndStable&&report.driveMoved&&report.railsDeployed&&report.pickedUp&&report.shotReleased&&report.disabledStopped&&report.inputsBound&&report.endToEndCycle&&report.stowed&&report.preloadsRetained&&report.errors.Count==0;
        Debug.Log("TURBO_PHYSICS_DIAGNOSTICS "+JsonUtility.ToJson(report));
        SessionState.SetInt("Turbo.DiagnosticExit",passed?0:1);
        EditorApplication.ExitPlaymode();
    }
}

public sealed class TurboFuelContactAudit:MonoBehaviour
{
    public Action<string> record;
    private readonly HashSet<string> seen=new HashSet<string>();
    private void OnCollisionEnter(Collision collision)
    {
        if(seen.Add(collision.collider.name))
            record?.Invoke(collision.collider.name+" normal "+collision.contacts[0].normal.ToString("F2"));
    }
}
