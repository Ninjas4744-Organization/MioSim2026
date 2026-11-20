using System;
using System.Collections.Generic;
using System.Globalization;
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
using MoSimLib;
using Ninjas4744.MoSim;
using RobotFramework;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class Turbo4744SystemsBench
{
    [Serializable] private sealed class Result
    {
        public string kind;
        public float range,wheelRatio,peakSpeed,forwardSpeed,reverseSpeed,strafeSpeed,maxTilt,minPump=1,maxPump;
        public int payload,payloadAtStart,payloadAfterDrive,launched,scored,netSolvers,hardPumpCycles,jamRecoveries;
        public float minNetY,maxNetY,netSpan,closedNetSpan,openedNetSpan,backoffSeconds;
        public int backoffSamples;
        public bool stable=true,stowed,netScaleOne;
        public Vector3 target;
        public List<string> samples=new List<string>();
        public List<string> shots=new List<string>();
        public List<string> wheelContacts=new List<string>();
    }
    [Serializable] private sealed class Report
    {
        public bool passed;
        public string label;
        public string prefabSource,bundleSha256,runtimeDllSha256;
        public float physicsStep,simulationSpeed;
        public string teamSourceCommit="2bd34881f2c39f86a74cc7d3ea18ba6e00678493";
        public List<Result> cases=new List<Result>();
        public List<string> errors=new List<string>();
    }
    private sealed class Ball
    {
        public Rigidbody body;
        public Vector3 previous;
        public bool launched,crossed,scored;
        public float began;
    }
    private static readonly string Out=Path.GetFullPath(Application.dataPath+"/../BuildArtifacts");
    private static readonly List<Ball> balls=new List<Ball>();
    private static readonly List<GameObject> jamFixture=new List<GameObject>();
    private static float backoffBegan=-1;
    private static Report report;
    private static Result result;
    private static Turbo4744 robot;
    private static AssetBundle bundle;
    private static GameObject prefab;
    private static Rigidbody body;
    private static Turbo4744BenchDriver input;
    private static Keyboard keyboard;
    private static BaseTimerManager timer;
    private static int index,sample;
    private static float began;
    private static double wall;
    private static bool loaded,capturedClosed,capturedOpen,capturedLoaded;
    private static float intakeClosed,hopperClosed;
    private static Vector3 hub;
    private static readonly float[] Ranges={2.25f,3f,4f,4.5f,5f};
    private static string[] Args=>Environment.GetCommandLineArgs();
    private static bool Flag(string name)=>Args.Contains(name);
    private static float Arg(string name,float fallback){int i=Array.IndexOf(Args,name);return i>=0?float.Parse(Args[i+1],CultureInfo.InvariantCulture):fallback;}
    private static float[] Ratios=>Flag("-turboRatios")?Args[Array.IndexOf(Args,"-turboRatios")+1].Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray():null;
    static Turbo4744SystemsBench(){EditorApplication.playModeStateChanged+=Mode;}
    public static void Run()
    {
        SessionState.SetString("Turbo.SystemsCompany",PlayerSettings.companyName);
        SessionState.SetString("Turbo.SystemsProduct",PlayerSettings.productName);
        PlayerSettings.companyName="Ninjas4744ModTests";PlayerSettings.productName="TurboSystemsSandbox";
        SessionState.SetBool("Turbo.SystemsPending",true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame=Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }
    public static void AssembleAndRun(){Ninjas4744Builder.Assemble();Run();}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if(!SessionState.GetBool("Turbo.SystemsPending",false))return;
        InputSystem.settings=Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("TurboSystemsKeyboard");
    }
    private static void Mode(PlayModeStateChange state)
    {
        if(!SessionState.GetBool("Turbo.SystemsPending",false))return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            report=new Report{label=Flag("-turboJam")?"jam":Flag("-turboBefore")?"before":Ratios!=null?"calibration":"after"};
            Time.timeScale=Flag("-turboFast")?3f:1f;
            report.physicsStep=Time.fixedDeltaTime;report.simulationSpeed=Time.timeScale;
            prefab=null;bundle=null;
            index=Flag("-turboShotsOnly")||Flag("-turboJam")?2:0;robot=null;wall=EditorApplication.timeSinceStartup;
            EditorApplication.update+=Tick;Application.logMessageReceived+=Log;
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName=SessionState.GetString("Turbo.SystemsCompany","CascadeStudios");
            PlayerSettings.productName=SessionState.GetString("Turbo.SystemsProduct","MoSim");
            SessionState.SetBool("Turbo.SystemsPending",false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.SystemsExit",1));
        }
    }
    private static void Log(string text,string stack,LogType type)
    {
        if(robot&&(type==LogType.Error||type==LogType.Exception)&&report.errors.Count<20)report.errors.Add(text+"\n"+stack);
    }
    private static void Press(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();}
    private static void AddBall(GameObject go)
    {
        go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform,true);
        var rb=go.GetComponent<Rigidbody>();balls.Add(new Ball{body=rb,previous=rb.position});
    }
    private static void Spawn()
    {
        var position=new Vector3(0,.04f,-2.7f);var rotation=Quaternion.identity;
        result=new Result{kind=Flag("-turboJam")?"jam":index<2?"drive":"shoot",payload=index==1?20:0};
        if(index>=2)
        {
            result.range=Arg("-turboRange",Flag("-turboBefore")?3f:Ranges[index-2]);
            // All firing positions use the clear alliance side of the hub.
            // Neutral-side fixtures hit the physical back panel before the
            // scoring opening and do not measure release calibration.
            Vector3 away=new Vector3(.8f,0,.6f);
            position=hub+away*result.range;position.y=.04f;
            Vector3 towards=hub-position;towards.y=0;
            rotation=Quaternion.Euler(0,Mathf.Atan2(towards.x,towards.z)*Mathf.Rad2Deg+210,0);
        }
        robot=Object.Instantiate(prefab,position,rotation,Object.FindFirstObjectByType<RobotSpawnController>().transform).GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);body=robot.GetComponent<Rigidbody>();
        input=robot.gameObject.AddComponent<Turbo4744BenchDriver>();input.drive=robot.GetComponent<DriveController>();
        intakeClosed=robot.intakeRail.transform.localPosition.z;hopperClosed=robot.hopperRail.transform.localPosition.z;
        robot.shooterMotorToWheelRatio=Arg("-turboWheelRatio",robot.shooterMotorToWheelRatio);
        if(Ratios!=null)robot.shooterMotorToWheelRatio=Ratios[index-2];
        result.wheelRatio=robot.shooterMotorToWheelRatio;
        var fuel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
        var manager=Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
        var removable=Enumerable.Range(0,8).Select(i=>Object.Instantiate(fuel,new Vector3(7,.075f,3),Quaternion.identity)).ToArray();
        typeof(RebuiltFuelPreloadManager).GetField("removableFuel",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(manager,removable);
        Object.FindFirstObjectByType<RebuiltSpawnInitializer>().HandleSpawnedRobots(new[]{robot});
        foreach(var other in Object.FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None))
            if(!robot.preloads.Contains(other.gameObject))other.gameObject.SetActive(false);
        balls.Clear();
        foreach(var preload in robot.preloads){if(index<2)Object.Destroy(preload);else AddBall(preload);}
        jamFixture.Clear();backoffBegan=-1;
        if(Flag("-turboJam"))
        {
            foreach(var ball in balls)Object.Destroy(ball.body.gameObject);balls.Clear();
            var center=new Vector3(0,.28f,.22f);
            AddBall(Object.Instantiate(fuel,robot.transform.TransformPoint(center),Quaternion.identity));
            for(int axis=0;axis<3;axis++)foreach(int side in new[]{-1,1})
            {
                var wall=new GameObject("Benchmark jam obstruction");wall.transform.SetParent(robot.transform,false);
                var offset=Vector3.zero;offset[axis]=side*.08f;wall.transform.localPosition=center+offset;
                var box=wall.AddComponent<BoxCollider>();var size=Vector3.one*.165f;size[axis]=.005f;box.size=size;
                box.excludeLayers=~LayerMask.GetMask("GamePiece1","GamePiece2","GamePieceAlt");jamFixture.Add(wall);
            }
        }
        began=Time.time;loaded=capturedClosed=capturedOpen=capturedLoaded=false;sample=-1;report.cases.Add(result);
        result.netSolvers=robot.GetComponentsInChildren<NetSolver>().Length;
        result.netScaleOne=robot.roofNet.localScale==Vector3.one;
    }
    private static void Measure(float t)
    {
        result.netScaleOne&=robot.roofNet.localScale==Vector3.one;
        var solver=robot.GetComponentInChildren<NetSolver>();
        if(solver)
        {
            var vertices=solver.GetRuntimePositions();
            if(vertices.Length>0)
            {
                float minY=float.PositiveInfinity,maxY=float.NegativeInfinity,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=robot.transform.InverseTransformPoint((Vector3)vertices[i]);
                    if(!float.IsFinite(p.y))result.stable=false;
                    minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);minZ=Mathf.Min(minZ,p.z);maxZ=Mathf.Max(maxZ,p.z);
                }
                result.minNetY=minY;result.maxNetY=maxY;result.netSpan=maxZ-minZ;
                result.stable&=minY>-.05f&&maxY<.9f;
            }
        }
        var cycles=typeof(Turbo4744).GetProperty("CompletedHardPumpCycles");
        if(cycles!=null)result.hardPumpCycles=(int)cycles.GetValue(robot);
        result.jamRecoveries=robot.JamRecoveries;
        bool backing=robot.ShootingState==Turbo4744.ShootState.SaveOuttakeBack;
        if(backing&&backoffBegan<0)backoffBegan=t;
        if(backing&&robot.TargetIndexerMotorRps==-20&&robot.TargetAcceleratorMotorRps==0)result.backoffSamples++;
        if(!backing&&backoffBegan>=0){result.backoffSeconds=t-backoffBegan;backoffBegan=-1;}
        result.peakSpeed=Mathf.Max(result.peakSpeed,new Vector2(body.velocity.x,body.velocity.z).magnitude);
        float tilt=Quaternion.Angle(body.rotation,Quaternion.Euler(0,body.rotation.eulerAngles.y,0));
        result.maxTilt=Mathf.Max(result.maxTilt,tilt);
        result.stable&=float.IsFinite(body.position.y)&&Mathf.Abs(body.position.y)<.2f&&tilt<15;
        if(index<2)
        {
            if(t>3.8f&&t<4.4f)result.forwardSpeed=Mathf.Max(result.forwardSpeed,body.velocity.z);
            if(t>5.6f&&t<6.2f)result.reverseSpeed=Mathf.Max(result.reverseSpeed,-body.velocity.z);
            if(t>7.3f&&t<7.8f)result.strafeSpeed=Mathf.Max(result.strafeSpeed,body.velocity.x);
        }
        if(t>6.1f&&t<11.5f)
        {
            float extension=robot.transform.InverseTransformPoint(robot.intakeRail.GetComponent<Rigidbody>().position).z-intakeClosed;
            result.minPump=Mathf.Min(result.minPump,extension);result.maxPump=Mathf.Max(result.maxPump,extension);
        }
        foreach(var ball in balls.Where(b=>b.body))
        {
            var p=ball.body.position;var local=robot.transform.InverseTransformPoint(p);
            if(!ball.launched&&local.y>.65f&&local.z<-.2f)
            {
                ball.launched=true;ball.began=t;result.launched++;
                result.shots.Add("Exit "+balls.IndexOf(ball)+" t "+t.ToString("F3")+" p "+p.ToString("F3")+" v "+ball.body.velocity.ToString("F3")+" goalRPM "+robot.TargetShooterRpm.ToString("F1"));
            }
            var controller=ball.body.GetComponent<RebuiltGamePieceController>();
            // scored is a presence flag and can clear between editor samples.
            // The native scorer's successful claim timestamp persists.
            float scoredAt=(float)typeof(RebuiltGamePieceController).GetField("_lastScoredTime",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            if(scoredAt>=began&&!ball.scored){ball.scored=true;result.scored++;}
            if(ball.launched&&!ball.crossed&&ball.previous.y>=hub.y&&p.y<hub.y&&ball.body.velocity.y<0)
            {
                float f=(hub.y-ball.previous.y)/(p.y-ball.previous.y);
                Vector3 hit=Vector3.Lerp(ball.previous,p,f);Vector3 miss=hit-hub;miss.y=0;
                result.shots.Add("Landing "+balls.IndexOf(ball)+" error "+miss.magnitude.ToString("F3")+" point "+hit.ToString("F3")+" flight "+(t-ball.began).ToString("F3"));
                ball.crossed=true;
            }
            ball.previous=p;
        }
        int s=(int)(t*4);if(s==sample)return;sample=s;
        result.samples.Add(t.ToString("F2")+" p "+body.position.ToString("F3")+" v "+body.velocity.ToString("F3")+" yaw "+body.rotation.eulerAngles.y.ToString("F1")+" aim "+robot.autoAim.GetRotationError().ToString("F2")+" rpm "+robot.CurrentShooterRpm.ToString("F1")+" feed "+robot.Feeding+" rails "+robot.intakeRail.transform.localPosition.z.ToString("F4")+","+robot.hopperRail.transform.localPosition.z.ToString("F4"));
        if(s%4==0)
            foreach(var wheel in robot.GetComponentsInChildren<WheelCollider>())
                result.wheelContacts.Add(t.ToString("F2")+" "+wheel.name+" "+(wheel.GetGroundHit(out var hit)?hit.collider.name+" force "+hit.force.ToString("F1"):"none"));
    }
    private static void Cleanup()
    {
        foreach(var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))outpost.Robots.Remove(robot);
        foreach(var ball in balls)if(ball.body){ball.body.gameObject.SetActive(false);Object.Destroy(ball.body.gameObject);}
        // Destroy is deferred. Disable the completed fixture before spawning
        // the next nearby robot so they cannot collide for one physics step.
        robot.gameObject.SetActive(false);Object.Destroy(robot.gameObject);robot=null;
    }
    private static int Retained()=>balls.Count(b=>b.body&&Mathf.Abs(robot.transform.InverseTransformPoint(b.body.position).x)<.36f&&
        robot.transform.InverseTransformPoint(b.body.position).y>.17f&&robot.transform.InverseTransformPoint(b.body.position).y<.7f&&
        robot.transform.InverseTransformPoint(b.body.position).z>0&&robot.transform.InverseTransformPoint(b.body.position).z<.65f);
    private static void Capture(string name)
    {
        var camera=new GameObject("Bench preview").AddComponent<Camera>();
        camera.transform.position=robot.transform.TransformPoint(name.Contains("top")?new Vector3(.05f,1.6f,.35f):new Vector3(1.3f,.8f,1.25f));
        camera.transform.LookAt(robot.transform.TransformPoint(new Vector3(0,.26f,.1f)));
        camera.orthographic=true;camera.orthographicSize=.6f;camera.nearClipPlane=.01f;
        camera.cullingMask=LayerMask.GetMask("Robot","GamePiece1","GamePiece2","GamePieceAlt");
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.05f,.07f,0);
        var rt=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;camera.Render();
        RenderTexture.active=rt;var tex=new Texture2D(960,720,TextureFormat.RGBA32,false);
        tex.ReadPixels(new Rect(0,0,960,720),0,0);tex.Apply();Directory.CreateDirectory(Out);
        File.WriteAllBytes(Out+"/Turbo4744-v4-"+name+".png",tex.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;Object.Destroy(rt);Object.Destroy(tex);Object.Destroy(camera.gameObject);
    }
    private static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-wall>600)throw new TimeoutException("Systems benchmark exceeded ten minutes");
            if(!robot)
            {
                if(EditorApplication.timeSinceStartup-wall<5||!BaseGameManager.Instance)return;
                timer=BaseGameManager.Instance.GetComponent<BaseTimerManager>();timer.PauseTimer();
                typeof(BaseTimerManager).GetProperty("CurrentRobotState").SetValue(timer,RobotState.Enabled);
                foreach(var camera in Camera.allCameras)camera.enabled=false;
                foreach(var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))old.gameObject.SetActive(false);
                if(!prefab)
                {
                    if(Flag("-turboBundle"))
                    {
                        string file=Path.GetFullPath(Application.dataPath+"/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets");
                        bundle=AssetBundle.LoadFromFile(file);
                        report.prefabSource=file;report.bundleSha256=Digest(file);
                        report.runtimeDllSha256=Digest(Path.Combine(Path.GetDirectoryName(file),"Ninjas4744.dll"));
                        if(report.runtimeDllSha256!=Digest(typeof(Turbo4744).Assembly.Location))throw new InvalidOperationException("The built DLL differs from the runtime under test");
                        prefab=bundle.LoadAllAssets<BaseModpackSO>().Single(p=>p.ModpackName=="Ninjas 4744 Turbo 2026").Robots.Single().RobotPrefab;
                    }
                    else{prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Ninjas4744Builder.RobotPath);report.prefabSource=Ninjas4744Builder.RobotPath;}
                }
                hub=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.gameObject.layer==LayerMask.NameToLayer("AutoAngleNodes")&&t.name.ToLower().Contains("blue")&&t.name.ToLower().Contains("hub")).position;
                Debug.Log("TURBO_HUB_TARGET "+hub.ToString("F4"));
                Spawn();return;
            }
            float t=Time.time-began;Measure(t);input.vx=input.vy=input.omega=0;
            if(index<2)
            {
                if(index==0&&t<.35f)Press();else if(t<1.8f)Press(Key.LeftShift);
                else if(!loaded)
                {
                    loaded=true;Press();
                    if(index==1)
                    {
                        var fuel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
                        foreach(float y in new[]{.27f,.425f})foreach(float z in new[]{.225f,.375f,.525f})
                            foreach(float x in z<.3f?new[]{-.075f,.075f}:new[]{-.225f,-.075f,.075f,.225f})
                                AddBall(Object.Instantiate(fuel,robot.transform.TransformPoint(new Vector3(x,y,z)),Quaternion.identity));
                    }
                }
                if(index==0&&t>.3f&&!capturedClosed){result.closedNetSpan=result.netSpan;Capture("closed");Capture("closed-top");capturedClosed=true;}
                if(index==0&&t>2.4f&&!capturedOpen){result.openedNetSpan=result.netSpan;Capture("opened");Capture("opened-top");capturedOpen=true;}
                if(index==1&&t>3.2f&&!capturedLoaded){result.payloadAtStart=Retained();Capture("loaded");Capture("loaded-top");capturedLoaded=true;}
                if(index==1&&t>9.2f&&t<10.2f)result.payloadAfterDrive=Retained();
                if(t>=3.3f&&t<4.4f)input.vx=4.5f;
                else if(t>=5.1f&&t<6.2f)input.vx=-4.5f;
                else if(t>=6.8f&&t<7.8f)input.vy=3f;
                else if(t>=8.2f&&t<9.2f)input.omega=120;
                else if(t>=10.2f)Press(Key.Z);
            }
            else
            {
                if(Flag("-turboJam")&&t>7)foreach(var obstacle in jamFixture)if(obstacle)Object.Destroy(obstacle);
                if(t<1)Press();else if(t<2.2f)Press(Key.LeftShift);else if(t<3)Press();
                else if(t>=6&&t<6.08f)Press(Key.K,Key.V);else Press(Key.K);
                result.target=robot.autoAim.GetCurrentTarget();
            }
            if(t<(index<2?12.5f:16f))return;
            if(index>=2)foreach(var ball in balls.Where(b=>b.body&&!b.launched))
                result.shots.Add("Remaining "+balls.IndexOf(ball)+" local "+robot.transform.InverseTransformPoint(ball.body.position).ToString("F3"));
            result.stowed=Mathf.Abs(robot.intakeRail.transform.localPosition.z-intakeClosed)<.035f&&Mathf.Abs(robot.hopperRail.transform.localPosition.z-hopperClosed)<.035f;
            Debug.Log("TURBO_SYSTEMS_CASE "+JsonUtility.ToJson(result));Press();Cleanup();index++;
            if(Flag("-turboJam")?true:Ratios!=null?index>=2+Ratios.Length:Flag("-turboDriveOnly")?index>=2:Flag("-turboBefore")||Flag("-turboRange")?index>=3:index>=7)Finish();else Spawn();
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    private static void Finish()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Press();
        report.passed=report.errors.Count==0&&report.cases.All(c=>c.stable&&c.netSolvers==1&&c.netScaleOne&&
            (c.kind=="drive"?c.forwardSpeed>3.3f&&c.reverseSpeed>3.3f&&c.strafeSpeed>2.3f&&
                (c.payload==0?c.closedNetSpan>.36f&&c.closedNetSpan<.41f&&c.openedNetSpan>.59f:c.payloadAtStart==c.payload&&c.payloadAfterDrive==c.payload)
                :c.kind=="jam"?c.jamRecoveries>0&&c.backoffSamples>0&&c.backoffSeconds>.45f&&c.backoffSeconds<.6f&&c.scored==1
                :c.scored>=7&&c.hardPumpCycles==3&&c.maxPump-c.minPump>.045f&&c.minPump>.145f&&c.maxPump<.245f));
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/systems-"+report.label+".json",JsonUtility.ToJson(report,true));
        SessionState.SetInt("Turbo.SystemsExit",Flag("-turboStrict")&&!report.passed?1:0);EditorApplication.ExitPlaymode();
    }
    private static string Digest(string file)
    {using(var stream=File.OpenRead(file))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
}
[DefaultExecutionOrder(-1000)]
public sealed class Turbo4744BenchDriver:MonoBehaviour
{
    public DriveController drive;
    public float vx,vy,omega;
    private void FixedUpdate(){if(drive&&(vx!=0||vy!=0||omega!=0))drive.OverrideInput(vx,vy,omega,DriveController.DrivePerspective.RobotRelative);}
}
