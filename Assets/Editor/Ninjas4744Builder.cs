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
    public const string SimpleRobotPath = Root + "/Turbo4744Simple.prefab";
    private const float CadCenterX = .0223825f;

    // Native CAD bounds: keep the wheel cutouts instead of filling the bumper envelope.
    private static readonly (string name, Vector3 min, Vector3 max)[] BellypanSections =
    {
        ("rear", new Vector3(-.30114f, -.22840f, .04680f), new Vector3(-.06460f, .22843f, .05080f)),
        (
            "middle",
            new Vector3(-.06460f, -.36404f, .04680f),
            new Vector3(.21580f, .36375f, .05080f)
        ),
        ("front", new Vector3(.21580f, -.12745f, .04680f), new Vector3(.27886f, .12762f, .05080f)),
    };
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
        public string version,
            selectorLogo,
            modpack,
            game;
        public float selectorWidth,
            selectorHeight;
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
        // Nominal travel is 21.2 mm per native 4.5 ms step. Swept CCD held
        // the pose despite nonzero velocity in dense-fuel native tests;
        // discrete compound contacts resolved the same load continuously.
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        var bumper = root.GetComponent<BoxCollider>() ?? root.AddComponent<BoxCollider>();
        bumper.center = new Vector3(0, .09578f, 0);
        bumper.size = new Vector3(.8884f, .127f, .79548f);
        // The framework's filled box is a bounds sensor, not the bumper shell.
        bumper.isTrigger = true;
        bumper.gameObject.tag = "BlueRobot";
        ConfigureBumperContacts(root.transform, bumper);
        ConfigureBellypanContacts(root.transform);

        var model = LoadModel(root.transform, mats);
        var intakeVisual = Find(model.transform, "Intake");
        var intakeRollerVisual = Find(model.transform, "IntakeRoller");
        robot.intakeRollerVisual = intakeRollerVisual;
        var hopperVisual = Find(model.transform, "Hopper");
        var kickerVisual = Find(model.transform, "Kicker");
        var netVisual = Find(model.transform, "RoofNet");
        if (!intakeVisual.IsChildOf(hopperVisual) || !kickerVisual.IsChildOf(hopperVisual))
            throw new InvalidOperationException("The source intake/kicker assembly is incomplete");
        robot.intakeRail = Rail(
            root.transform,
            body,
            "Intake rail",
            S(.106f, 0, .3535f),
            5.5f,
            Array.Empty<Transform>(),
            .24f
        );
        robot.hopperRail = Rail(
            root.transform,
            body,
            "Sliding hopper and kicker carrier",
            S(0, 0, 0),
            2.5f,
            new[] { hopperVisual },
            .22946f
        );
        var kickerHinge = New(
            "Retractable hopper kicker",
            robot.hopperRail.transform,
            Vector3.zero
        ).transform;
        kickerHinge.position = kickerVisual.position;
        kickerVisual.SetParent(kickerHinge, true);
        var kickerRig = root.AddComponent<Turbo4744KickerRig>();
        kickerRig.robot = robot;
        kickerRig.hopper = robot.hopperRail.transform;
        kickerRig.hopperClosedPosition = robot.hopperRail.transform.localPosition;
        kickerRig.travel = robot.hopperTravel;
        kickerRig.kicker = kickerHinge;
        kickerRig.UpdatePose();
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
        var fixedFloor = Box(
            root.transform,
            "Fixed indexer inlet floor",
            S(.34f, 0, .175f),
            new Vector3(.68f, .012f, .04f)
        );
        fixedFloor.sharedMaterial = trayPlastic;
        // One-way intake contacts allow inbound fuel to rise through the entry,
        // then provide a complete, continuous floor beneath stored fuel.
        var storageFloor = Box(
            root.transform,
            "Telescoping hopper storage floor",
            S(.495f, 0, .175f),
            new Vector3(.68f, .012f, .31f)
        );
        storageFloor.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        storageFloor.sharedMaterial = trayPlastic;
        AddRailBox(
            robot.hopperRail.transform,
            "Hopper left wall",
            S(.40f, .359f, .37f),
            new Vector3(.006f, .32f, .5f),
            robot.hopperTravel
        );
        AddRailBox(
            robot.hopperRail.transform,
            "Hopper right wall",
            S(.40f, -.359f, .37f),
            new Vector3(.006f, .32f, .5f),
            robot.hopperTravel
        );
        var frontPanel = AddRailBox(
            robot.hopperRail.transform,
            "Hopper end wall",
            S(.648f, 0, .37f),
            new Vector3(.714f, .32f, .006f),
            robot.hopperTravel
        );
        frontPanel.sharedMaterial = trayPlastic;
        robot.intakeHold = AddRailBox(
            robot.hopperRail.transform,
            "Intake inlet retention contact",
            S(.61f, 0, .22f),
            new Vector3(.714f, .26f, .02f),
            robot.hopperTravel
        );
        // The inlet lip travels with the hopper front panel.
        robot.intakeHold.transform.localRotation = Quaternion.Euler(25f, 0, 0);
        robot.intakeHold.excludeLayers = ~LayerMask.GetMask(
            "GamePiece1",
            "GamePiece2",
            "GamePieceAlt"
        );
        var keeper = root.AddComponent<Turbo4744FuelKeeper>();
        keeper.robot = robot;
        keeper.floor = storageFloor;
        keeper.fixedFloor = fixedFloor;
        keeper.inlet = robot.intakeHold;
        keeper.frontPanel = frontPanel;
        var hopperRig = root.AddComponent<Turbo4744HopperRig>();
        hopperRig.floor = storageFloor;
        hopperRig.frontPanel = frontPanel;
        hopperRig.UpdateFloor();
        AddRailBox(
            robot.intakeRail.transform,
            "Intake left guide",
            S(.465f, .342f, .16f),
            new Vector3(.008f, .18f, .27f),
            .24f
        );
        AddRailBox(
            robot.intakeRail.transform,
            "Intake right guide",
            S(.465f, -.342f, .16f),
            new Vector3(.008f, .18f, .27f),
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
            -Vector3.forward * robot.intakeTravel
        );
        robot.intakeContact = transfer.AddComponent<Turbo4744ShooterContact>();
        robot.intakeContact.robotBody = robot.intakeRail.GetComponent<Rigidbody>();
        robot.intakeContact.wheelRadius = .025f;
        // The contact fades over one fuel radius at its edge. Cover the full
        // tray width so a sphere against the side guide still gets support.
        robot.intakeContact.centerline = new[]
        {
            new Vector3(0, .08f, .68f),
            new Vector3(0, .08f, .64f),
            new Vector3(0, .18f, .59f),
            new Vector3(0, .28f, .55f),
            new Vector3(0, .35f, .49f),
            new Vector3(0, .34f, .42f),
            new Vector3(0, .28f, .34f),
        };
        robot.intakeContact.halfWidth = .34f;
        robot.intakeContact.normalClearance = .06f;
        robot.intakeContact.maxTractionForce = 320;
        robot.intakeContact.tractionResponse = 120;
        robot.intakeContact.maxTotalForce = 600;
        robot.intakeContact.maxTotalTractionForce = 100;
        robot.intakeContact.normalStiffness = 900;
        robot.intakeContact.normalDamping = 28;
        robot.intakeContact.maxNormalForce = 80;
        robot.intakeContact.forwardTractionOnly = true;
        robot.intakeContact.unilateralContact = false;
        robot.intakeContact.maxSurfaceSpeed = 3.6f;
        robot.intakeContact.compensateNormalGravity = true;
        robot.intakeContact.allowReverse = true;
        robot.intakeContact.requiresCommand = true;
        robot.intakeContact.entryKeeper = keeper;
        // A powered contact is not a supporting belt. Back the pickup path
        // physically so fuel that meets a packed hopper cannot roll underneath
        // the one-way storage floor instead of climbing into it.
        var intakeBacking = new List<BoxCollider>();
        // The final two sections hand down onto the tray/roller bed; a high
        // backing plate there would close the storage space beneath the intake.
        for (int i = 1; i < robot.intakeContact.centerline.Length - 2; i++)
        {
            var start = robot.intakeContact.centerline[i - 1];
            var end = robot.intakeContact.centerline[i];
            var direction = (end - start).normalized;
            var normal = Vector3.Cross(Vector3.right, direction).normalized;
            var backing = Box(
                transfer.transform,
                "Intake belt backing " + i,
                (start + end) * .5f - normal * .081f,
                new Vector3(.64f, .012f, Vector3.Distance(start, end) + .012f)
            );
            backing.transform.localRotation = Quaternion.LookRotation(direction, normal);
            backing.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            backing.sharedMaterial = trayPlastic;
            backing.enabled = false;
            intakeBacking.Add(backing);
        }
        robot.intakeBacking = intakeBacking.ToArray();
        // The main hopper is passive and fixed to the chassis.
        robot.storageContact = null;
        ConfigureIndexer(root, model, robot, body, keeper, trayPlastic);
        robot.floorRollers = Array.Empty<InteractionRoller>();
        robot.acceleratorRollers = Array.Empty<InteractionRoller>();
        var accelerator = New(
            "Lower entrance accelerator",
            root.transform,
            new Vector3(.0131f, 0, 0)
        );
        robot.acceleratorContact = accelerator.AddComponent<Turbo4744ShooterContact>();
        var ac = robot.acceleratorContact;
        ac.robotBody = body;
        ac.lowerEntry = robot.indexer;
        ac.wheelRadius = .0254f;
        ac.normalClearance = .065f;
        ac.halfWidth = .1565f;
        ac.maxTotalForce = 320;
        ac.rampRpmPerSecond = 60000;
        ac.maxSurfaceSpeed = 2.5f;
        ac.requiresCommand = true;
        ac.lateralResponse = 40;
        ac.maxNormalForce = 120;
        ac.centerline = new[]
        {
            new Vector3(0, .200f, .12f),
            new Vector3(0, .185f, -.08f),
            new Vector3(0, .245f, -.11f),
            new Vector3(0, .335f, -.11f),
            new Vector3(0, .395f, -.075f),
            new Vector3(0, .435f, -.038f),
        };
        var contact = New(
            "Wide shooter tire contact",
            root.transform,
            S(-.13f, -.0131f, .40f) + Quaternion.Euler(58f, 0, 0) * Vector3.up * .115f
        );
        contact.transform.localRotation = Quaternion.Euler(58f, 0, 0);
        robot.shooterContact = contact.AddComponent<Turbo4744ShooterContact>();
        robot.shooterContact.robotBody = body;
        robot.shooterContact.lowerEntry = robot.indexer;
        robot.shooterContact.halfWidth = .1565f;
        robot.shooterContact.maxTotalForce = 480;
        robot.shooterContact.lateralResponse = 60;
        foreach (int side in new[] { -1, 1 })
        {
            var guide = Box(
                root.transform,
                "Wide shooter side " + side,
                new Vector3(.0131f + side * .1605f, .37f, -.1625f),
                new Vector3(.008f, .52f, .225f)
            );
            guide.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            guide.sharedMaterial = trayPlastic;
            var axis = Quaternion.Euler(58f, 0, 0);
            var hood = Box(
                root.transform,
                "Shooter hood " + side,
                new Vector3(.0131f, .461f, -.055f) + axis * new Vector3(0, side * .081f, -.16f),
                new Vector3(.313f, .005f, .16f)
            );
            hood.transform.localRotation = axis;
            hood.excludeLayers = guide.excludeLayers;
            hood.sharedMaterial = trayPlastic;
        }
        robot.shooterRollers = Array.Empty<InteractionRoller>();
        robot.flywheels = Array.Empty<PvffController>();
        robot.feedHold = Box(
            root.transform,
            "Stopped indexer retention contact",
            new Vector3(0, .205f, .095f),
            new Vector3(.68f, .23f, .008f)
        );
        robot.feedHold.excludeLayers = ~LayerMask.GetMask(
            "GamePiece1",
            "GamePiece2",
            "GamePieceAlt"
        );

        robot.indexer.retention = robot.feedHold;
        robot.indexer.releaseRetention = Box(
            root.transform,
            "Flywheel readiness retention gate",
            new Vector3(.0131f, .43f, -.036f),
            new Vector3(.313f, .18f, .008f)
        );
        robot.indexer.releaseRetention.transform.localRotation = Quaternion.Euler(58f, 0, 0);
        robot.indexer.releaseRetention.excludeLayers = robot.feedHold.excludeLayers;
        robot.indexer.releaseRetention.sharedMaterial = trayPlastic;

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
        // Keep a serialized reference so the SDK includes the contact handler
        // in its runtime dependency set as well as the asset bundle.
        root.AddComponent<Turbo4744BumperContacts>();
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
        Set(metadata, "robotImage", ImportSelectorLogo());
        SaveAsset(metadata, Root + "/Turbo4744.asset");
        var pack = AssetDatabase.LoadAssetAtPath<RebuiltModpackSO>(PackPath)
            ?? ScriptableObject.CreateInstance<RebuiltModpackSO>();
        Set(pack, "modpackName", "Ninjas 4744 Turbo 2026");
        Set(pack, "modpackVersion", "1.0.1");
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
        BuildSimpleVariant();
        Validate();
        Selection.activeObject = pack;
    }

    [MenuItem("Tools/Modding/Ninjas 4744/Update Control Variants")]
    public static void BuildSimpleVariant()
    {
        AssetDatabase.ImportAsset(
            RobotPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
        );
        var metadata = AssetDatabase.LoadAssetAtPath<RebuiltRobotMetadataSO>(
            Root + "/Turbo4744.asset"
        );
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (!metadata || !original)
            throw new InvalidOperationException("Assemble Turbo before adding its simple variant");
        // A prefab variant inherits the current physical robot, including later
        // mechanism fixes, and overrides only its control scheme and name.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(original);
        GameObject prefab;
        try
        {
            instance.name = "4744 Turbo";
            var robot = instance.GetComponent<Turbo4744>();
            if (!robot)
                throw new InvalidOperationException(
                    "Turbo script import is missing. Recreate the generated Unity compilation cache before building."
                );
            robot.simpleControls = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(robot);
            prefab = PrefabUtility.SaveAsPrefabAsset(instance, SimpleRobotPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
        var simple = new RobotMetadataSO.RobotVariant();
        Set(simple, "displayName", "Turbo");
        Set(simple, "robotPrefab", prefab);
        Set(simple, "mainMenuPrefab", metadata.MainMenuPrefab);
        Set(simple, "spawnSelectionPrefab", metadata.SpawnSelectionPrefab);
        Set(simple, "preloadGamePiece", metadata.RobotPreloadGamePiece);
        Set(simple, "tutorialCards", BuildCards(true).ToList());
        var actual = new RobotMetadataSO.RobotVariant();
        Set(actual, "displayName", "Actual Turbo");
        Set(actual, "robotPrefab", original);
        Set(actual, "mainMenuPrefab", metadata.MainMenuPrefab);
        Set(actual, "spawnSelectionPrefab", metadata.SpawnSelectionPrefab);
        Set(actual, "preloadGamePiece", metadata.RobotPreloadGamePiece);
        Set(actual, "tutorialCards", BuildCards().ToList());
        var variants = metadata
            .AlternateRobots.Where(v =>
                v.DisplayName != "Turbo Simple" && v.DisplayName != "Actual Turbo"
            )
            .ToList();
        variants.Add(actual);
        Set(metadata, "mainRobot", simple);
        Set(metadata, "alternateRobots", variants);
        EditorUtility.SetDirty(metadata);
        AssetDatabase.SaveAssets();
    }

    private static void ValidateSimpleVariant(RobotMetadataSO metadata)
    {
        var actual = metadata.AlternateRobots.Single(v => v.DisplayName == "Actual Turbo");
        if (
            metadata.RobotName != "Turbo"
            || !metadata.RobotPrefab.GetComponent<Turbo4744>().simpleControls
            || metadata.TutorialCards.Count != 3
            || metadata.TutorialCards.Any(c => !c)
            || !actual.RobotPrefab
            || actual.RobotPrefab.GetComponent<Turbo4744>().simpleControls
            || !actual.MainMenuPrefab
            || !actual.SpawnSelectionPrefab
            || !actual.PreloadGamePiece
            || actual.TutorialCards.Count != 3
            || actual.TutorialCards.Any(c => !c)
        )
            throw new InvalidOperationException(
                "Turbo or Actual Turbo is missing its controls or assets"
            );
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
        box.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(
            Root + "/Materials/FeedPlastic.physicMaterial"
        );
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
            // Conservative housing envelopes inside the CAD corner modules.
            // The existing tire mesh supplies the lower, steering wheel shape.
            var housing = Box(
                root.transform,
                "Swerve " + wheel.name + " housing contact",
                new Vector3(wheel.transform.localPosition.x, .10f, wheel.transform.localPosition.z),
                new Vector3(.16f, .10f, .16f)
            );
            housing.sharedMaterial = root.GetComponentsInChildren<BoxCollider>()
                .First(c => c.name == "Bumper rear contact")
                .sharedMaterial;
            wheel.excludeLayers |= LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
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
        // Simulator tread calibration: retain grip with a full independent-fuel
        // payload. Hardware motor gains, gearing and current limits stay below.
        Set(controller, "frictionCoefficient", 1.4f);
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

    private static void ValidateSwerveContacts(GameObject root)
    {
        int fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        var chassis = root.GetComponent<Rigidbody>();
        foreach (var wheel in root.GetComponentsInChildren<WheelCollider>(true))
        {
            var housing = root.GetComponentsInChildren<BoxCollider>(true)
                .Single(c => c.name == "Swerve " + wheel.name + " housing contact");
            var position = root.transform.InverseTransformPoint(wheel.transform.position);
            if (
                !housing.enabled
                || housing.isTrigger
                || housing.transform.parent != root.transform
                || housing.GetComponentInParent<Rigidbody>(true) != chassis
                || housing.gameObject.layer != LayerMask.NameToLayer("Robot")
                || housing.excludeLayers.value != 0
                || housing.includeLayers.value != 0
                || Vector3.Distance(housing.size, new Vector3(.16f, .10f, .16f)) > .00001f
                || Vector3.Distance(
                    housing.transform.localPosition,
                    new Vector3(position.x, .10f, position.z)
                ) > .00001f
                || (wheel.excludeLayers.value & fuelMask) != fuelMask
                || !wheel
                    .GetComponentsInChildren<MeshCollider>(true)
                    .Any(c =>
                        c.enabled
                        && !c.isTrigger
                        && c.convex
                        && (c.excludeLayers.value & fuelMask) == 0
                    )
            )
                throw new InvalidOperationException(
                    "Missing solid swerve module contacts: " + wheel.name
                );
        }
        if (
            root.GetComponentsInChildren<BoxCollider>(true).Count(c => c.name.StartsWith("Swerve "))
            != 4
        )
            throw new InvalidOperationException("Four solid swerve module housings are required");
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
        // The source texture already contains about 11 x 13 openings. Dense
        // field-net tiling makes this small roof disappear into its alpha mips.
        material.SetTextureScale("_BaseMap", new Vector2(2, 2));
        material.SetTextureScale("_MainTex", new Vector2(2, 2));
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Cull", 0);
        // Opaque strands and clipped holes give the cloth stable depth and
        // shadows while preserving the openings in MoSim's supplied texture.
        material.SetFloat("_Surface", 0);
        material.SetFloat("_AlphaClip", 1);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 1);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
        SaveAsset(material, Root + "/Materials/TurboNet.mat");
        renderer.sharedMaterial = material;
        var solver = go.AddComponent<Turbo4744NetSolver>();
        go.AddComponent<Turbo4744NetOrigin>();
        go.AddComponent<Turbo4744NetEnvironment>();
        root.AddComponent<Turbo4744FuelCompliance>();
        solver.resolutionX = nx;
        solver.resolutionY = ny;
        solver.netWidth = .71f;
        solver.netHeight = .612f;
        solver.useAuthoredRestShape = false;
        // Keep the roof tensioned to its telescoping frame, with the estimated 2.3 mm
        // cord diameter and finite foam contacts. Extra textile ease allowed
        // stored fuel to bulge over the native trench crossbar.
        solver.restLengthScale = 1f;
        solver.subSteps = 4;
        solver.collisionSubstepInterval = 1;
        solver.stretchCompliance = .0000003f;
        solver.shearCompliance = .00000015f;
        solver.bendCompliance = .002f;
        // 320 nodes represent a roughly 240 g textile, not a 1.6 kg roof.
        // Heavy nodes generate excessive collision reactions under a full load.
        solver.damping = .05f;
        solver.particleMass = .00075f;
        solver.visualSmoothingIntensity = .7f;
        solver.collisionRadius = .00115f;
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
                pins,
                new Vector3(side, .533f, .6156175f)
            ).transform;
            front.SetParent(robot.hopperRail.transform, true);
            front.position -= root.transform.forward * robot.hopperTravel;
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
        // Complete the physical perimeter at pinned cloth edges. These are
        // rim tubes, not a roof plate: the textile remains free to bulge.
        rig.leftRim = Box(root.transform, "Left net rim", Vector3.zero, Vector3.one * .016f);
        rig.rightRim = Box(root.transform, "Right net rim", Vector3.zero, Vector3.one * .016f);
        var rearRim = Box(
            root.transform,
            "Rear net rim",
            new Vector3(0, .533f, .0036175f),
            new Vector3(.73f, .016f, .016f)
        );
        var frontRim = AddRailBox(
            robot.hopperRail.transform,
            "Front net rim",
            new Vector3(0, .533f, .6156175f),
            new Vector3(.73f, .016f, .016f),
            robot.hopperTravel
        );
        foreach (var rim in new[] { rig.leftRim, rig.rightRim, rearRim, frontRim })
        {
            rim.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            rim.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(
                Root + "/Materials/FeedPlastic.physicMaterial"
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

    private static void ValidateBumperContacts(GameObject root)
    {
        var contacts = root.GetComponentsInChildren<BoxCollider>(true)
            .Where(c => c.name.StartsWith("Bumper ") && c.name.EndsWith(" contact"))
            .ToArray();
        int fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        if (
            contacts.Length != 4
            || contacts.Any(c =>
                !c.enabled
                || c.isTrigger
                || (c.excludeLayers.value & fuelMask) != 0
                || c.GetComponentInParent<Rigidbody>(true) != root.GetComponent<Rigidbody>()
            )
        )
            throw new InvalidOperationException("Four solid chassis bumper contacts are required");
        if (!root.GetComponent<BoxCollider>().isTrigger)
            throw new InvalidOperationException(
                "The filled bumper bounds sensor must stay a trigger"
            );
        if (
            root.GetComponent<Rigidbody>().collisionDetectionMode != CollisionDetectionMode.Discrete
        )
            throw new InvalidOperationException(
                "The validated dense-fuel chassis collision mode is required"
            );
    }

    private static void ValidateMovingHopper(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>();
        var robot = root.GetComponent<Turbo4744>();
        var keeper = root.GetComponent<Turbo4744FuelKeeper>();
        var net = root.GetComponent<Turbo4744NetRig>();
        var hopper = robot.hopperRail.GetComponent<Rigidbody>();
        var rig = root.GetComponent<Turbo4744HopperRig>();
        if (
            Find(root.transform, "Hopper_Geometry").GetComponentInParent<Rigidbody>(true) != hopper
            || keeper.floor.GetComponentInParent<Rigidbody>(true) != body
            || keeper.frontPanel.GetComponentInParent<Rigidbody>(true) != hopper
            || keeper.inlet.GetComponentInParent<Rigidbody>(true) != hopper
            || net.frontLeft.GetComponentInParent<Rigidbody>(true) != hopper
            || net.frontRight.GetComponentInParent<Rigidbody>(true) != hopper
            || net.rearLeft.GetComponentInParent<Rigidbody>(true) != body
            || net.rearRight.GetComponentInParent<Rigidbody>(true) != body
            || robot.indexer.rollers.Any(r =>
                r.visual.GetComponentInParent<Rigidbody>(true) != body
            )
            || !rig
            || rig.floor != keeper.floor
            || rig.frontPanel != keeper.frontPanel
            || robot.storageContact
        )
            throw new InvalidOperationException(
                "The hopper walls, model and front net anchors must follow the physical hopper rail; the indexer entrance stays fixed"
            );
        var solver = robot.roofNet.GetComponent<Turbo4744NetSolver>();
        var foam = root.GetComponent<Turbo4744FuelCompliance>();
        var drive = new SerializedObject(root.GetComponent<DriveController>());
        if (
            !solver.GetComponent<Turbo4744NetEnvironment>()
            || !foam
            || Mathf.Abs(solver.restLengthScale - 1f) > .000001f
            || Mathf.Abs(solver.collisionRadius - .00115f) > .000001f
            || solver.collisionSubstepInterval != 1
            || Mathf.Abs(solver.stretchCompliance - .0000003f) > .00000001f
            || Mathf.Abs(solver.shearCompliance - .00000015f) > .00000001f
            || Mathf.Abs(foam.stiffness - 750) > .01f
            || Mathf.Abs(foam.damping - 8) > .01f
            || Mathf.Abs(foam.maximumCompression - .020f) > .000001f
            || Mathf.Abs(robot.intakeContact.maxTotalForce - 600) > .01f
            || Mathf.Abs(robot.intakeContact.maxTotalTractionForce - 100) > .01f
            || Mathf.Abs(robot.railMaxForce - 350) > .01f
            || Mathf.Abs(robot.railVelocityDamping - 4200) > .01f
            || Mathf.Abs(drive.FindProperty("frictionCoefficient").floatValue - 1.4f) > .000001f
        )
            throw new InvalidOperationException(
                "The native-tested textile and bounded fuel compliance are required"
            );
    }

    private static void ConfigureBellypanContacts(Transform root)
    {
        foreach (var section in BellypanSections)
        {
            var center = (section.min + section.max) * .5f;
            var size = section.max - section.min;
            Box(
                root,
                "Bellypan " + section.name + " contact",
                S(center.x, center.y, center.z),
                new Vector3(size.y, size.z, size.x)
            );
        }
    }

    private static void ValidateBellypanContacts(GameObject root)
    {
        var contacts = root.GetComponentsInChildren<BoxCollider>(true)
            .Where(c => c.name.StartsWith("Bellypan "))
            .ToArray();
        if (contacts.Length != BellypanSections.Length)
            throw new InvalidOperationException(
                "Three solid chassis bellypan contacts are required"
            );
        var body = root.GetComponent<Rigidbody>();
        var bumper = root.GetComponentsInChildren<BoxCollider>(true)
            .First(c => c.name == "Bumper rear contact");
        foreach (var section in BellypanSections)
        {
            var contact = contacts.Single(c => c.name == "Bellypan " + section.name + " contact");
            var center = (section.min + section.max) * .5f;
            var size = section.max - section.min;
            if (
                !contact.enabled
                || !contact.gameObject.activeSelf
                || contact.isTrigger
                || contact.transform.parent != root.transform
                || contact.GetComponentInParent<Rigidbody>(true) != body
                || contact.gameObject.layer != LayerMask.NameToLayer("Robot")
                || contact.includeLayers.value != 0
                || contact.excludeLayers.value != 0
                || contact.sharedMaterial != bumper.sharedMaterial
                || contact.center.sqrMagnitude > .000000000001f
                || Vector3.Distance(
                    contact.transform.localPosition,
                    S(center.x, center.y, center.z)
                ) > .000001f
                || Vector3.Distance(contact.size, new Vector3(size.y, size.z, size.x)) > .000001f
                || Quaternion.Angle(contact.transform.localRotation, Quaternion.identity) > .001f
                || Vector3.Distance(contact.transform.localScale, Vector3.one) > .000001f
            )
                throw new InvalidOperationException(
                    "Invalid CAD bellypan contact: " + contact.name
                );
        }
    }

    private static void ConfigureBumperContacts(Transform root, BoxCollider bounds)
    {
        // CAD outer edges +/- .4442 m across and +/- .39772 m fore/aft;
        // the inner edges are 57.2 mm inward. Keep the feed channel hollow.
        const float thickness = .0572f;
        foreach (int side in new[] { -1, 1 })
        foreach (bool end in new[] { false, true })
        {
            var contact = Box(
                root,
                "Bumper "
                    + (end ? (side > 0 ? "front" : "rear") : (side > 0 ? "right" : "left"))
                    + " contact",
                bounds.center
                    + (
                        end
                            ? Vector3.forward * (bounds.size.z - thickness)
                            : Vector3.right * (bounds.size.x - thickness)
                    ) * (.5f * side),
                end
                    ? new Vector3(bounds.size.x - 2 * thickness, bounds.size.y, thickness)
                    : new Vector3(thickness, bounds.size.y, bounds.size.z)
            );
            contact.gameObject.tag = "Bumpers";
        }
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

    private static GameObject[] BuildCards(bool simple = false)
    {
        var titles = simple
            ? new[] { "Toggle intake", "Hold to shoot", "Stow and clear a jam" }
            : new[] { "Deploy and collect", "Shoot or pass", "Clear a jam" };
        var descriptions = simple
            ? new[]
            {
                "Press Intake to toggle deployment and collection. The intake starts stowed. Shooting opens it temporarily and returns it to your selected state when released.",
                "Hold Auto Shoot to aim and fire at the zone-selected hub or delivery target. Release to stop. Auto Pass and Manual Shoot are also held controls. Pumping is automatic while shooting.",
                "Stow closes the intake and cancels shooting; release the shoot controls before firing again. Robot Special requests three hard pump cycles. Hold Face Down to reverse. All controls follow MoSim rebinding.",
            }
            : new[]
            {
                "Intake opens Turbo's pickup and cancels shooting. Stow retracts the intake and hopper. Shooting pumps the sliding bin toward the stationary indexer.",
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
            card.name = (simple ? "Turbo Simple tutorial " : "Turbo tutorial ") + (i + 1);
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

    private static Sprite ImportSelectorLogo()
    {
        string path = Root + "/Model/selector-logo.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
        // Build snapshots can restore an older file timestamp. Force the
        // prefab import so validation never reads a stale SDK import artifact.
        AssetDatabase.ImportAsset(
            RobotPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
        );
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (!prefab)
            throw new InvalidOperationException("Turbo prefab missing");
        ValidateSimpleVariant(
            AssetDatabase.LoadAssetAtPath<RebuiltRobotMetadataSO>(Root + "/Turbo4744.asset")
        );
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
            || !robot.indexer
            || !robot.indexer.releaseRetention
            || !robot.acceleratorContact
            || robot.indexer.rollers.Length != 9
            || robot.indexer.rollers.Any(r => !r.visual)
            || robot.shooterContact.halfWidth < .156f
            || robot.shooterContact.lowerEntry != robot.indexer
            || !robot.intakeContact
            || robot.storageContact
            || !robot.intakeContact.entryKeeper
            || !robot.intakeContact.entryKeeper.fixedFloor
            || !robot.intakeContact.entryKeeper.frontPanel
            || robot.intakeContact.centerline == null
            || robot.intakeContact.centerline.Length < 2
        )
            throw new InvalidOperationException(
                "Required Turbo mechanism references missing: "
                    + (
                        !robot
                            ? "main robot script"
                            : $"intake={robot.intakeRail}, hopper={robot.hopperRail}, aim={robot.autoAim}, muzzle={robot.muzzle}, feedHold={robot.feedHold}, intakeHold={robot.intakeHold}, shooter={robot.shooterContact}, indexer={robot.indexer}, release={(robot.indexer ? robot.indexer.releaseRetention : null)}, accelerator={robot.acceleratorContact}, intakeContact={robot.intakeContact}, storageContact={robot.storageContact}, keeper={(robot.intakeContact ? robot.intakeContact.entryKeeper : null)}, rollers={(robot.indexer ? robot.indexer.rollers.Length : -1)}, missingRollerVisuals={(robot.indexer ? robot.indexer.rollers.Count(r => !r.visual) : -1)}, width={(robot.shooterContact ? robot.shooterContact.halfWidth : -1)}, lowerEntry={(robot.shooterContact ? robot.shooterContact.lowerEntry : null)}, centerline={(robot.intakeContact ? robot.intakeContact.centerline?.Length : -1)}"
                    )
            );
        var missing = prefab
            .GetComponentsInChildren<Transform>(true)
            .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        if (missing != 0)
            throw new InvalidOperationException("Missing scripts: " + missing);
        ValidateBumperContacts(prefab);
        if (!prefab.GetComponent<Turbo4744BumperContacts>())
            throw new InvalidOperationException(
                "The trench contact handler must be included in the prefab"
            );
        ValidateBellypanContacts(prefab);
        ValidateSwerveContacts(prefab);
        ValidateMovingHopper(prefab);
        if (
            robot.indexer.rollers.Any(r =>
                Vector3.Distance(
                    prefab.transform.InverseTransformPoint(r.visual.position),
                    r.center
                ) > .001f
            )
        )
            throw new InvalidOperationException("Indexer visual and contact axes disagree");
        var sideSupports = prefab
            .GetComponentsInChildren<MeshCollider>(true)
            .Where(c => c.name.StartsWith("SideIndexer"))
            .ToArray();
        if (
            sideSupports.Length != 2
            || sideSupports.Any(c =>
                !c.convex
                || c.GetComponentInParent<Rigidbody>(true) != prefab.GetComponent<Rigidbody>()
            )
        )
            throw new InvalidOperationException("Two passive CAD side supports are required");
        foreach (var support in sideSupports)
        {
            var center = prefab.transform.InverseTransformPoint(
                support.transform.TransformPoint(support.sharedMesh.bounds.center)
            );
            float expectedSide = support.name.Contains("Left") ? -1 : 1;
            if (
                center.x * expectedSide < .20f
                || center.x * expectedSide > .28f
                || center.y < .18f
                || center.y > .23f
                || center.z < .06f
                || center.z > .11f
            )
                throw new InvalidOperationException(
                    "Passive side support does not match evaluated CAD position: " + support.name
                );
        }
        var kickerRig = prefab.GetComponent<Turbo4744KickerRig>();
        if (
            !kickerRig
            || !kickerRig.kicker
            || kickerRig.hopper != robot.hopperRail.transform
            || kickerRig.kicker.parent != robot.hopperRail.transform
            || !robot.intakeRollerVisual.IsChildOf(kickerRig.kicker)
        )
            throw new InvalidOperationException(
                "The retractable kicker must stay attached to the hopper"
            );
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
        var metadata = AssetDatabase.LoadAssetAtPath<RebuiltRobotMetadataSO>(
            Root + "/Turbo4744.asset"
        );
        Set(metadata, "robotImage", ImportSelectorLogo());
        EditorUtility.SetDirty(metadata);
        var releasePack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        Set(releasePack, "modpackVersion", "1.0.1");
        EditorUtility.SetDirty(releasePack);
        AssetDatabase.SaveAssets();
        BuildSimpleVariant();
        Validate();
        Turbo4744NetBurstBuilder.CompileAndEmbed();
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        if (!ModBuildUtility.TryBuildModpack(pack, true, out var path))
            throw new InvalidOperationException("MoSim mod build failed");
        File.WriteAllText(Artifacts + "/mod-build-path.txt", path);
        Turbo4744NetBurstBuilder.CopyRuntime(path);
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
        Turbo4744NetBurstBuilder.VerifyRuntime();
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(PackPath);
        if (!ModBuildUtility.TryBuildModpack(pack, false, out var path))
            throw new InvalidOperationException("Runtime synchronization failed");
        Turbo4744NetBurstBuilder.CopyRuntime(path);
        string dll = Path.Combine(
            path,
            EditorUserBuildSettings.activeBuildTarget.ToString().ToLowerInvariant(),
            "Ninjas4744.dll"
        );
        if (
            !File.ReadAllBytes(dll)
                .SequenceEqual(File.ReadAllBytes(Turbo4744NetBurstBuilder.PlayerAssembly))
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
            ValidateSimpleVariant(metadata);
            var prefab = metadata.RobotPrefab;
            ValidateBumperContacts(prefab);
            ValidateBellypanContacts(prefab);
            ValidateSwerveContacts(prefab);
            ValidateMovingHopper(prefab);
            var robot = prefab.GetComponent<Turbo4744>();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            int missing = prefab
                .GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var references = System
                .Reflection.Assembly.ReflectionOnlyLoad(
                    File.ReadAllBytes(Path.Combine(path, "standalonewindows64", "Ninjas4744.dll"))
                )
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();
            var expectedLogo = AssetDatabase.LoadAssetAtPath<Sprite>(
                Root + "/Model/selector-logo.png"
            );
            bool passed =
                missing == 0
                && loaded.ModpackVersion == pack.ModpackVersion
                && metadata.RobotImage
                && metadata.RobotImage.name == "selector-logo"
                && expectedLogo
                && metadata.RobotImage.rect.size == expectedLogo.rect.size
                && robot
                && robot.intakeRail
                && robot.hopperRail
                && robot.shooterContact
                && robot.indexer
                && robot.indexer.releaseRetention
                && robot.indexer.rollers.Length == 9
                && robot.indexer.rollers.All(r => r.visual)
                && robot.acceleratorContact
                && robot.shooterContact.lowerEntry == robot.indexer
                && prefab.GetComponentsInChildren<Turbo4744ShooterContact>().Length == 3
                && robot.intakeContact
                && robot.intakeContact.entryKeeper
                && robot.intakeContact.entryKeeper.fixedFloor
                && robot.intakeContact.entryKeeper.frontPanel
                && robot.intakeContact.centerline.Length >= 2
                && !robot.storageContact
                && robot.intakeContact.robotBody == robot.intakeRail.GetComponent<Rigidbody>()
                && prefab.GetComponentsInChildren<Turbo4744NetSolver>().Length == 1
                && robot.roofNet.GetComponent<Turbo4744NetSolver>().collisionBroadphase
                && robot.roofNet.GetComponent<Turbo4744NetSolver>().nativeLibrary
                && robot.roofNet.GetComponent<Turbo4744NetSolver>().customAnchors.Count == 68
                && prefab.GetComponent<Turbo4744KickerRig>()
                && robot.shooterContact.robotBody == prefab.GetComponent<Rigidbody>()
                && robot.indexer.robotBody == prefab.GetComponent<Rigidbody>()
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
                version = loaded.ModpackVersion,
                selectorLogo = metadata.RobotImage ? metadata.RobotImage.name : "missing",
                selectorWidth = metadata.RobotImage ? metadata.RobotImage.rect.width : 0,
                selectorHeight = metadata.RobotImage ? metadata.RobotImage.rect.height : 0,
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
            instance.GetComponent<Turbo4744KickerRig>().SetDeployment(deployed ? 1 : 0);
            net.localScale = Vector3.one;
            instance.GetComponent<Turbo4744NetRig>().UpdatePins();
            net.GetComponent<Turbo4744NetSolver>().Rebuild();
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
        // A level side view exposes the small gap between the deployed
        // intake supports and the front bumper that the high view can hide.
        camera.transform.position = new Vector3(1.4f, .29f, .12f);
        camera.transform.LookAt(new Vector3(0, .29f, .12f));
        camera.orthographicSize = .43f;
        Directory.CreateDirectory(Artifacts + "/kicker-motion");
        for (int step = 0; step <= 32; step++)
        {
            // Complete the slide first. The final twelve frames swing only
            // the lower kicker while the hopper and its frame stay still.
            float open = Mathf.Min(step / 20f, 1);
            float deployment = Mathf.Clamp01((step - 20) / 12f);
            intake.localPosition = intakeClosed + Vector3.forward * (.24f * open);
            hopper.localPosition = hopperClosed + Vector3.forward * (.22946f * open);
            instance.GetComponent<Turbo4744KickerRig>().SetDeployment(deployment);
            instance.GetComponent<Turbo4744NetRig>().UpdatePins();
            net.GetComponent<Turbo4744NetSolver>().Rebuild();
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(Artifacts + "/kicker-motion/" + step.ToString("D2") + ".png", png);
            if (step == 0 || step == 32)
                File.WriteAllBytes(
                    Artifacts
                        + "/Turbo4744-kicker-"
                        + (step == 0 ? "closed" : "open")
                        + "-side.png",
                    png
                );
            if (step == 32)
                File.WriteAllBytes(Artifacts + "/Turbo4744-bumper-clearance-side.png", png);
        }
        // Match the supplied shooting footage's front-corner view. Keep the
        // compact lower assembly deployed through the complete pump stroke.
        camera.transform.position = new Vector3(-1.10f, .75f, 1.55f);
        camera.transform.LookAt(new Vector3(0, .26f, .12f));
        camera.orthographicSize = .60f;
        Directory.CreateDirectory(Artifacts + "/kicker-pumping");
        for (int step = 0; step <= 20; step++)
        {
            float pump = step <= 10 ? step / 10f : (20 - step) / 10f;
            float open = Mathf.Lerp(1, 8f / 11, pump);
            intake.localPosition = intakeClosed + Vector3.forward * (.24f * open);
            hopper.localPosition = hopperClosed + Vector3.forward * (.22946f * open);
            instance.GetComponent<Turbo4744KickerRig>().SetDeployment(1);
            instance.GetComponent<Turbo4744NetRig>().UpdatePins();
            net.GetComponent<Turbo4744NetSolver>().Rebuild();
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(Artifacts + "/kicker-pumping/" + step.ToString("D2") + ".png", png);
            if (step == 0)
                File.WriteAllBytes(Artifacts + "/Turbo4744-kicker-reference-view.png", png);
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

    private static void ConfigureIndexer(
        GameObject root,
        GameObject model,
        Turbo4744 robot,
        Rigidbody body,
        Turbo4744FuelKeeper keeper,
        PhysicMaterial plastic
    )
    {
        robot.indexer = root.AddComponent<Turbo4744Indexer>();
        robot.indexer.robotBody = body;
        robot.indexer.keeper = keeper;
        var definitions = new[]
        {
            new Vector4(.33680f, -.00825f, .19344f, .34279f),
            new Vector4(.28802f, -.00825f, .18884f, .34279f),
            new Vector4(.24077f, -.00825f, .17584f, .34279f),
            new Vector4(.19560f, 0, .15685f, .29155f),
            new Vector4(.15029f, .00375f, .13745f, .28780f),
            new Vector4(.15291f, -.00375f, .32162f, .28780f),
            new Vector4(.10796f, -.00375f, .30210f, .28780f),
            new Vector4(.06302f, 0, .28257f, .29155f),
            new Vector4(.07542f, -.00825f, .33873f, .29230f),
        };
        var rollers = new List<Turbo4744Indexer.Roller>();
        for (int i = 0; i < definitions.Length; i++)
        {
            var d = definitions[i];
            bool upper = i >= 5;
            string name =
                (upper ? "IndexerUpper" : "IndexerLower") + (upper ? i - 4 : i + 1).ToString("00");
            var center = S(d.x, d.y, d.z);
            var go = New(name + " fuel support", root.transform, center);
            var cylinder = go.AddComponent<CapsuleCollider>();
            cylinder.direction = 0;
            // Real fuel compresses at the 145 mm CAD inter-deck gap. Keep the
            // official rigid 150 mm fuel unchanged and represent that compliance
            // with a recessed hard core beneath the full-size traction surface.
            cylinder.radius = .0065f;
            cylinder.contactOffset = .001f;
            cylinder.height = d.w * 2;
            cylinder.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            cylinder.sharedMaterial = plastic;
            rollers.Add(
                new Turbo4744Indexer.Roller
                {
                    visual = Find(model.transform, name),
                    center = center,
                    radius = .0125f,
                    halfWidth = d.w,
                    upper = upper,
                }
            );
        }
        robot.indexer.rollers = rollers.ToArray();
        foreach (var side in new[] { "Left", "Right" })
        {
            var support = Find(model.transform, "SideIndexer" + side + "_Support");
            support.GetComponent<Renderer>().enabled = false;
            var surface = support.gameObject.AddComponent<MeshCollider>();
            surface.sharedMesh = support.GetComponent<MeshFilter>().sharedMesh;
            surface.convex = true;
            surface.sharedMaterial = plastic;
            surface.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        }
        // A shallow passive landing supports the lower mouth beneath the accelerator.
        var landing = Box(
            root.transform,
            "Lower shooter entrance floor",
            S(-.005f, -.0131f, .105f),
            new Vector3(.313f, .012f, .31f)
        );
        landing.sharedMaterial = plastic;
        landing.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        // Back the bottom turn physically; traction alone cannot turn a rigid
        // ball through the accelerator bend before it escapes behind the robot.
        var back = Box(
            root.transform,
            "Lower accelerator backing",
            new Vector3(.0131f, .255f, -.193f),
            new Vector3(.313f, .27f, .006f)
        );
        back.sharedMaterial = plastic;
        back.excludeLayers = landing.excludeLayers;
        foreach (int side in new[] { -1, 1 })
        {
            var outer = new Vector3(side * .35f, .32f, .19f);
            var inner = new Vector3(.0131f + side * .1605f, .32f, -.05f);
            var funnel = Box(
                root.transform,
                "Passive side-to-mouth guide " + side,
                (outer + inner) * .5f,
                new Vector3(.008f, .48f, Vector3.Distance(outer, inner) + .012f)
            );
            funnel.transform.localRotation = Quaternion.LookRotation(inner - outer, Vector3.up);
            funnel.sharedMaterial = plastic;
            funnel.excludeLayers = landing.excludeLayers;
            var rear = Box(
                root.transform,
                "Side shoulder rear keeper " + side,
                new Vector3(side * .258f, .35f, -.055f),
                new Vector3(.185f, .4f, .012f)
            );
            rear.sharedMaterial = plastic;
            rear.excludeLayers = landing.excludeLayers;
        }
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
