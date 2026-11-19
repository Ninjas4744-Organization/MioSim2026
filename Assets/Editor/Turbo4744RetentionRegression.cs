using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.GamePieceSystem;
using Games.Rebuilt.FieldScripts;
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

/// Collect field fuel, then hold it through driving, turning, disable and stow.
/// Loads the release bundle and never parents, teleports or freezes collected fuel.
[InitializeOnLoad]
public static class Turbo4744RetentionRegression
{
    [Serializable] private sealed class Case
    {
        public int heading,offered,collected,retained;
        public bool stowed,stable;
        public float maxRobotSpeed;
        public List<string> escapes=new List<string>();
        public List<string> samples=new List<string>();
        public List<string> contacts=new List<string>();
    }
    [Serializable] private sealed class Report
    {
        public bool passed;
        public string prefabSource,bundleSha256,runtimeDllSha256;
        public string fixture="Nine field balls; held intake, release, forward/reverse, 90 deg/s turn, 60 deg/s arcs, disable, stow";
        public List<Case> cases=new List<Case>();
        public List<string> errors=new List<string>();
    }
    private sealed class Piece
    {
        public Rigidbody body;
        public float insideSince=-1;
        public bool collected,escaped;
    }
    private static readonly int[] Headings={0,90};
    private static readonly string Out=Path.GetFullPath(Application.dataPath+"/../BuildArtifacts");
    private static readonly List<Piece> fuel=new List<Piece>();
    private static Report report;
    private static Case current;
    private static Turbo4744 robot;
    private static BaseTimerManager timer;
    private static Keyboard keyboard;
    private static AssetBundle bundle;
    private static GameObject prefab;
    private static int caseIndex,lastSample;
    private static float began,hopperClosed,intakeClosed;
    private static double wallStart;
    private static string label;

    static Turbo4744RetentionRegression(){EditorApplication.playModeStateChanged+=Mode;}
    public static void Run()
    {
        SessionState.SetString("Turbo.RetentionLabel",Environment.GetCommandLineArgs().Contains("-turboFixed")?"after":"before");
        SessionState.SetString("Turbo.RetentionOldCompany",PlayerSettings.companyName);
        SessionState.SetString("Turbo.RetentionOldProduct",PlayerSettings.productName);
        PlayerSettings.companyName="Ninjas4744ModTests";
        PlayerSettings.productName="TurboRetentionSandbox";
        SessionState.SetBool("Turbo.RetentionPending",true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame=Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if(!SessionState.GetBool("Turbo.RetentionPending",false))return;
        InputSystem.settings=Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("TurboRetentionKeyboard");
    }
    private static void Mode(PlayModeStateChange state)
    {
        if(!SessionState.GetBool("Turbo.RetentionPending",false))return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            report=new Report();caseIndex=0;wallStart=EditorApplication.timeSinceStartup;robot=null;
            label=SessionState.GetString("Turbo.RetentionLabel","before");
            EditorApplication.update+=Tick;Application.logMessageReceived+=Log;
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName=SessionState.GetString("Turbo.RetentionOldCompany","CascadeStudios");
            PlayerSettings.productName=SessionState.GetString("Turbo.RetentionOldProduct","MoSim");
            SessionState.SetBool("Turbo.RetentionPending",false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.RetentionExit",1));
        }
    }
    private static void Log(string message,string stack,LogType type)
    {
        if(robot&&(type==LogType.Exception||type==LogType.Error)&&report.errors.Count<15)report.errors.Add(message+"\n"+stack);
    }
    private static void Enabled(bool enabled)=>typeof(BaseTimerManager).GetProperty("CurrentRobotState").SetValue(timer,enabled?RobotState.Enabled:RobotState.Disabled);
    private static void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();
    }
    private static void Spawn()
    {
        var parent=Object.FindFirstObjectByType<RobotSpawnController>().transform;
        robot=Object.Instantiate(prefab,new Vector3(-1.2f,.04f,-1.5f),Quaternion.Euler(0,Headings[caseIndex],0),parent).GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        hopperClosed=robot.hopperRail.transform.localPosition.z;
        intakeClosed=robot.intakeRail.transform.localPosition.z;
        current=new Case{heading=Headings[caseIndex],stable=true};report.cases.Add(current);
        var manager=Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
        var fieldFuel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
        var removable=Enumerable.Range(0,8).Select(i=>Object.Instantiate(fieldFuel,new Vector3(7,.075f,3),Quaternion.identity)).ToArray();
        typeof(RebuiltFuelPreloadManager).GetField("removableFuel",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(manager,removable);
        Object.FindFirstObjectByType<RebuiltSpawnInitializer>().HandleSpawnedRobots(new[]{robot});
        foreach(var preload in robot.preloads)Object.Destroy(preload);
        // Keep the real field geometry, but isolate the offered packet from
        // unrelated field fuel so accidental pile contacts do not steer it away.
        foreach(var other in Object.FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None))other.gameObject.SetActive(false);
        fuel.Clear();
        for(int row=0;row<3;row++)for(int col=-1;col<=1;col++)
        {
            var go=Object.Instantiate(fieldFuel,robot.transform.TransformPoint(new Vector3(col*.16f,.075f,.78f+row*.22f)),Quaternion.identity);
            go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform,true);
            var piece=new Piece{body=go.GetComponent<Rigidbody>()};fuel.Add(piece);
            int index=fuel.Count-1;
            go.AddComponent<TurboFuelContactAudit>().record=message=>{if(current.contacts.Count<120)current.contacts.Add((Time.time-began).ToString("F2")+" fuel "+index+" "+message);};
        }
        current.offered=fuel.Count;began=Time.time;lastSample=-1;Enabled(true);
    }
    private static void Measure(float t)
    {
        var rb=robot.GetComponent<Rigidbody>();
        current.maxRobotSpeed=Mathf.Max(current.maxRobotSpeed,rb.velocity.magnitude);
        current.stable &= float.IsFinite(rb.position.y)&&Mathf.Abs(rb.position.y)<.2f&&rb.velocity.magnitude<12;
        foreach(var piece in fuel)
        {
            var p=robot.transform.InverseTransformPoint(piece.body.position);
            bool inside=Mathf.Abs(p.x)<.31f&&p.y>.22f&&p.y<.52f&&p.z>.08f&&p.z<.56f;
            if(inside)
            {
                if(piece.insideSince<0)piece.insideSince=t;
                if(t-piece.insideSince>.2f&&!piece.collected){piece.collected=true;current.collected++;}
            }
            else piece.insideSince=-1;
            if(piece.collected&&!piece.escaped&&(Mathf.Abs(p.x)>.48f||p.y<.12f||p.y>.68f||p.z<-.2f||p.z>.8f))
            {
                piece.escaped=true;
                current.escapes.Add(t.ToString("F2")+" fuel "+fuel.IndexOf(piece)+" local "+p.ToString("F3")+" velocity "+piece.body.velocity.ToString("F3")+" intake rpm "+string.Join(",",robot.intakeRollers.Select(r=>r.realRpm.ToString("F0"))));
            }
        }
        int sample=(int)(t*2);
        if(sample!=lastSample)
        {
            lastSample=sample;
            current.samples.Add(t.ToString("F2")+" robot "+rb.position.ToString("F3")+" intake "+robot.IntakeAction.IsPressed()+" collected "+current.collected);
            foreach(var piece in fuel)current.samples.Add(t.ToString("F2")+" fuel "+fuel.IndexOf(piece)+" local "+robot.transform.InverseTransformPoint(piece.body.position).ToString("F3")+" captured "+piece.collected);
        }
    }
    private static void Cleanup()
    {
        foreach(var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))outpost.Robots.Remove(robot);
        foreach(var piece in fuel)if(piece.body)Object.Destroy(piece.body.gameObject);
        Object.Destroy(robot.gameObject);robot=null;
    }
    private static string Digest(string file)
    {
        using(var stream=File.OpenRead(file))using(var hash=SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    private static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-wallStart>300)throw new TimeoutException("Retention regression exceeded 300 seconds");
            if(!robot)
            {
                if(EditorApplication.timeSinceStartup-wallStart<5||!BaseGameManager.Instance)return;
                timer=BaseGameManager.Instance.GetComponent<BaseTimerManager>();timer.PauseTimer();
                foreach(var camera in Camera.allCameras)camera.enabled=false;
                foreach(var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))old.gameObject.SetActive(false);
                string file=Path.Combine(Application.dataPath,"../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets");
                bundle=AssetBundle.LoadFromFile(file);
                report.bundleSha256=Digest(file);
                report.runtimeDllSha256=Digest(Path.Combine(Path.GetDirectoryName(file),"Ninjas4744.dll"));
                if(report.runtimeDllSha256!=Digest(typeof(Turbo4744).Assembly.Location))
                    throw new InvalidOperationException("Built mod DLL differs from the runtime being tested. Build again.");
                prefab=bundle.LoadAllAssets<BaseModpackSO>().Single(p=>p.ModpackName=="Ninjas 4744 Turbo 2026").Robots.Single().RobotPrefab;
                report.prefabSource=file;Spawn();return;
            }
            float t=Time.time-began;Measure(t);
            var drive=robot.GetComponent<DriveController>();
            if(t<1){Press();drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<2.5f){Press(Key.LeftShift);drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<7){Press(Key.LeftShift);drive.OverrideInput(.5f,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<10){Press(Key.LeftShift);drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<10.5f){Press();drive.OverrideInput(.8f,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<11.5f){Press();drive.OverrideInput(-.8f,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<13){Press();drive.OverrideInput(0,0,90f,DriveController.DrivePerspective.RobotRelative);}
            else if(t<16){Press();drive.OverrideInput(.3f,0,60f,DriveController.DrivePerspective.RobotRelative);}
            else if(t<19){Press();drive.OverrideInput(0,.35f,-60f,DriveController.DrivePerspective.RobotRelative);}
            else if(t<21){Press();drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);Enabled(false);}
            else if(t<23){Enabled(true);Press(Key.Z);drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);}
            else if(t<25){Press();drive.OverrideInput(0,0,0,DriveController.DrivePerspective.RobotRelative);}
            else
            {
                current.stowed=Mathf.Abs(robot.intakeRail.transform.localPosition.z-intakeClosed)<.035f&&Mathf.Abs(robot.hopperRail.transform.localPosition.z-hopperClosed)<.035f;
                current.retained=fuel.Count(piece=>piece.collected&&!piece.escaped&&InsideClosed(robot.transform.InverseTransformPoint(piece.body.position)));
                Debug.Log("TURBO_RETENTION_CASE "+JsonUtility.ToJson(current));
                Press();Cleanup();caseIndex++;
                if(caseIndex<Headings.Length)Spawn();else Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    private static bool InsideClosed(Vector3 p)=>Mathf.Abs(p.x)<.36f&&p.y>.17f&&p.y<.55f&&p.z>.03f&&p.z<.42f;
    private static void Finish()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Press();
        report.passed=report.cases.Count==Headings.Length&&report.cases.All(c=>c.collected>=7&&c.retained==c.collected&&c.escapes.Count==0&&c.stowed&&c.stable)&&report.errors.Count==0;
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/retention-"+label+".json",JsonUtility.ToJson(report,true));
        SessionState.SetInt("Turbo.RetentionExit",report.passed?0:1);EditorApplication.ExitPlaymode();
    }
}
