using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.Components;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.GamePieceSystem;
using Games.Rebuilt.Robots;
using GameSystems.Management;
using GameSystems.Modding.Authoring.GameSystems.Modding.Editor;
using MoSimLib;
using Ninjas4744.MoSim;
using RobotFramework;
using RobotFramework.Components;
using RobotFramework.Controllers.Drivetrain;
using RobotFramework.Motors;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class Ninjas4744Builder
{
    public const string Root = "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744";
    public const string PackPath = Root + "/Ninjas4744.asset";
    public const string RobotPath = Root + "/Turbo4744.prefab";
    private const float CadCenterX = .0223825f;
    private static readonly string Artifacts = Path.GetFullPath(
        Application.dataPath + "/../BuildArtifacts"
    );

    [Serializable]
    private class MaterialList
    {
        public MaterialRecord[] materials;
    }

    [Serializable]
    private class MaterialRecord
    {
        public string name,
            source_name,
            texture;
        public float[] base_color;
        public float roughness,
            metallic;
        public bool transparent,
            decal,
            bumper;
    }

    [Serializable]
    private class BundleReport
    {
        public bool passed;
        public string modpack,
            game;
        public int team,
            robots,
            missingScripts,
            renderers,
            materials,
            preloads,
            tutorialCards;
        public string[] assemblyReferences;
    }

    [MenuItem("Tools/Modding/Ninjas 4744/Assemble Turbo")]
    public static void Assemble()
    {
        Directory.CreateDirectory(Artifacts);
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/TutorialCards");
        AssetDatabase.Refresh();
        var mats = BuildMaterials();
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/RobotTemplates/BaseRobot.prefab"
        );
        if (!template)
            throw new InvalidOperationException("MoSim BaseRobot template is missing");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(template);
        PrefabUtility.UnpackPrefabInstance(
            root,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction
        );
        root.name = "4744 Turbo";
        root.SetActive(false);
        foreach (var old in root.GetComponents<RobotBase>())
            Object.DestroyImmediate(old);
        foreach (var child in root.transform.Cast<Transform>().ToArray())
            if (child.name.ToLowerInvariant().Contains("drive"))
                Object.DestroyImmediate(child.gameObject);
        var robot = root.AddComponent<Turbo4744>();
        var body = root.GetComponent<Rigidbody>();
        body.mass = 54f; // +5.5 kg intake and +2.5 kg hopper = team-configured 62 kg.
        body.drag = .25f;
        body.angularDrag = .4f;
        body.centerOfMass = new Vector3(0, .14f, 0);
        body.inertiaTensor = new Vector3(4.3f, 7.411f, 4.7f);
        body.solverIterations = 24;
        body.solverVelocityIterations = 12;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var bumper = root.GetComponent<BoxCollider>() ?? root.AddComponent<BoxCollider>();
        bumper.center = new Vector3(0, .09578f, 0);
        bumper.size = new Vector3(.8884f, .127f, .79548f);
        bumper.gameObject.tag = "BlueRobot";

        var model = LoadModel(root.transform, mats);
        var intakeVisual = Find(model.transform, "Intake");
        var intakeRollerVisual = Find(model.transform, "IntakeRoller");
        robot.intakeRollerVisual = intakeRollerVisual;
        var hopperVisual = Find(model.transform, "Hopper");
        var netVisual = Find(model.transform, "RoofNet");
        robot.intakeRail = Rail(
            root.transform,
            body,
            "Intake rail",
            S(.106f, 0, .3535f),
            5.5f,
            new[] { intakeVisual, intakeRollerVisual },
            .24f
        );
        robot.hopperRail = Rail(
            root.transform,
            body,
            "Sliding hopper",
            S(0, 0, 0),
            2.5f,
            new[] { hopperVisual },
            .22946f
        );
        foreach (var renderer in netVisual.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        ConfigureNet(root, robot);
        robot.roofCollider = Box(
            root.transform,
            "Legacy roof proxy (disabled)",
            S(.332f, 0, .533f),
            new Vector3(.71f, .006f, .612f)
        );
        robot.roofCollider.enabled = false;
        var trayPlastic = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(
            Root + "/Materials/FeedPlastic.physicMaterial"
        );
        if (!trayPlastic)
        {
            trayPlastic = new PhysicMaterial("Feed plastic");
            AssetDatabase.CreateAsset(trayPlastic, Root + "/Materials/FeedPlastic.physicMaterial");
        }
        trayPlastic.staticFriction = trayPlastic.dynamicFriction = .03f;
        trayPlastic.bounciness = 0;
        trayPlastic.frictionCombine = PhysicMaterialCombine.Minimum;
        trayPlastic.bounceCombine = PhysicMaterialCombine.Minimum;

        // Proxy surfaces follow the visible CAD; roller contacts are separately
        // calibrated and do not use expensive high-poly CAD mesh colliders.
        Box(
            root.transform,
            "Left shooter side",
            S(.02f, .365f, .32f),
            new Vector3(.008f, .43f, .31f)
        );
        Box(
            root.transform,
            "Right shooter side",
            S(.02f, -.365f, .32f),
            new Vector3(.008f, .43f, .31f)
        );
        Box(
            root.transform,
            "Hopper floor",
            S(.23f, 0, .175f),
            new Vector3(.68f, .012f, .38f)
        ).sharedMaterial = trayPlastic;
        // One-way intake contacts allow inbound fuel to rise through the entry,
        // then provide a complete, continuous floor beneath stored fuel.
        var storageFloor = AddRailBox(
            robot.hopperRail.transform,
            "Hopper moving storage floor",
            S(.34f, 0, .175f),
            new Vector3(.68f, .012f, .62f),
            .22946f
        );
        storageFloor.sharedMaterial = trayPlastic;
        AddRailBox(
            robot.hopperRail.transform,
            "Hopper left wall",
            S(.40f, .359f, .37f),
            new Vector3(.006f, .32f, .5f),
            .22946f
        );
        AddRailBox(
            robot.hopperRail.transform,
            "Hopper right wall",
            S(.40f, -.359f, .37f),
            new Vector3(.006f, .32f, .5f),
            .22946f
        );
        AddRailBox(
            robot.hopperRail.transform,
            "Hopper end wall",
            S(.648f, 0, .37f),
            new Vector3(.714f, .32f, .006f),
            .22946f
        ).sharedMaterial = trayPlastic;
        robot.intakeHold = AddRailBox(
            robot.hopperRail.transform,
            "Intake inlet retention contact",
            S(.648f, 0, .14f),
            new Vector3(.714f, .16f, .02f),
            .22946f
        );
        robot.intakeHold.excludeLayers = ~LayerMask.GetMask(
            "GamePiece1",
            "GamePiece2",
            "GamePieceAlt"
        );
        var keeper = root.AddComponent<Turbo4744FuelKeeper>();
        keeper.robot = robot;
        keeper.floor = storageFloor;
        keeper.inlet = robot.intakeHold;
        AddRailBox(
            robot.intakeRail.transform,
            "Intake left guide",
            S(.40f, .342f, .16f),
            new Vector3(.008f, .18f, .4f),
            .24f
        );
        AddRailBox(
            robot.intakeRail.transform,
            "Intake right guide",
            S(.40f, -.342f, .16f),
            new Vector3(.008f, .18f, .4f),
            .24f
        );

        robot.intakeRollers = new[]
        {
            Roller(
                robot.intakeRail.transform,
                "Intake pickup",
                S(.52268f, -.0035f, .07545f) - Vector3.forward * .24f,
                .035f,
                .56f,
                0f,
                false,
                0f
            ),
            // A cylindrical leading contact completes the belt's curved entry;
            // belt mode alone creates a flat core and leaves its nose unsupported.
            Roller(
                robot.intakeRail.transform,
                "Intake belt nose",
                new Vector3(0, .055f, .54f) - Vector3.forward * .24f,
                .035f,
                .52f,
                0f,
                false,
                0f
            ),
            Roller(
                robot.intakeRail.transform,
                "Intake transfer",
                S(.447f, 0, .14f) - Vector3.forward * .24f,
                .05f,
                .52f,
                29f,
                true,
                .34f
            ),
        };
        foreach (var roller in robot.intakeRollers)
            Set(roller, "ignoreLayers", (LayerMask)~0);
        var transfer = New("Intake transfer traction", robot.intakeRail.transform, Vector3.zero);
        transfer.transform.position = root.transform.TransformPoint(
            S(.447f, 0, .185f)
                + Quaternion.Euler(29f, 0, 0) * Vector3.up * .10f
                - Vector3.forward * robot.intakeTravel
        );
        transfer.transform.localRotation = Quaternion.Euler(29f, 0, 0);
        robot.intakeContact = transfer.AddComponent<Turbo4744ShooterContact>();
        robot.intakeContact.robotBody = robot.intakeRail.GetComponent<Rigidbody>();
        robot.intakeContact.wheelRadius = .025f;
        // The contact fades over one fuel radius at its edge. Cover the full
        // tray width so a sphere against the side guide still gets support.
        robot.intakeContact.contactNear = -.26f;
        robot.intakeContact.contactFar = .16f;
        robot.intakeContact.halfWidth = .34f;
        robot.intakeContact.normalClearance = .11f;
        robot.intakeContact.maxTractionForce = 35;
        robot.intakeContact.tractionResponse = 40;
        robot.intakeContact.maxTotalForce = 80;
        robot.intakeContact.normalStiffness = 450;
        robot.intakeContact.normalDamping = 28;
        robot.intakeContact.maxNormalForce = 35;
        robot.intakeContact.compensateNormalGravity = true;
        robot.intakeContact.allowReverse = true;
        robot.intakeContact.requiresCommand = true;
        robot.intakeContact.entryKeeper = keeper;
        var transport = new List<InteractionRoller>();
        // The transport acts on captured fuel above the tray. Cylindrical
        // cores along the entry would pin incoming ground fuel underneath.
        var bed = New("Stored fuel conveyor contact", root.transform, new Vector3(0, .26f, .34f));
        robot.storageContact = bed.AddComponent<Turbo4744ShooterContact>();
        robot.storageContact.robotBody = body;
        robot.storageContact.wheelRadius = .035f;
        robot.storageContact.contactNear = -.28f;
        robot.storageContact.contactFar = .26f;
        robot.storageContact.halfWidth = .33f;
        robot.storageContact.normalClearance = .09f;
        robot.storageContact.maxTractionForce = 12;
        robot.storageContact.tractionResponse = 40;
        robot.storageContact.maxTotalForce = 35;
        robot.storageContact.maxNormalForce = 0;
        robot.storageContact.lateralResponse = 5;
        robot.storageContact.maxLateralForce = 4;
        robot.storageContact.allowReverse = true;
        robot.storageContact.requiresCommand = true;
        robot.storageContact.entryKeeper = keeper;
        robot.storageContact.capturedOnly = true;
        // A continuous rising surface avoids balls bouncing between isolated
        // stair-step contacts on their way from storage to the accelerator.
        foreach (float offset in new[] { -.10f, 0f, .10f })
        {
            var angle = Quaternion.Euler(32.2f, 0, 0);
            var position = new Vector3(0, .255f, .035f) + angle * Vector3.forward * offset;
            var lower = Roller(
                root.transform,
                "Indexer belt " + offset,
                position,
                .035f,
                .30f,
                32.2f,
                true,
                .16f
            );
            Set(lower, "ignoreLayers", (LayerMask)~0);
            transport.Add(lower);
        }
        var indexer = New(
            "Indexer traction contact",
            root.transform,
            new Vector3(0, .255f, .035f) + Quaternion.Euler(32.2f, 0, 0) * Vector3.up * .09f
        );
        indexer.transform.localRotation = Quaternion.Euler(32.2f, 0, 0);
        robot.indexerContact = indexer.AddComponent<Turbo4744ShooterContact>();
        robot.indexerContact.robotBody = body;
        robot.indexerContact.wheelRadius = .035f;
        robot.indexerContact.contactNear = -.20f;
        robot.indexerContact.contactFar = .17f;
        robot.indexerContact.halfWidth = .22f;
        robot.indexerContact.normalClearance = .065f;
        robot.indexerContact.maxTractionForce = 120;
        robot.indexerContact.tractionResponse = 60;
        robot.indexerContact.maxTotalForce = 140;
        robot.indexerContact.normalStiffness = 800;
        robot.indexerContact.normalDamping = 30;
        robot.indexerContact.maxNormalForce = 80;
        robot.indexerContact.allowReverse = true;
        robot.indexerContact.requiresCommand = true;
        robot.floorRollers = transport.ToArray();
        foreach (int side in new[] { -1, 1 })
        {
            // Reach the hopper rim so the upper layer cannot roll over the
            // guide into the open rear corners beside the narrow shooter.
            var funnel = Box(
                root.transform,
                "Indexer funnel " + side,
                new Vector3(side * .215f, .35f, .165f),
                new Vector3(.008f, .40f, .368f)
            );
            funnel.transform.localRotation = Quaternion.Euler(0, side * 42.8f, 0);
            funnel.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            // Join the funnel at z=.03. A forward-projecting sloped guide
            // otherwise presents a blunt end before fuel has been centered.
            var guide = Box(
                root.transform,
                "Shooter alignment guide " + side,
                new Vector3(side * .09f, .45f, -.17f),
                new Vector3(.008f, .50f, .40f)
            );
            guide.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            var plastic = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(
                Root + "/Materials/FeedPlastic.physicMaterial"
            );
            if (!plastic)
            {
                plastic = new PhysicMaterial("Feed plastic")
                {
                    staticFriction = .03f,
                    dynamicFriction = .03f,
                    bounciness = 0,
                };
                AssetDatabase.CreateAsset(plastic, Root + "/Materials/FeedPlastic.physicMaterial");
            }
            plastic.frictionCombine = PhysicMaterialCombine.Minimum;
            plastic.bounceCombine = PhysicMaterialCombine.Minimum;
            funnel.sharedMaterial = guide.sharedMaterial = plastic;
            // A short hood constrains the release plane. It starts above the
            // indexer approach, so it cannot pinch fuel before the flywheels.
            var axis = Quaternion.Euler(58f, 0, 0);
            var hood = Box(
                root.transform,
                "Shooter hood " + side,
                new Vector3(0, .461f, -.055f) + axis * (new Vector3(0, side * .081f, -.10f)),
                new Vector3(.172f, .005f, .30f)
            );
            hood.transform.localRotation = axis;
            hood.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            hood.sharedMaterial = plastic;
        }
        robot.acceleratorRollers = new[]
        {
            Roller(
                root.transform,
                "Accelerator",
                new Vector3(0, .36f, -.10f),
                .04f,
                .30f,
                0f,
                false,
                0f
            ),
        };
        // The symmetric tire adapter applies finite traction forces within
        // the release channel and the opposite reaction to the chassis.
        var contact = New(
            "Symmetric shooter tire contact",
            root.transform,
            S(-.13f, 0, .40f) + Quaternion.Euler(58f, 0, 0) * Vector3.up * .115f
        );
        contact.transform.localRotation = Quaternion.Euler(58f, 0, 0);
        robot.shooterContact = contact.AddComponent<Turbo4744ShooterContact>();
        robot.shooterContact.robotBody = body;
        robot.shooterRollers = Array.Empty<InteractionRoller>();
        robot.flywheels = Array.Empty<PvffController>();
        robot.feedHold = Box(
            root.transform,
            "Stopped indexer retention contact",
            new Vector3(0, .35f, .015f),
            new Vector3(.70f, .40f, .008f)
        );
        robot.feedHold.excludeLayers = ~LayerMask.GetMask(
            "GamePiece1",
            "GamePiece2",
            "GamePieceAlt"
        );

        var muzzle = New("Shooter reference", root.transform, S(-.215f, 0, .43f)).transform;
        muzzle.localRotation = Quaternion.Euler(0, 180, 0);
        robot.muzzle = muzzle;
        robot.autoAim = root.AddComponent<RebuiltAutoAimController>();
        Set(robot.autoAim, "rotationReference", muzzle);
        Set(robot.autoAim, "aimControl", AutoAim.AimControl.PID);
        Set(robot.autoAim, "K_p", 5.5f);
        Set(robot.autoAim, "PIDMax", 8.5f * Mathf.Rad2Deg);
        Set(robot.autoAim, "maxOmega", 8.5f * Mathf.Rad2Deg);
        Set(robot.autoAim, "maxAlpha", 1500f);
        Set(robot.autoAim, "maxAlphaDot", 10000f);
        Set(robot.autoAim, "rotationApproachTime", .2f);

        ConfigureDrive(root, body);
        ConfigureBumpers(robot, model, mats);
        ConfigurePreloads(robot);
        var tagger = root.AddComponent<FlywheelPenaltyTagger>();
        Set(tagger, "gamePieceLayers", (LayerMask)LayerMask.GetMask("GamePiece1", "GamePiece2"));
        var shotTrigger = root.AddComponent<BoxCollider>();
        shotTrigger.center = S(-.10f, 0, .35f);
        shotTrigger.size = new Vector3(.33f, .31f, .31f);
        shotTrigger.isTrigger = true;
        SetLayer(root, LayerMask.NameToLayer("Robot"));
        root.SetActive(true);
        var gamePrefab = PrefabUtility.SaveAsPrefabAsset(root, RobotPath);
        Object.DestroyImmediate(root);

        var menu = MakeDisplayPrefab(gamePrefab, false);
        var spawn = MakeDisplayPrefab(gamePrefab, true);
        var cards = BuildCards();
        var metadata = AssetDatabase.LoadAssetAtPath<RebuiltRobotMetadataSO>(
            Root + "/Turbo4744.asset"
        ) ?? ScriptableObject.CreateInstance<RebuiltRobotMetadataSO>();
        var main = new RobotMetadataSO.RobotVariant();
        Set(main, "displayName", "Turbo");
        Set(main, "robotPrefab", gamePrefab);
        Set(main, "mainMenuPrefab", menu);
        Set(main, "spawnSelectionPrefab", spawn);
        Set(
            main,
            "preloadGamePiece",
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab")
        );
        Set(main, "tutorialCards", cards.ToList());
        Set(metadata, "mainRobot", main);
        Set(metadata, "teamNumber", 4744);
        Set(metadata, "teamName", "Ninjas");
        Set(metadata, "robotImage", ImportPreview());
        SaveAsset(metadata, Root + "/Turbo4744.asset");
        var pack = AssetDatabase.LoadAssetAtPath<RebuiltModpackSO>(PackPath)
            ?? ScriptableObject.CreateInstance<RebuiltModpackSO>();
        Set(pack, "modpackName", "Ninjas 4744 Turbo 2026");
        Set(pack, "modpackVersion", "0.5.1");
        Set(pack, "authorName", "Ninjas 4744");
        Set(
            pack,
            "description",
            "Turbo for Rebuilt: original team CAD with physics-driven intake, hopper, shooter and passing. Initial simulation calibration."
        );
        Set(pack, "thumbnail", ImportPreview());
        Set(pack, "robots", new List<RobotMetadataSO> { metadata });
        SaveAsset(pack, PackPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Selection.activeObject = pack;
    }

    private static Vector3 S(float x, float y, float z) => new Vector3(-y, z, x - CadCenterX);

    private static Dictionary<string, Material> BuildMaterials()
    {
        var data = JsonUtility.FromJson<MaterialList>(
            File.ReadAllText(Root + "/Model/materials.json")
        );
        var result = new Dictionary<string, Material>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader)
            throw new InvalidOperationException("URP Lit shader is unavailable");
        foreach (var record in data.materials)
        {
            var path = Root + "/Materials/" + record.name + ".mat";
            var selectedShader = record.decal ? Shader.Find("Ninjas4744/WhiteVinyl") : shader;
            if (!selectedShader)
                throw new InvalidOperationException("Sponsor alpha shader is missing");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path) ?? new Material(selectedShader);
            mat.shader = selectedShader;
            mat.name = record.name;
            mat.SetColor(
                "_BaseColor",
                new Color(
                    record.base_color[0],
                    record.base_color[1],
                    record.base_color[2],
                    record.base_color[3]
                )
            );
            mat.SetFloat("_Smoothness", 1f - record.roughness);
            mat.SetFloat("_Metallic", record.metallic);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            if (!string.IsNullOrEmpty(record.texture))
            {
                string texturePath = Root + "/Model/textures/" + record.texture;
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            }
            if (record.transparent)
            {
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = record.decal ? 3010 : 3000;
            }
            SaveAsset(mat, path);
            result.Add(record.name, mat);
        }
        var red = result.First(x => x.Value.name == data.materials.First(m => m.bumper).name).Value;
        var blue = new Material(red);
        blue.SetColor("_BaseColor", new Color(.025f, .12f, .65f));
        SaveAsset(blue, Root + "/Materials/BlueBumper.mat");
        result["BumperRed"] = red;
        result["BumperBlue"] = blue;
        return result;
    }

    private static GameObject LoadModel(Transform parent, Dictionary<string, Material> materials)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Model/Turbo4744.fbx");
        if (!asset)
            throw new InvalidOperationException("Turbo's FBX is missing");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        PrefabUtility.UnpackPrefabInstance(
            model,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction
        );
        model.name = "Original CAD game model";
        var origin = Find(model.transform, "AxisOrigin");
        var front = Find(model.transform, "AxisIntake");
        var up = Find(model.transform, "AxisUp");
        Vector3 dir = front.position - origin.position;
        float unit = dir.magnitude;
        model.transform.rotation =
            Quaternion.Inverse(Quaternion.LookRotation(dir, up.position - origin.position))
            * model.transform.rotation;
        model.transform.localScale = model.transform.localScale / unit;
        model.transform.position -= origin.position;
        model.transform.localPosition -= Vector3.forward * CadCenterX;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            renderer.sharedMaterials = renderer
                .sharedMaterials.Select(m =>
                    m && materials.ContainsKey(m.name) ? materials[m.name] : m
                )
                .ToArray();
        var check =
            Find(model.transform, "AxisIntake").position
            - Find(model.transform, "AxisOrigin").position;
        if (Vector3.Distance(check, Vector3.forward) > .001f)
            throw new InvalidOperationException("FBX metre/forward conversion failed: " + check);
        var left =
            Find(model.transform, "AxisLeft").position
            - Find(model.transform, "AxisOrigin").position;
        if (Vector3.Distance(left, Vector3.left) > .001f)
            throw new InvalidOperationException("FBX handedness mismatch: " + left);
        return model;
    }

    private static ConfigurableJoint Rail(
        Transform root,
        Rigidbody connected,
        string name,
        Vector3 pivot,
        float mass,
        Transform[] visuals,
        float travel
    )
    {
        var go = New(name, root, pivot);
        foreach (var visual in visuals)
            visual.SetParent(go.transform, true);
        go.transform.localPosition -= Vector3.forward * travel;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = false;
        rb.solverIterations = 24;
        rb.solverVelocityIterations = 12;
        var joint = go.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connected;
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedAnchor = go.transform.localPosition + Vector3.forward * (travel * .5f);
        joint.anchor = Vector3.zero;
        joint.xMotion = joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.linearLimit = new SoftJointLimit
        {
            limit = travel * .5f,
            contactDistance = .005f,
            bounciness = 0,
        };
        joint.angularXMotion =
            joint.angularYMotion =
            joint.angularZMotion =
                ConfigurableJointMotion.Locked;
        joint.zDrive = new JointDrive
        {
            positionSpring = 0,
            positionDamper = 600,
            maximumForce = 120,
        };
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = .005f;
        joint.projectionAngle = 2f;
        joint.enableCollision = false;
        return joint;
    }

    private static BoxCollider AddRailBox(
        Transform parent,
        string name,
        Vector3 deployed,
        Vector3 size,
        float travel
    )
    {
        var box = Box(parent, name, Vector3.zero, size);
        if (name.StartsWith("Hopper moving"))
            box.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        box.transform.position = parent.parent.TransformPoint(deployed - Vector3.forward * travel);
        return box;
    }

    private static InteractionRoller Roller(
        Transform parent,
        string name,
        Vector3 rootPosition,
        float radius,
        float width,
        float angle,
        bool belt,
        float span
    )
    {
        var go = New(name, parent, Vector3.zero);
        go.transform.position = parent.root.TransformPoint(rootPosition);
        go.transform.localRotation = Quaternion.Euler(angle, 0, 0);
        var roller = go.AddComponent<InteractionRoller>();
        Set(roller, "radius", radius / .0254f);
        Set(roller, "length", width / .0254f);
        Set(roller, "useBeltMode", belt);
        Set(roller, "beltLength", span / .0254f);
        Set(
            roller,
            "maxSquish",
            (
                name == "Intake transfer" ? .042f
                : name == "Indexer belt" ? .019f
                : name.StartsWith("Shooter") ? .025f
                : belt ? .032f
                : Mathf.Min(radius * .7f, .019f)
            ) / .0254f
        );
        Set(roller, "rpm", 0f);
        Set(
            roller,
            "motor",
            belt ? InteractionRoller.RollerMotor.Shooter : InteractionRoller.RollerMotor.FullSize
        );
        Set(roller, "compressionStiffness", name.StartsWith("Shooter") ? 2500f : 700f);
        Set(roller, "compressionDamping", 15f);
        Set(roller, "scrubForces", 6f);
        Set(roller, "motorSlipResponse", 1f);
        if (name.StartsWith("Shooter"))
        {
            Set(roller, "motorSlipResponse", .08f);
            Set(roller, "frictionCoupledToNormal", true);
            Set(roller, "frictionCoefficient", 3f);
        }
        Set(roller, "oppositeReaction", true);
        Set(roller, "ignoreLayers", (LayerMask)~LayerMask.GetMask("GamePiece1", "GamePiece2"));
        return roller;
    }

    private static void ConfigureDrive(GameObject root, Rigidbody rb)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Robots/Drivetrain Variant.prefab"
        );
        var drive = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
        PrefabUtility.UnpackPrefabInstance(
            drive,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction
        );
        drive.name = "DriveTrain";
        drive.transform.localPosition = Vector3.zero;
        drive.transform.localRotation = Quaternion.identity;
        drive.transform.localScale = Vector3.one;
        var wheels = drive.GetComponentsInChildren<WheelCollider>();
        if (wheels.Length != 4)
            throw new InvalidOperationException("Expected four MoSim swerve wheels");
        int index = 0;
        foreach (var wheel in wheels)
        {
            int i = index++;
            wheel.transform.localPosition = new Vector3(
                i % 2 == 0 ? .297375f : -.297375f,
                .0695935f,
                i < 2 ? .259275f : -.259275f
            );
            foreach (var solid in wheel.GetComponentsInChildren<MeshCollider>())
            {
                solid.transform.localPosition = new Vector3(0, -.0125f, 0);
                solid.transform.localScale = new Vector3(.098f, .01905f, .098f);
            }
            wheel.radius = .049f;
            wheel.mass = 1.2f;
            wheel.suspensionDistance = .025f;
            wheel.suspensionSpring = new JointSpring
            {
                spring = 50000f,
                damper = 3500f,
                targetPosition = .5f,
            };
        }
        var legacy = root.GetComponent<DriveController>();
        var controller = root.AddComponent<Turbo4744DriveController>();
        EditorUtility.CopySerializedManagedFieldsOnly(legacy, controller);
        Object.DestroyImmediate(legacy);
        Set(controller, "drivetrainParent", drive);
        Set(controller, "maxVelocity", new SecureFloat(4.7f));
        Set(controller, "maxOmega", 8.5f * Mathf.Rad2Deg);
        Set(controller, "realWorldWeightLbs", 62f / .45359237f);
        Set(controller, "gearRatio", 5.9f);
        Set(controller, "wheelDiameterInches", .098f / .0254f);
        Set(controller, "frictionCoefficient", 1.3f);
        Set(controller, "biteFactor", .32f);
        Set(controller, "contactPatchWidthInches", 1.5f);
        var motor = new Motor();
        Set(motor, "motorType", MotorConstants.MotorType.KrakenX60_FOC);
        Set(controller, "motor", motor);
        var so = new SerializedObject(controller);
        var config = so.FindProperty("motor").FindPropertyRelative("config");
        float rotationsPerMetre = 5.9f / (Mathf.PI * .098f);
        config.FindPropertyRelative("K_p").floatValue = 1f / rotationsPerMetre;
        config.FindPropertyRelative("K_s").floatValue = .5f;
        config.FindPropertyRelative("K_v").floatValue = 2.35f / rotationsPerMetre;
        config.FindPropertyRelative("K_a").floatValue = .3f / rotationsPerMetre;
        config.FindPropertyRelative("StatorLimit").floatValue = 100f;
        config.FindPropertyRelative("SupplyLimit").floatValue = 60f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureNet(GameObject root, Turbo4744 robot)
    {
        const int nx = 16,
            ny = 20;
        var pins = New("Net frame attachments", root.transform, Vector3.zero).transform;
        var rest = New("Net rest frame", root.transform, S(.332f, 0, .533f)).transform;
        var go = New("Deforming hopper net", rest, -rest.localPosition);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Net/Net.mat");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/TurboNet.mat")
            ?? new Material(source);
        material.CopyPropertiesFromMaterial(source);
        material.name = "Turbo woven net";
        material.SetFloat("_Cull", 0);
        material.SetTextureScale("_BaseMap", new Vector2(18, 16));
        SaveAsset(material, Root + "/Materials/TurboNet.mat");
        renderer.sharedMaterial = material;
        var solver = go.AddComponent<NetSolver>();
        go.AddComponent<NetParentFollower>();
        solver.resolutionX = nx;
        solver.resolutionY = ny;
        solver.netWidth = .71f;
        solver.netHeight = .612f;
        solver.useAuthoredRestShape = false;
        solver.restLengthScale = 1;
        solver.subSteps = 3;
        solver.stretchCompliance = .0005f;
        solver.shearCompliance = .0002f;
        solver.bendCompliance = .0035f;
        // 320 nodes represent a roughly 160 g textile, not a 1.6 kg roof.
        // Heavy nodes generate excessive collision reactions under a full load.
        solver.damping = .025f;
        solver.particleMass = .0005f;
        solver.visualSmoothingIntensity = .7f;
        solver.collisionRadius = .012f;
        solver.collisionLayers = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        solver.anchorBorderFraction = 0;
        var rig = root.AddComponent<Turbo4744NetRig>();
        rig.leftPins = new Transform[ny];
        rig.rightPins = new Transform[ny];
        for (int x = 0; x < nx; x++)
        {
            float side = Mathf.Lerp(-.355f, .355f, (float)x / (nx - 1));
            var rear = New(
                "Rear net pin " + x,
                pins,
                new Vector3(side, .533f, .0036175f)
            ).transform;
            var front = New(
                "Front net pin " + x,
                robot.hopperRail.transform,
                Vector3.zero
            ).transform;
            front.position = root.transform.TransformPoint(
                new Vector3(side, .533f, .6156175f - .22946f)
            );
            solver.customAnchors.Add(
                new NetSolver.NodeAnchor
                {
                    targetTransform = rear,
                    gridX = x,
                    gridY = 0,
                }
            );
            solver.customAnchors.Add(
                new NetSolver.NodeAnchor
                {
                    targetTransform = front,
                    gridX = x,
                    gridY = ny - 1,
                }
            );
            if (x == 0)
            {
                rig.rearLeft = rig.leftPins[0] = rear;
                rig.frontLeft = rig.leftPins[ny - 1] = front;
            }
            if (x == nx - 1)
            {
                rig.rearRight = rig.rightPins[0] = rear;
                rig.frontRight = rig.rightPins[ny - 1] = front;
            }
        }
        for (int y = 1; y < ny - 1; y++)
        {
            rig.leftPins[y] = New("Left lacing " + y, pins, Vector3.zero).transform;
            rig.rightPins[y] = New("Right lacing " + y, pins, Vector3.zero).transform;
            solver.customAnchors.Add(
                new NetSolver.NodeAnchor
                {
                    targetTransform = rig.leftPins[y],
                    gridX = 0,
                    gridY = y,
                }
            );
            solver.customAnchors.Add(
                new NetSolver.NodeAnchor
                {
                    targetTransform = rig.rightPins[y],
                    gridX = nx - 1,
                    gridY = y,
                }
            );
        }
        rig.UpdatePins();
        solver.Rebuild();
        // Menu/spawn displays have no simulation components. Keep a persistent
        // preview mesh; runtime NetSolver creates its own dynamic mesh.
        var snapshot = Object.Instantiate(filter.sharedMesh);
        snapshot.name = "Turbo4744NetFolded";
        string meshPath = Root + "/Model/Turbo4744NetFolded.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved)
        {
            EditorUtility.CopySerialized(snapshot, saved);
            Object.DestroyImmediate(snapshot);
            EditorUtility.SetDirty(saved);
        }
        else
        {
            saved = snapshot;
            AssetDatabase.CreateAsset(saved, meshPath);
        }
        filter.sharedMesh = saved;
        robot.roofNet = go.transform;
    }

    private static void ConfigureBumpers(
        Turbo4744 robot,
        GameObject model,
        Dictionary<string, Material> mats
    )
    {
        var mesh = Find(model.transform, "Bumpers_Geometry").GetComponent<MeshRenderer>();
        Set(
            robot,
            "bumpers",
            new BumperSettings
            {
                bumperMeshes = new[] { mesh },
                materialIndex = 0,
                redBumperMaterial = mats["BumperRed"],
                blueBumperMaterial = mats["BumperBlue"],
                Vanity = new VanityBumpers
                {
                    AllowVanity = false,
                    DisableOnVanity = Array.Empty<GameObject>(),
                    EnableOnVanity = Array.Empty<GameObject>(),
                },
            }
        );
        var rsl = robot
            .GetComponentsInChildren<MeshRenderer>()
            .FirstOrDefault(r => r.name.Contains("RSL"));
        if (rsl)
            robot.RSLSettings = new RSLSettings
            {
                RSLMesh = rsl,
                materialIndex = 0,
                rslOnMaterial = rsl.sharedMaterial,
                rslOffMaterial = rsl.sharedMaterial,
            };
    }

    private static void ConfigurePreloads(Turbo4744 robot)
    {
        var holder = New("Fuel preload positions", robot.transform, Vector3.zero);
        var preloads = new List<GameObject>();
        var fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        foreach (float y in new[] { .28f, .43f })
        foreach (float x in new[] { -.225f, -.075f, .075f, .225f })
        {
            var piece = (GameObject)PrefabUtility.InstantiatePrefab(fuel, holder.transform);
            piece.name = "Preloaded fuel " + preloads.Count;
            piece.transform.localPosition = new Vector3(x, y, .29f - CadCenterX);
            preloads.Add(piece);
        }
        robot.preloads = preloads.ToArray();
    }

    private static GameObject MakeDisplayPrefab(GameObject source, bool spawn)
    {
        var root = Object.Instantiate(source);
        root.SetActive(false);
        root.name = spawn ? "4744 Turbo Spawn" : "4744 Turbo Main Menu";
        foreach (var fuel in root.GetComponentsInChildren<RebuiltGamePieceController>(true))
            Object.DestroyImmediate(fuel.gameObject);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            Object.DestroyImmediate(component);
        foreach (var joint in root.GetComponentsInChildren<Joint>(true))
            Object.DestroyImmediate(joint);
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            if (!spawn || rb.gameObject != root)
                Object.DestroyImmediate(rb);
        if (spawn)
        {
            var rb = root.GetComponent<Rigidbody>();
            rb.mass = 62;
            rb.drag = 4;
            rb.angularDrag = 5;
            rb.centerOfMass = Vector3.zero;
            var settings = root.AddComponent<SpawnpointSelectionSettings>();
            settings.bumperMeshes = new[]
            {
                Find(root.transform, "Bumpers_Geometry").GetComponent<MeshRenderer>(),
            };
            settings.materialIndex = 0;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.gameObject.tag = "Bumpers";
        }
        else
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            root.AddComponent<Animator>();
        }
        root.SetActive(true);
        var result = PrefabUtility.SaveAsPrefabAsset(
            root,
            Root + (spawn ? "/Turbo4744Spawn.prefab" : "/Turbo4744MainMenu.prefab")
        );
        Object.DestroyImmediate(root);
        return result;
    }

    private static GameObject[] BuildCards()
    {
        var titles = new[] { "Deploy and collect", "Shoot or pass", "Clear a jam" };
        var descriptions = new[]
        {
            "Intake opens Turbo's pickup and hopper and cancels shooting. Stow retracts both. The net folds with the frame.",
            "Auto Shoot selects the hub in your alliance zone and a side delivery target in neutral. Press again for three hard pump cycles. Auto Pass forces delivery. Intake cancels aiming and restores normal driving.",
            "Soft pumping starts automatically while firing. Robot Special requests three hard cycles. Face Down is a held reverse override. Manual Shoot bypasses aiming. Turbo has no simulated climber.",
        };
        var cards = new List<GameObject>();
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/UI/Components/TutorialCardTemplate.prefab"
        );
        for (int i = 0; i < titles.Length; i++)
        {
            var card = Object.Instantiate(template);
            card.name = "Turbo tutorial " + (i + 1);
            foreach (var text in card.GetComponentsInChildren<TMP_Text>(true))
                text.text = "<b>" + titles[i] + "</b>\n" + descriptions[i];
            var image = card.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(c => c.name == "Image");
            if (image)
            {
                image.sprite = ImportPreview();
                image.preserveAspect = true;
                image.color = Color.white;
            }
            cards.Add(
                PrefabUtility.SaveAsPrefabAsset(
                    card,
                    Root + "/TutorialCards/" + card.name + ".prefab"
                )
            );
            Object.DestroyImmediate(card);
        }
        return cards.ToArray();
    }

    private static Sprite ImportPreview()
    {
        string path = Root + "/Model/preview.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    [MenuItem("Tools/Modding/Ninjas 4744/Validate Turbo")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (!prefab)
            throw new InvalidOperationException("Turbo prefab missing");
        var robot = prefab.GetComponent<Turbo4744>();
        if (
            !robot
            || !robot.intakeRail
            || !robot.hopperRail
            || !robot.autoAim
            || !robot.muzzle
            || !robot.feedHold
            || !robot.intakeHold
            || !robot.shooterContact
            || !robot.indexerContact
            || !robot.intakeContact
            || !robot.storageContact
        )
            throw new InvalidOperationException("Required Turbo mechanism references missing");
        var missing = prefab
            .GetComponentsInChildren<Transform>(true)
            .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        if (missing != 0)
            throw new InvalidOperationException("Missing scripts: " + missing);
        var meshes = prefab
            .GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.sharedMesh && !m.GetComponentInParent<RebuiltGamePieceController>(true))
            .ToArray();
        long triangles = meshes.Sum(m => (long)m.sharedMesh.triangles.Length / 3);
        if (triangles > 1_000_000)
            throw new InvalidOperationException("Triangle budget exceeded: " + triangles);
        float mass =
            prefab.GetComponent<Rigidbody>().mass
            + robot.intakeRail.GetComponent<Rigidbody>().mass
            + robot.hopperRail.GetComponent<Rigidbody>().mass;
        if (Mathf.Abs(mass - 62f) > .01f)
            throw new InvalidOperationException("Unexpected total configured mass: " + mass);
        string json =
            "{\"team\":4744,\"season\":2026,\"triangles\":"
            + triangles
            + ",\"rigidbody_mass_kg\":"
            + mass.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"missing_scripts\":"
            + missing
            + ",\"status\":\"assembled\"}";
        File.WriteAllText(Artifacts + "/assembly-audit.json", json);
        Debug.Log("TURBO_VALIDATED " + json);
    }

    public static void AssembleAndBuild()
    {
        Assemble();
        Build();
    }

    [MenuItem("Tools/Modding/Ninjas 4744/Build Windows Mod")]
    public static void Build()
    {
        Validate();
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        if (!ModBuildUtility.TryBuildModpack(pack, true, out var path))
            throw new InvalidOperationException("MoSim mod build failed");
        File.WriteAllText(Artifacts + "/mod-build-path.txt", path);
        CheckBundle(path);
        AssetDatabase.ExportPackage(
            Root,
            Artifacts + "/Ninjas4744-Turbo2026.unitypackage",
            ExportPackageOptions.Recurse
        );
        Debug.Log("TURBO_BUILT " + path);
    }

    // Run in a fresh editor after the full bundle build. BuildAssetBundles can
    // restore an earlier ScriptAssemblies snapshot before the SDK copies the
    // mod DLL; a code-only SDK build copies the final compiled runtime instead.
    public static void SyncRuntime()
    {
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        if (!ModBuildUtility.TryBuildModpack(pack, false, out var path))
            throw new InvalidOperationException("Runtime synchronization failed");
        string dll = Path.Combine(
            path,
            EditorUserBuildSettings.activeBuildTarget.ToString().ToLowerInvariant(),
            "Ninjas4744.dll"
        );
        if (
            !File.ReadAllBytes(dll)
                .SequenceEqual(File.ReadAllBytes(typeof(Turbo4744).Assembly.Location))
        )
            throw new InvalidOperationException(
                "The packaged runtime differs from the compiled assembly"
            );
        CheckBundle(path);
        AssetDatabase.ExportPackage(
            Root,
            Artifacts + "/Ninjas4744-Turbo2026.unitypackage",
            ExportPackageOptions.Recurse
        );
        Debug.Log("TURBO_RUNTIME_SYNCHRONIZED " + dll);
    }

    private static void CheckBundle(string path)
    {
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        var bundle = AssetBundle.LoadFromFile(
            Path.Combine(path, "standalonewindows64", ModBuildUtility.GetModId(pack) + "_assets")
        );
        if (!bundle)
            throw new InvalidOperationException("Built bundle did not load");
        try
        {
            var loaded = bundle
                .LoadAllAssets<BaseModpackSO>()
                .Single(p => p.ModpackName == pack.ModpackName);
            var metadata = loaded.Robots.Single();
            var prefab = metadata.RobotPrefab;
            var robot = prefab.GetComponent<Turbo4744>();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            int missing = prefab
                .GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var references = typeof(Turbo4744)
                .Assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();
            bool passed =
                missing == 0
                && robot
                && robot.intakeRail
                && robot.hopperRail
                && robot.shooterContact
                && robot.indexerContact
                && prefab.GetComponentsInChildren<Turbo4744ShooterContact>().Length == 4
                && robot.intakeContact
                && robot.intakeContact.entryKeeper
                && robot.storageContact
                && robot.storageContact.capturedOnly
                && robot.intakeContact.robotBody == robot.intakeRail.GetComponent<Rigidbody>()
                && prefab.GetComponentsInChildren<NetSolver>().Length == 1
                && robot.shooterContact.robotBody == prefab.GetComponent<Rigidbody>()
                && robot.indexerContact.robotBody == prefab.GetComponent<Rigidbody>()
                && metadata.TeamNumber == 4744
                && metadata.GameYear == 2026
                && metadata.MainMenuPrefab
                && metadata.SpawnSelectionPrefab
                && robot.preloads.Length == 8
                && robot.preloads.All(p =>
                    p.GetComponent<Rigidbody>()
                    && p.GetComponent<RebuiltGamePieceController>()
                    && p.layer == LayerMask.NameToLayer("GamePiece1")
                )
                && metadata.TutorialCards.Count == 3
                && prefab.GetComponentsInChildren<WheelCollider>().Length == 4
                && materials.All(m =>
                    m && m.shader && m.shader.name != "Hidden/InternalErrorShader"
                )
                && references.All(a => !a.StartsWith("UnityEditor"));
            var report = new BundleReport
            {
                passed = passed,
                modpack = loaded.ModpackName,
                game = loaded.GameOption.name,
                team = metadata.TeamNumber,
                robots = loaded.Robots.Count,
                missingScripts = missing,
                renderers = renderers.Length,
                materials = materials.Length,
                preloads = robot.preloads.Length,
                tutorialCards = metadata.TutorialCards.Count,
                assemblyReferences = references,
            };
            File.WriteAllText(Artifacts + "/bundle-audit.json", JsonUtility.ToJson(report, true));
            if (!passed)
                throw new InvalidOperationException(
                    "Built bundle failed reference/metadata checks"
                );
            Debug.Log("TURBO_BUNDLE_VALIDATED " + JsonUtility.ToJson(report));
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    public static void RenderPreview()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single
        );
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var intake = Find(instance.transform, "Intake rail");
        var hopper = Find(instance.transform, "Sliding hopper");
        var net = Find(instance.transform, "Deforming hopper net");
        var intakeClosed = intake.localPosition;
        var hopperClosed = hopper.localPosition;
        var light = new GameObject("Preview key").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2f;
        light.transform.rotation = Quaternion.Euler(42f, -35f, 0);
        var fill = new GameObject("Preview fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = .75f;
        fill.transform.rotation = Quaternion.Euler(20f, 140f, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.45f, .48f, .52f);
        var camera = new GameObject("Turbo preview camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(1.3f, .78f, 1.25f);
        camera.transform.LookAt(new Vector3(0, .26f, .1f));
        camera.orthographic = true;
        camera.orthographicSize = .60f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .05f, .065f, 0f);
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 20f;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
        Directory.CreateDirectory(Artifacts);
        foreach (bool deployed in new[] { false, true })
        {
            intake.localPosition = intakeClosed + Vector3.forward * (deployed ? .24f : 0f);
            hopper.localPosition = hopperClosed + Vector3.forward * (deployed ? .22946f : 0f);
            net.localScale = Vector3.one;
            instance.GetComponent<Turbo4744NetRig>().UpdatePins();
            net.GetComponent<NetSolver>().Rebuild();
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(
                Artifacts + "/Turbo4744-" + (deployed ? "deployed" : "stowed") + ".png",
                png
            );
            if (deployed)
                File.WriteAllBytes(Artifacts + "/Turbo4744-unity-preview.png", png);
        }
        camera.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(instance);
        Debug.Log("TURBO_PREVIEW_RENDERED");
    }

    private static GameObject New(string name, Transform parent, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return go;
    }

    private static BoxCollider Box(Transform parent, string name, Vector3 local, Vector3 size)
    {
        var go = New(name, parent, local);
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    private static Transform Find(Transform root, string name)
    {
        var result = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == name);
        if (!result)
            throw new InvalidOperationException("Model node missing: " + name);
        return result;
    }

    private static void SetLayer(GameObject root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (!child.GetComponentInParent<RebuiltGamePieceController>(true))
                child.gameObject.layer = layer;
    }

    private static void SaveAsset(Object obj, string path)
    {
        if (!AssetDatabase.Contains(obj))
            AssetDatabase.CreateAsset(obj, path);
        EditorUtility.SetDirty(obj);
    }

    private static void Set(object target, string name, object value)
    {
        var type = target.GetType();
        FieldInfo field = null;
        while (type != null && field == null)
        {
            field = type.GetField(
                name,
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            );
            type = type.BaseType;
        }
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
        if (target is Object unityObject)
            EditorUtility.SetDirty(unityObject);
    }
}
