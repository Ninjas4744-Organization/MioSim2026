using System;
using System.Linq;
using Ninjas4744.MoSim;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Optional visible Game view for the same physics fixtures used in batch runs.</summary>
[InitializeOnLoad]
public static class Turbo4744LiveTestView
{
    private static Camera camera;

    static Turbo4744LiveTestView() => EditorApplication.update += Update;

    private static void Update()
    {
        if (
            !Environment.GetCommandLineArgs().Contains("-turboVisible")
            || !EditorApplication.isPlaying
        )
            return;
        // Collection fixtures already provide their own following camera and caption.
        if (
            Object.FindFirstObjectByType<Turbo4744CapacityView>()
            || Object.FindFirstObjectByType<Turbo4744RetentionView>()
        )
        {
            if (camera)
                Object.Destroy(camera.gameObject);
            return;
        }
        if (camera)
        {
            camera.enabled = true;
            return;
        }
        var robot = Object
            .FindObjectsByType<Turbo4744>(FindObjectsSortMode.None)
            .FirstOrDefault(r => r.isActiveAndEnabled);
        if (!robot)
            return;
        var go = new GameObject("Visible Turbo test camera");
        go.transform.SetParent(robot.transform, false);
        camera = go.AddComponent<Camera>();
        camera.depth = 1000;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 100;
        camera.orthographic = true;
        var rig = go.AddComponent<Turbo4744LiveCamera>();
        rig.robot = robot;
        rig.camera = camera;
        rig.indexer = Environment.GetCommandLineArgs().Contains("-turboIndexer");
        if (rig.indexer)
        {
            foreach (var renderer in robot.GetComponentsInChildren<Renderer>(true))
                renderer.enabled =
                    renderer.name.StartsWith("Indexer") || renderer.name.StartsWith("SideIndexer");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(.15f, .65f, .8f);
            foreach (
                var renderer in robot
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name.StartsWith("SideIndexer"))
            )
                renderer.sharedMaterial = material;
            rig.supportMaterial = material;
            rig.cutaway = Turbo4744SystemsBench.CreateShooterCutaway(robot);
            camera.cullingMask = LayerMask.GetMask(
                "Robot",
                "GamePiece1",
                "GamePiece2",
                "GamePieceAlt"
            );
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .045f, .06f);
        }
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
        if (type != null)
        {
            var game = EditorWindow.GetWindow(type);
            game.Show();
            game.Focus();
        }
    }
}

public sealed class Turbo4744LiveCamera : MonoBehaviour
{
    public Turbo4744 robot;
    public new Camera camera;
    public bool indexer;
    public GameObject cutaway;
    public Material supportMaterial;

    private void LateUpdate()
    {
        if (!robot)
            return;
        Vector3 target;
        if (indexer)
        {
            target = robot.transform.TransformPoint(new Vector3(0, .31f, .02f));
            transform.position = robot.transform.TransformPoint(new Vector3(.95f, .75f, .85f));
            camera.orthographicSize = .53f;
        }
        else if (robot.TargetShooterRpm > 0 && robot.AimTarget != Vector3.zero)
        {
            target = (robot.transform.position + robot.AimTarget) * .5f;
            target.y = .8f;
            transform.position = target + robot.transform.right * 4 + Vector3.up * 3;
            camera.orthographicSize = Mathf.Max(1.8f, robot.ShotDistance * .65f);
        }
        else
        {
            target = robot.transform.TransformPoint(new Vector3(0, .3f, .2f));
            transform.position = robot.transform.TransformPoint(new Vector3(2, 1.8f, 2));
            camera.orthographicSize = 1.3f;
        }
        transform.LookAt(target, Vector3.up);
    }

    private void OnGUI()
    {
        if (!robot)
            return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
        string label = indexer
            ? "Indexer: " + Turbo4744SystemsBench.LiveCase
            : "Turbo robot regression";
        GUI.Box(
            new Rect(12, 12, 650, 76),
            label
                + "\n"
                + robot.ShootingState
                + "  |  motor "
                + robot.TargetIndexerMotorRps.ToString("F0")
                + " RPS"
                + "  |  floor contacts "
                + robot.indexer.LowerContacts
                + "/"
                + robot.indexer.UpperContacts
                + "  |  shooter contacts "
                + robot.shooterContact.Contacts,
            style
        );
    }

    private void OnDestroy()
    {
        if (supportMaterial)
            Destroy(supportMaterial);
        if (cutaway)
        {
            Destroy(cutaway.GetComponent<MeshFilter>().sharedMesh);
            Destroy(cutaway);
        }
    }
}
