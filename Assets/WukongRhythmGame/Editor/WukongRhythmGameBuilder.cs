using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.SpatialTracking;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using ByteDance.PICO.XR;
using UnityEngine.XR.Management;

public static class WukongRhythmGameBuilder
{
    private struct UiTransformState
    {
        public Vector3 headRelativeOffset;
        public bool initializePosition;
        public bool followPosition;
    }

    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RootName = "Wukong Rhythm Game";
    private const string GameRoot = "Assets/WukongRhythmGame";
    private const string AudioRoot = GameRoot + "/Audio";
    private const string UiRoot = GameRoot + "/UI";
    private const string UserUiRoot = UiRoot + "/UserProvided";
    private const string GeneratedRoot = GameRoot + "/Generated";
    private const string FontsRoot = GameRoot + "/Fonts";
    // Keep the expanded 1.5x panel fully inside the right side of the headset view.
    private static readonly Vector3 UnifiedHudOffset = new Vector3(1.05f, 0.04f, 1.85f);
    private const float UnifiedHudPixelsToMeters = 0.001425f;

    [MenuItem("Tools/Wukong/Build PICO Rhythm Battle")]
    public static void Build()
    {
        ConfigurePicoBuildSettings();
        EditorSceneManager.SaveOpenScenes();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        GameObject monster = FindSceneObject("LavaElemental_Red");
        if (monster == null)
        {
            monster = FindLavaElementalByAnimator();
        }
        GameObject staff = FindSceneObject("jingubang");
        Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        GameObject playerSpawn = FindSceneObject("PlayerSpawn");

        if (monster == null || staff == null || camera == null)
        {
            Debug.LogError("Rhythm battle requires LavaElemental_Red, jingubang, and a scene Camera.");
            return;
        }

        GameObject previousRoot = FindSceneObject(RootName);
        Dictionary<string, UiTransformState> authoredUiLayout = CaptureSpatialUiLayout(previousRoot);
        if (previousRoot != null)
        {
            if (staff.transform.IsChildOf(previousRoot.transform))
            {
                staff.transform.SetParent(null, true);
            }
            if (camera.transform.IsChildOf(previousRoot.transform))
            {
                camera.transform.SetParent(null, true);
            }
            Object.DestroyImmediate(previousRoot);
        }

        RemoveLegacyCameraUi(camera);

        // Each rebuild used to append another trail/collider to the prefab instance.
        // Revert the instance first so the restored scene starts from one clean staff.
        if (PrefabUtility.IsPartOfPrefabInstance(staff))
        {
            PrefabUtility.RevertPrefabInstance(staff, InteractionMode.AutomatedAction);
            staff.name = "jingubang";
        }

        foreach (Transform oldAnchor in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (oldAnchor != null
                && oldAnchor.gameObject != staff
                && oldAnchor.name == "PICO Right Hand Anchor"
                && (staff == null || !staff.transform.IsChildOf(oldAnchor)))
            {
                Object.DestroyImmediate(oldAnchor.gameObject);
            }
        }

        EnsureFolder(GameRoot);
        EnsureFolder(GeneratedRoot);
        // The source previews include a charcoal presentation background. Build
        // transparent glass overlays so the real scene remains visible through the
        // panel at runtime.
        Texture2D mainPanel = LoadGlassTexture("MainPanel_Wide.png", "MainPanel_Wide_Transparent.png");
        Texture2D perfectCard = LoadGlassTexture("Rating_Perfect.png", "Rating_Perfect_Transparent.png");
        Texture2D goodCard = LoadGlassTexture("Rating_Good.png", "Rating_Good_Transparent.png");
        Texture2D missCard = LoadGlassTexture("Rating_Miss.png", "Rating_Miss_Transparent.png");

        WukongSongLibrary songLibrary = WukongBeatmapGenerator.EnsureDefaultLibrary();
        if (mainPanel == null || perfectCard == null || goodCard == null || missCard == null || songLibrary == null || songLibrary.Count == 0)
        {
            Debug.LogError("Missing Wukong rhythm UI artwork or song library.");
            return;
        }

        Material trailMaterial = CreateOrUpdateMaterial(
            GeneratedRoot + "/GoldenTrail.mat",
            "Universal Render Pipeline/Unlit",
            new Color(1f, 0.23f, 0.025f, 1f));
        Material particleMaterial = CreateOrUpdateMaterial(
            GeneratedRoot + "/LavaParticles.mat",
            "Universal Render Pipeline/Particles/Unlit",
            new Color(1f, 0.15f, 0.01f, 1f));

        GameObject root = new GameObject(RootName);
        GameObject rig = BuildPlayerRig(root.transform, camera, playerSpawn, monster);

        WukongStaffController staffController = BuildStaff(root.transform, rig.transform, camera, staff, trailMaterial);
        Animator animator = monster.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        Bounds monsterBounds = CalculateWorldBounds(monster);
        Material rockMaterial = FindLavaMaterial(monster);
        Transform throwPoint = CreateThrowPoint(root.transform, monsterBounds, camera.transform.position);
        BuildBattleLighting(root.transform, monsterBounds, camera.transform.position);
        GameObject rockTemplate = BuildRockTemplate(root.transform, rockMaterial, trailMaterial);

        WukongRhythmGame game = root.AddComponent<WukongRhythmGame>();
        game.rockShatterSound = LoadAudio("Rock_Shatter.mp3");
        game.fireImpactSound = LoadAudio("Fire_Impact.mp3");
        game.staffImpactSound = LoadAudio("Staff_Impact.mp3");
        game.victorySound = LoadAudio("Victory_Chime.mp3");
        game.rockMaterial = rockMaterial;
        game.particleMaterial = particleMaterial;
        game.rockTemplate = rockTemplate;
        game.monsterAnimator = animator;
        game.monsterRoot = monster.transform;
        game.monsterThrowPoint = throwPoint;
        game.playerCamera = camera;
        game.staff = staffController;
        game.hitWindow = 0.38f;
        game.musicVolume = 1f;
        game.effectsVolume = 0.28f;
        game.songLibrary = songLibrary;

        BuildHud(root.transform, camera, mainPanel, perfectCard, goodCard, missCard, game);
        RestoreSpatialUiLayout(root, authoredUiLayout);
        rockTemplate.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = root;
        Debug.Log("PICO-ready Wukong rhythm battle built in SampleScene.");
    }

    [MenuItem("Tools/Wukong/Build Android APK for PICO")]
    public static void BuildAndroidApk()
    {
        ConfigurePicoBuildSettings();
        EditorSceneManager.SaveOpenScenes();
        string outputDirectory = "Builds";
        Directory.CreateDirectory(outputDirectory);
        string outputPath = outputDirectory + "/WukongRhythmVR.apk";
        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(BuildTarget.Android);
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, BuildTarget.Android))
        {
            Debug.LogError("Could not switch Unity to the Android build target.");
            return;
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("PICO Android build failed: " + report.summary.result);
            return;
        }

        Debug.Log("PICO Android APK built at " + Path.GetFullPath(outputPath));
    }

    private static void ConfigurePicoBuildSettings()
    {
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            throw new System.InvalidOperationException("Could not switch Unity to the Android build target for PICO XR.");
        }

        PlayerSettings.companyName = "PICO Rhythm Studio";
        PlayerSettings.productName = "Wukong Rhythm VR";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.UnityTechnologies.com.unity.template.urpblank");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        // PICO targets are ARM64-only, and Unity requires IL2CPP for Android ARM64.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        // PICO's native platform bridge expects UnityPlayerActivity's class loader.
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

        XRGeneralSettingsPerBuildTarget perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>("Assets/XRGeneralSettingsPerBuildTarget.asset");
        XRGeneralSettings generalSettings = perBuildTarget != null
            ? perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android)
            : null;
        if (perBuildTarget == null || generalSettings == null || generalSettings.Manager == null)
        {
            throw new System.InvalidOperationException("PICO XR loader settings are missing from Assets/XRGeneralSettingsPerBuildTarget.asset.");
        }

        bool picoLoaderAssigned = generalSettings.Manager.activeLoaders.Any(loader => loader is PXR_Loader);
        if (!picoLoaderAssigned)
        {
            picoLoaderAssigned = XRPackageMetadataStore.AssignLoader(
                generalSettings.Manager,
                "ByteDance.PICO.XR.PXR_Loader",
                BuildTargetGroup.Android);
        }
        if (!picoLoaderAssigned)
        {
            throw new System.InvalidOperationException("PICO XR loader is not assigned to Android.");
        }

        PXR_Settings picoSettings = AssetDatabase.LoadAssetAtPath<PXR_Settings>("Assets/XR/Settings/PXR_Settings.asset");
        if (picoSettings == null)
        {
            throw new System.InvalidOperationException("PICO XR settings asset is missing from Assets/XR/Settings/PXR_Settings.asset.");
        }
        picoSettings.appMode = PXR_Settings.AppMode.XR;
        picoSettings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
        picoSettings.optimizeBufferDiscards = true;
        EditorUtility.SetDirty(picoSettings);
        EditorUtility.SetDirty(generalSettings.Manager);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(perBuildTarget);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Wukong/Capture Rhythm Battle Preview")]
    public static void CapturePreview()
    {
        Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            Debug.LogError("No camera found for preview capture.");
            return;
        }

        const int width = 1536;
        const int height = 1024;
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        Texture2D preview = new Texture2D(width, height, TextureFormat.RGB24, false);
        preview.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        preview.Apply();
        File.WriteAllBytes(GeneratedRoot + "/RhythmBattlePreview.png", preview.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);
        Object.DestroyImmediate(preview);
        AssetDatabase.ImportAsset(GeneratedRoot + "/RhythmBattlePreview.png", ImportAssetOptions.ForceUpdate);
        Debug.Log("Rhythm battle preview captured.");
    }

    private static GameObject BuildPlayerRig(Transform parent, Camera camera, GameObject playerSpawn, GameObject monster)
    {
        GameObject rig = new GameObject("PICO XR Player Rig");
        rig.transform.SetParent(parent);
        rig.transform.position = playerSpawn != null ? playerSpawn.transform.position : camera.transform.position;

        Bounds monsterBounds = CalculateWorldBounds(monster);
        Vector3 lookDirection = monsterBounds.center - rig.transform.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude > 0.01f)
        {
            rig.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        XROrigin origin = rig.AddComponent<XROrigin>();
        rig.AddComponent<PXR_Manager>();

        GameObject cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(rig.transform, false);

        camera.gameObject.name = "PICO VR Camera";
        camera.gameObject.tag = "MainCamera";
        camera.transform.SetParent(cameraOffset.transform, false);
        camera.transform.localPosition = Vector3.zero;
        camera.transform.localRotation = Quaternion.identity;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 250f);
        camera.fieldOfView = 72f;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;

        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i].gameObject != camera.gameObject)
            {
                Object.DestroyImmediate(listeners[i]);
            }
        }
        if (camera.GetComponent<AudioListener>() == null)
        {
            camera.gameObject.AddComponent<AudioListener>();
        }

        TrackedPoseDriver poseDriver = camera.GetComponent<TrackedPoseDriver>();
        if (poseDriver == null)
        {
            poseDriver = camera.gameObject.AddComponent<TrackedPoseDriver>();
        }
        poseDriver.SetPoseSource(
            TrackedPoseDriver.DeviceType.GenericXRDevice,
            TrackedPoseDriver.TrackedPose.Center);
        poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        origin.Origin = rig;
        origin.CameraFloorOffsetObject = cameraOffset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        origin.Camera = camera;
        return rig;
    }

    private static WukongStaffController BuildStaff(
        Transform gameRoot,
        Transform rig,
        Camera camera,
        GameObject staff,
        Material trailMaterial)
    {
        Transform handAnchor = new GameObject("PICO Right Hand Anchor").transform;
        handAnchor.SetParent(rig, false);
        handAnchor.localPosition = new Vector3(0.38f, 1.15f, 0.48f);
        handAnchor.localRotation = Quaternion.Euler(12f, -8f, -18f);

        staff.transform.SetParent(handAnchor, false);
        staff.transform.localPosition = Vector3.zero;
        staff.transform.localRotation = Quaternion.identity;
        staff.transform.localScale = Vector3.Max(staff.transform.localScale, Vector3.one * 0.0001f);
        staff.SetActive(true);

        Bounds worldBounds = CalculateWorldBounds(staff);
        float currentLength = Mathf.Max(worldBounds.size.x, worldBounds.size.y, worldBounds.size.z);
        if (currentLength > 0.001f)
        {
            // Keep the controller-led staff fully inside the first-person view
            // while leaving enough reach for comfortable, low-amplitude swings.
            float factor = Mathf.Clamp(0.58f / currentLength, 0.08f, 8f);
            staff.transform.localScale *= factor;
        }

        foreach (Collider collider in staff.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        Bounds localBounds = CalculateLocalBounds(staff.transform);
        int direction = LargestAxis(localBounds.size);
        Vector3 staffAxis = direction == 0 ? Vector3.right
            : direction == 1 ? Vector3.up
            : Vector3.forward;
        float axisExtent = direction == 0 ? localBounds.extents.x
            : direction == 1 ? localBounds.extents.y
            : localBounds.extents.z;
        // Point the staff away from the grip so the right controller controls its
        // near end and the striking end stays in front of the player.
        // The imported jingubang mesh is authored with its striking end on the
        // negative local axis. Negate the vertical component here so the grip
        // stays at the controller while the visible staff extends down/right
        // into the player's view in the editor and PICO simulator.
        Vector3 heldDirection = new Vector3(0.34f, 0.52f, 0.79f).normalized;
        staff.transform.localRotation = Quaternion.FromToRotation(staffAxis, heldDirection);

        // The controller origin is the grip point: attach it to one physical end
        // of the staff instead of leaving the hand in the middle of the mesh.
        Vector3 gripPoint = localBounds.center - staffAxis * axisExtent;
        Vector3 gripOffsetInAnchor = staff.transform.localRotation
            * Vector3.Scale(gripPoint, staff.transform.localScale);
        staff.transform.localPosition = -gripOffsetInAnchor;

        foreach (Renderer renderer in staff.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.gameObject.activeSelf)
            {
                renderer.gameObject.SetActive(true);
            }
            renderer.enabled = true;
        }

        CapsuleCollider capsule = staff.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = staff.AddComponent<CapsuleCollider>();
        }
        float length = direction == 0 ? localBounds.size.x : direction == 1 ? localBounds.size.y : localBounds.size.z;
        float widthA = direction == 0 ? localBounds.size.y : localBounds.size.x;
        float widthB = direction == 2 ? localBounds.size.y : localBounds.size.z;
        capsule.center = localBounds.center;
        capsule.direction = direction;
        capsule.height = Mathf.Max(0.35f, length * 0.96f);
        capsule.radius = Mathf.Max(0.035f, Mathf.Min(widthA, widthB) * 0.48f);
        capsule.isTrigger = true;
        capsule.enabled = true;

        // Hits are evaluated by WukongStaffController's swept collider checks.
        // A Rigidbody on this child causes the physics transform to preserve its
        // world pose while the controller anchor moves, visually detaching the
        // staff from the hand in the PICO simulator.
        Rigidbody body = staff.GetComponent<Rigidbody>();
        if (body != null)
        {
            Object.DestroyImmediate(body);
        }

        Transform trailTip = new GameObject("Golden Swing Trail").transform;
        trailTip.SetParent(staff.transform, false);
        Vector3 tip = localBounds.center;
        if (direction == 0) tip.x += localBounds.extents.x;
        if (direction == 1) tip.y += localBounds.extents.y;
        if (direction == 2) tip.z += localBounds.extents.z;
        trailTip.localPosition = tip;
        TrailRenderer trail = trailTip.gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.17f;
        trail.minVertexDistance = 0.02f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.085f, 1f, 0f);
        trail.numCornerVertices = 3;
        trail.numCapVertices = 3;
        trail.sharedMaterial = trailMaterial;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.78f, 0.18f), 0f),
                new GradientColorKey(new Color(1f, 0.12f, 0.01f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        trail.colorGradient = gradient;
        trail.shadowCastingMode = ShadowCastingMode.Off;

        WukongStaffController controller = staff.GetComponent<WukongStaffController>();
        if (controller == null)
        {
            controller = staff.AddComponent<WukongStaffController>();
        }
        controller.trackingOrigin = rig;
        controller.handAnchor = handAnchor;
        controller.playerCamera = camera;
        controller.staffCollider = capsule;
        controller.swingTrail = trail;
        controller.whooshSound = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioRoot + "/Staff_Whoosh.wav");
        controller.whooshVolume = 0.2f;
        controller.minimumStrikeSpeed = 0.58f;
        controller.contactForgiveness = 0.16f;
        controller.maximumThrowDistance = 4.5f;
        controller.outboundDuration = 0.35f;
        controller.returnDuration = 0.35f;
        controller.totalSpinDegrees = 1440f;
        controller.localLongAxis = staffAxis;
        return controller;
    }

    private static GameObject BuildRockTemplate(Transform parent, Material rockMaterial, Material trailMaterial)
    {
        GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = "Lava Rhythm Rock Template";
        rock.transform.SetParent(parent);
        rock.transform.localScale = Vector3.one * 0.62f;
        Renderer renderer = rock.GetComponent<Renderer>();
        renderer.sharedMaterial = rockMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        rock.AddComponent<WukongBeatRock>();

        TrailRenderer trail = rock.AddComponent<TrailRenderer>();
        trail.time = 0.34f;
        trail.minVertexDistance = 0.04f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.18f, 1f, 0f);
        trail.sharedMaterial = trailMaterial;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.18f, 0.015f), 0f),
                new GradientColorKey(new Color(0.25f, 0.015f, 0f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        trail.colorGradient = gradient;
        return rock;
    }

    private static void BuildBattleLighting(Transform parent, Bounds monsterBounds, Vector3 playerPosition)
    {
        GameObject bossLightObject = new GameObject("Lava Boss Key Light");
        bossLightObject.transform.SetParent(parent);
        bossLightObject.transform.position = monsterBounds.center + Vector3.up * 0.5f;
        Light bossLight = bossLightObject.AddComponent<Light>();
        bossLight.type = LightType.Point;
        bossLight.color = new Color(1f, 0.075f, 0.015f);
        bossLight.intensity = 850f;
        bossLight.range = 16f;
        bossLight.shadows = LightShadows.Soft;
        bossLight.shadowStrength = 0.52f;

        GameObject playerRimObject = new GameObject("Player Moon Rim Light");
        playerRimObject.transform.SetParent(parent);
        playerRimObject.transform.position = playerPosition + Vector3.up * 3f;
        Light playerRim = playerRimObject.AddComponent<Light>();
        playerRim.type = LightType.Point;
        playerRim.color = new Color(0.18f, 0.34f, 0.72f);
        playerRim.intensity = 220f;
        playerRim.range = 10f;
        playerRim.shadows = LightShadows.None;
    }

    private static Transform CreateThrowPoint(Transform parent, Bounds monsterBounds, Vector3 playerPosition)
    {
        Transform throwPoint = new GameObject("Monster Throw Point").transform;
        throwPoint.SetParent(parent);
        Vector3 direction = (playerPosition - monsterBounds.center).normalized;
        throwPoint.position = monsterBounds.center + direction * Mathf.Max(1.2f, monsterBounds.extents.magnitude * 0.22f) + Vector3.up * 0.35f;
        return throwPoint;
    }

    private static void BuildHud(
        Transform parent,
        Camera camera,
        Texture2D mainPanel,
        Texture2D perfectCard,
        Texture2D goodCard,
        Texture2D missCard,
        WukongRhythmGame game)
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontsRoot + "/NotoSansSC-UI.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        Color primaryText = new Color(0.92f, 0.98f, 1f, 1f);
        Color secondaryText = new Color(0.72f, 0.86f, 0.92f, 0.84f);
        Color accentText = new Color(0.54f, 0.97f, 1f, 1f);
        GameObject spatialRoot = CreateUiObject("PICO Spatial UI", parent);
        GameObject panel = CreateSpatialPanel("Unified Rhythm HUD", spatialRoot.transform, camera,
            UnifiedHudOffset, new Vector2(1120f, 1493f), UnifiedHudPixelsToMeters, 61);
        RawImage panelArtwork = CreateRawImage("Wide Glass Panel", panel.transform, mainPanel, Color.white);
        Stretch(panelArtwork.rectTransform);

        WukongRhythmHud hud = panel.AddComponent<WukongRhythmHud>();
        GameObject songSelectContent = CreateUiObject("Song Select Content", panel.transform);
        GameObject gameplayContent = CreateUiObject("Gameplay Content", panel.transform);
        GameObject resultsContent = CreateUiObject("Results Content", panel.transform);
        Stretch(songSelectContent.GetComponent<RectTransform>());
        Stretch(gameplayContent.GetComponent<RectTransform>());
        Stretch(resultsContent.GetComponent<RectTransform>());

        Text libraryLabel = CreateText("Music Library Label", songSelectContent.transform, font, 22, TextAnchor.MiddleCenter, secondaryText);
        libraryLabel.text = "MUSIC LIBRARY";
        SetRect(libraryLabel.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 615f), new Vector2(900f, 44f));
        Text selectTitle = CreateText("Song Select Title", songSelectContent.transform, font, 54, TextAnchor.MiddleCenter, primaryText);
        SetRect(selectTitle.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 550f), new Vector2(940f, 82f));
        Text selectSubtitle = CreateText("Song Select Subtitle", songSelectContent.transform, font, 22, TextAnchor.MiddleCenter, secondaryText);
        selectSubtitle.text = "CHOOSE A TRACK TO BEGIN";
        SetRect(selectSubtitle.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 492f), new Vector2(900f, 44f));

        RawImage selectedTrackCard = CreateLiquidGlassCard("Selected Track Glass Card", songSelectContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.9f));
        SetRect(selectedTrackCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 80f), new Vector2(950f, 210f));
        Text[] songRows = new Text[5];
        for (int i = 0; i < songRows.Length; i++)
        {
            Text row = CreateText("Song Row " + i, songSelectContent.transform, font, 30, TextAnchor.MiddleLeft, secondaryText);
            SetRect(row.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 320f - i * 120f), new Vector2(850f, 82f));
            songRows[i] = row;
        }
        Text songDetails = CreateText("Song Details", songSelectContent.transform, font, 27, TextAnchor.MiddleCenter, secondaryText);
        SetRect(songDetails.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -355f), new Vector2(900f, 120f));
        RawImage selectControlsCard = CreateLiquidGlassCard("Song Select Controls Card", songSelectContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.72f));
        SetRect(selectControlsCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -585f), new Vector2(920f, 150f));
        Text songHint = CreateText("Song Select Hint", songSelectContent.transform, font, 25, TextAnchor.MiddleCenter, accentText);
        SetRect(songHint.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -585f), new Vector2(850f, 100f));

        Text nowPlaying = CreateText("Now Playing Label", gameplayContent.transform, font, 21, TextAnchor.MiddleCenter, secondaryText);
        nowPlaying.text = "NOW PLAYING";
        SetRect(nowPlaying.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 625f), new Vector2(900f, 42f));
        Text songName = CreateText("Selected Song Name", gameplayContent.transform, font, 36, TextAnchor.MiddleCenter, primaryText);
        SetRect(songName.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 570f), new Vector2(930f, 64f));
        Text phase = CreateText("Phase Value", gameplayContent.transform, font, 23, TextAnchor.MiddleCenter, secondaryText);
        SetRect(phase.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 520f), new Vector2(850f, 44f));

        RawImage ratingIdleCard = CreateLiquidGlassCard("Rating Ready Glass Card", gameplayContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.72f));
        SetRect(ratingIdleCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 365f), new Vector2(920f, 220f));
        Text ratingIdleLabel = CreateText("Rating Ready Label", gameplayContent.transform, font, 34, TextAnchor.MiddleCenter, new Color(0.7f, 0.9f, 0.94f, 0.7f));
        SetRect(ratingIdleLabel.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 365f), new Vector2(820f, 100f));

        GameObject ratingState = CreateUiObject("Rating State", gameplayContent.transform);
        Stretch(ratingState.GetComponent<RectTransform>());
        CanvasGroup ratingGroup = ratingState.AddComponent<CanvasGroup>();
        ratingGroup.alpha = 0f;
        RawImage perfect = CreateLiquidGlassCard("Perfect Rating Card", ratingState.transform, perfectCard, Color.white);
        RawImage good = CreateLiquidGlassCard("Good Rating Card", ratingState.transform, goodCard, Color.white);
        RawImage miss = CreateLiquidGlassCard("Miss Rating Card", ratingState.transform, missCard, Color.white);
        SetRect(perfect.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 365f), new Vector2(920f, 220f));
        SetRect(good.rectTransform, perfect.rectTransform.anchorMin, perfect.rectTransform.anchorMax, perfect.rectTransform.pivot, perfect.rectTransform.anchoredPosition, perfect.rectTransform.sizeDelta);
        SetRect(miss.rectTransform, perfect.rectTransform.anchorMin, perfect.rectTransform.anchorMax, perfect.rectTransform.pivot, perfect.rectTransform.anchoredPosition, perfect.rectTransform.sizeDelta);
        Text ratingLabel = CreateText("Rating Label", ratingState.transform, font, 62, TextAnchor.MiddleCenter, primaryText);
        SetRect(ratingLabel.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 365f), new Vector2(840f, 130f));

        Text scoreCaption = CreateText("Score Caption", gameplayContent.transform, font, 21, TextAnchor.MiddleCenter, secondaryText);
        scoreCaption.text = "SCORE";
        SetRect(scoreCaption.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 224f), new Vector2(840f, 42f));
        Text score = CreateText("Score Value", gameplayContent.transform, font, 76, TextAnchor.MiddleCenter, primaryText);
        SetRect(score.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 150f), new Vector2(900f, 105f));
        Text comboCaption = CreateText("Combo Caption", gameplayContent.transform, font, 19, TextAnchor.MiddleCenter, secondaryText);
        comboCaption.text = "COMBO";
        SetRect(comboCaption.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-235f, 52f), new Vector2(360f, 36f));
        Text accuracyCaption = CreateText("Accuracy Caption", gameplayContent.transform, font, 19, TextAnchor.MiddleCenter, secondaryText);
        accuracyCaption.text = "ACCURACY";
        SetRect(accuracyCaption.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(235f, 52f), new Vector2(360f, 36f));
        Text combo = CreateText("Combo Value", gameplayContent.transform, font, 52, TextAnchor.MiddleCenter, accentText);
        SetRect(combo.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-235f, 2f), new Vector2(380f, 72f));
        Text accuracy = CreateText("Accuracy Value", gameplayContent.transform, font, 48, TextAnchor.MiddleCenter, primaryText);
        SetRect(accuracy.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(235f, 2f), new Vector2(380f, 72f));

        RawImage rhythmCard = CreateLiquidGlassCard("Rhythm Glass Card", gameplayContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.78f));
        SetRect(rhythmCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -245f), new Vector2(920f, 245f));
        Text rhythmCaption = CreateText("Rhythm Caption", gameplayContent.transform, font, 21, TextAnchor.MiddleLeft, secondaryText);
        rhythmCaption.text = "RHYTHM";
        SetRect(rhythmCaption.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-320f, -180f), new Vector2(220f, 42f));
        Text bpm = CreateText("BPM Value", gameplayContent.transform, font, 23, TextAnchor.MiddleRight, accentText);
        SetRect(bpm.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(300f, -180f), new Vector2(260f, 42f));
        Text beat = CreateText("Beat Value", gameplayContent.transform, font, 27, TextAnchor.MiddleCenter, primaryText);
        SetRect(beat.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -235f), new Vector2(800f, 48f));
        Image beatTrack = CreateImage("Beat Progress Track", gameplayContent.transform, new Color(0.62f, 0.82f, 0.9f, 0.2f));
        SetRect(beatTrack.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -295f), new Vector2(760f, 16f));
        Image beatProgress = CreateImage("Beat Progress", gameplayContent.transform, new Color(0.44f, 0.94f, 1f, 0.95f));
        beatProgress.type = Image.Type.Filled;
        beatProgress.fillMethod = Image.FillMethod.Horizontal;
        beatProgress.fillOrigin = 0;
        SetRect(beatProgress.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -295f), new Vector2(760f, 16f));
        Image[] beatMarkers = new Image[4];
        for (int i = 0; i < beatMarkers.Length; i++)
        {
            Image marker = CreateImage("Beat Marker " + (i + 1), gameplayContent.transform, new Color(0.66f, 0.82f, 0.9f, 0.24f));
            SetRect(marker.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-180f + i * 120f, -345f), new Vector2(24f, 24f));
            beatMarkers[i] = marker;
        }

        RawImage operationCard = CreateLiquidGlassCard("Operation Glass Card", gameplayContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.74f));
        SetRect(operationCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -575f), new Vector2(920f, 185f));
        Text operationCaption = CreateText("Operation Caption", gameplayContent.transform, font, 19, TextAnchor.MiddleCenter, secondaryText);
        operationCaption.text = "CONTROLS";
        SetRect(operationCaption.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -520f), new Vector2(820f, 34f));
        Text operation = CreateText("Operation Hint", gameplayContent.transform, font, 21, TextAnchor.MiddleCenter, accentText);
        SetRect(operation.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -596f), new Vector2(870f, 92f));

        Text resultEyebrow = CreateText("Result Eyebrow", resultsContent.transform, font, 22, TextAnchor.MiddleCenter, secondaryText);
        resultEyebrow.text = "SESSION RESULTS";
        SetRect(resultEyebrow.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 610f), new Vector2(900f, 44f));
        Text resultTitle = CreateText("Result Title", resultsContent.transform, font, 52, TextAnchor.MiddleCenter, primaryText);
        SetRect(resultTitle.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 540f), new Vector2(920f, 80f));
        RawImage resultScoreCard = CreateLiquidGlassCard("Result Score Glass Card", resultsContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.84f));
        SetRect(resultScoreCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 320f), new Vector2(920f, 235f));
        Text resultScore = CreateText("Result Score", resultsContent.transform, font, 64, TextAnchor.MiddleCenter, primaryText);
        SetRect(resultScore.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 320f), new Vector2(850f, 100f));
        RawImage resultStatsCard = CreateLiquidGlassCard("Result Stats Glass Card", resultsContent.transform, goodCard, new Color(1f, 1f, 1f, 0.76f));
        SetRect(resultStatsCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 55f), new Vector2(920f, 230f));
        Text resultAccuracy = CreateText("Result Accuracy", resultsContent.transform, font, 38, TextAnchor.MiddleCenter, primaryText);
        SetRect(resultAccuracy.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 95f), new Vector2(860f, 64f));
        Text resultCombo = CreateText("Result Combo", resultsContent.transform, font, 38, TextAnchor.MiddleCenter, accentText);
        SetRect(resultCombo.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, 12f), new Vector2(860f, 64f));
        RawImage resultBreakdownCard = CreateLiquidGlassCard("Result Breakdown Glass Card", resultsContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.68f));
        SetRect(resultBreakdownCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -220f), new Vector2(920f, 190f));
        Text resultBreakdown = CreateText("Result Breakdown", resultsContent.transform, font, 27, TextAnchor.MiddleCenter, secondaryText);
        SetRect(resultBreakdown.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -220f), new Vector2(860f, 82f));
        RawImage resultControlsCard = CreateLiquidGlassCard("Result Controls Glass Card", resultsContent.transform, perfectCard, new Color(1f, 1f, 1f, 0.7f));
        SetRect(resultControlsCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -560f), new Vector2(920f, 160f));
        Text resultHint = CreateText("Result Hint", resultsContent.transform, font, 26, TextAnchor.MiddleCenter, accentText);
        SetRect(resultHint.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(0f, -560f), new Vector2(860f, 100f));

        songSelectContent.SetActive(true);
        gameplayContent.SetActive(false);
        resultsContent.SetActive(false);

        hud.songSelectContent = songSelectContent;
        hud.gameplayContent = gameplayContent;
        hud.resultsContent = resultsContent;
        hud.musicLibraryLabel = libraryLabel;
        hud.songSelectTitle = selectTitle;
        hud.songSelectSubtitle = selectSubtitle;
        hud.songRows = songRows;
        hud.songDetails = songDetails;
        hud.songSelectHint = songHint;
        hud.nowPlayingLabel = nowPlaying;
        hud.scoreCaption = scoreCaption;
        hud.comboCaption = comboCaption;
        hud.accuracyCaption = accuracyCaption;
        hud.rhythmCaption = rhythmCaption;
        hud.operationCaption = operationCaption;
        hud.songNameValue = songName;
        hud.phaseValue = phase;
        hud.bpmValue = bpm;
        hud.beatValue = beat;
        hud.beatProgress = beatProgress;
        hud.beatMarkers = beatMarkers;
        hud.scoreValue = score;
        hud.comboValue = combo;
        hud.accuracyValue = accuracy;
        hud.operationHint = operation;
        hud.ratingIdleLabel = ratingIdleLabel;
        hud.perfectCard = perfect;
        hud.goodCard = good;
        hud.missCard = miss;
        hud.ratingLabel = ratingLabel;
        hud.resultEyebrow = resultEyebrow;
        hud.resultTitle = resultTitle;
        hud.resultScore = resultScore;
        hud.resultAccuracy = resultAccuracy;
        hud.resultCombo = resultCombo;
        hud.resultBreakdown = resultBreakdown;
        hud.resultHint = resultHint;
        game.hud = hud;
    }

    private static Texture2D LoadGlassTexture(string sourceFileName, string generatedFileName)
    {
        string sourcePath = UserUiRoot + "/" + sourceFileName;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) == null)
        {
            Debug.LogError("Missing approved liquid-glass source at " + sourcePath);
            return null;
        }

        return BuildGlassOverlay(sourcePath, GeneratedRoot + "/" + generatedFileName);
    }

    private static Texture2D BuildGlassOverlay(string sourcePath, string outputPath)
    {
        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
        {
            return null;
        }

        bool originalReadable = sourceImporter.isReadable;
        TextureImporterCompression originalCompression = sourceImporter.textureCompression;
        int originalMaxSize = sourceImporter.maxTextureSize;
        TextureImporterNPOTScale originalNpotScale = sourceImporter.npotScale;
        sourceImporter.isReadable = true;
        sourceImporter.textureCompression = TextureImporterCompression.Uncompressed;
        sourceImporter.maxTextureSize = 2048;
        sourceImporter.npotScale = TextureImporterNPOTScale.None;
        sourceImporter.SaveAndReimport();

        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (source == null)
        {
            return null;
        }

        Color[] pixels = source.GetPixels();
        Color background = (source.GetPixel(0, 0)
            + source.GetPixel(source.width - 1, 0)
            + source.GetPixel(0, source.height - 1)
            + source.GetPixel(source.width - 1, source.height - 1)) * 0.25f;
        float backgroundLuminance = Mathf.Max(background.r, Mathf.Max(background.g, background.b));
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            float distance = Mathf.Max(
                Mathf.Abs(pixel.r - background.r),
                Mathf.Max(Mathf.Abs(pixel.g - background.g), Mathf.Abs(pixel.b - background.b)));
            float luminance = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
            float presence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.025f, 0.2f, distance));
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.9f, luminance));
            float alpha = presence * Mathf.Lerp(0.16f, 0.82f, edge);
            if (luminance < backgroundLuminance + 0.065f && distance < 0.12f)
            {
                alpha = 0f;
            }
            pixel.a = alpha;
            pixels[i] = pixel;
        }

        Texture2D output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
        output.SetPixels(pixels);
        output.Apply(false, false);
        File.WriteAllBytes(outputPath, output.EncodeToPNG());
        Object.DestroyImmediate(output);

        sourceImporter.isReadable = originalReadable;
        sourceImporter.textureCompression = originalCompression;
        sourceImporter.maxTextureSize = originalMaxSize;
        sourceImporter.npotScale = originalNpotScale;
        sourceImporter.SaveAndReimport();

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        TextureImporter outputImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        outputImporter.alphaIsTransparency = true;
        outputImporter.textureCompression = TextureImporterCompression.CompressedHQ;
        outputImporter.maxTextureSize = 2048;
        outputImporter.npotScale = TextureImporterNPOTScale.None;
        outputImporter.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    private static Texture2D BuildTransparentOverlay(string sourcePath, string outputPath)
    {
        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
        {
            Debug.LogError("Missing generated HUD source at " + sourcePath);
            return null;
        }

        sourceImporter.isReadable = true;
        sourceImporter.textureCompression = TextureImporterCompression.Uncompressed;
        sourceImporter.alphaIsTransparency = false;
        sourceImporter.SaveAndReimport();
        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            float brightness = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
            int pixelX = i % source.width;
            int pixelY = i / source.width;
            float normalizedX = (pixelX + 0.5f) / source.width;
            float normalizedY = (pixelY + 0.5f) / source.height;
            bool centralOverlay = normalizedX > 0.18f && normalizedX < 0.82f
                && normalizedY > 0.19f && normalizedY < 0.92f;
            float alpha = Mathf.Clamp01((brightness - 0.018f) / 0.27f);
            alpha = Mathf.Pow(alpha, 0.8f) * 0.93f;
            if (centralOverlay)
            {
                alpha = 0f;
            }
            pixel.a = alpha;
            pixels[i] = pixel;
        }

        Texture2D output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
        output.SetPixels(pixels);
        output.Apply(false, false);
        File.WriteAllBytes(outputPath, output.EncodeToPNG());
        Object.DestroyImmediate(output);
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        TextureImporter outputImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        outputImporter.alphaIsTransparency = true;
        outputImporter.textureCompression = TextureImporterCompression.CompressedHQ;
        outputImporter.maxTextureSize = 2048;
        outputImporter.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    private static Texture2D LoadUiTexture(string userFileName, string fallbackFileName)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(UserUiRoot + "/" + userFileName);
        if (texture != null)
        {
            return texture;
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(UiRoot + "/" + fallbackFileName);
    }

    private static Material FindLavaMaterial(GameObject monster)
    {
        Renderer[] renderers = monster.GetComponentsInChildren<Renderer>(true);
        Material named = renderers
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(material => material != null && material.name.IndexOf("LavaElemental_Red", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (named != null)
        {
            return named;
        }
        return renderers.SelectMany(renderer => renderer.sharedMaterials).FirstOrDefault(material => material != null);
    }

    private static Material CreateOrUpdateMaterial(string path, string shaderName, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color * 2.2f);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static AudioClip LoadAudio(string fileName)
    {
        return AssetDatabase.LoadAssetAtPath<AudioClip>(AudioRoot + "/" + fileName);
    }

    private static GameObject FindSceneObject(string name)
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return transforms.FirstOrDefault(transform => transform.gameObject.scene.IsValid() && transform.name == name)?.gameObject;
    }

    private static GameObject FindLavaElementalByAnimator()
    {
        Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Animator animator in animators)
        {
            if (!animator.gameObject.scene.IsValid() || animator.runtimeAnimatorController == null)
            {
                continue;
            }
            if (animator.runtimeAnimatorController.name.IndexOf("LavaElemental", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return animator.transform.root.gameObject;
            }
        }
        return null;
    }

    private static Bounds CalculateWorldBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private static Bounds CalculateLocalBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            Bounds world = renderer.bounds;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3(x, y, z));
                        Vector3 local = root.InverseTransformPoint(corner);
                        if (!initialized)
                        {
                            result = new Bounds(local, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            result.Encapsulate(local);
                        }
                    }
                }
            }
        }
        return initialized ? result : new Bounds(Vector3.zero, new Vector3(0.12f, 2.2f, 0.12f));
    }

    private static int LargestAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z) return 0;
        return size.y >= size.z ? 1 : 2;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void RemoveLegacyCameraUi(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        Transform[] descendants = camera.GetComponentsInChildren<Transform>(true);
        for (int i = descendants.Length - 1; i >= 0; i--)
        {
            Transform candidate = descendants[i];
            if (candidate != camera.transform
                && (candidate.name == "Gameplay HUD" || candidate.name == "Result HUD"))
            {
                Object.DestroyImmediate(candidate.gameObject);
            }
        }
    }

    private static Dictionary<string, UiTransformState> CaptureSpatialUiLayout(GameObject root)
    {
        Dictionary<string, UiTransformState> layout = new Dictionary<string, UiTransformState>();
        if (root == null)
        {
            return layout;
        }

        WukongSpatialUi[] panels = root.GetComponentsInChildren<WukongSpatialUi>(true);
        foreach (WukongSpatialUi panel in panels)
        {
            if (panel == null || layout.ContainsKey(panel.name))
            {
                continue;
            }

            layout.Add(panel.name, new UiTransformState
            {
                headRelativeOffset = panel.headRelativeOffset,
                initializePosition = panel.initializePosition,
                followPosition = panel.followPosition
            });
        }

        return layout;
    }

    private static void RestoreSpatialUiLayout(GameObject root, Dictionary<string, UiTransformState> layout)
    {
        if (root == null || layout == null || layout.Count == 0)
        {
            return;
        }

        WukongSpatialUi[] panels = root.GetComponentsInChildren<WukongSpatialUi>(true);
        foreach (WukongSpatialUi panel in panels)
        {
            if (panel == null || !layout.TryGetValue(panel.name, out UiTransformState state))
            {
                continue;
            }

            panel.headRelativeOffset = panel.name == "Unified Rhythm HUD"
                ? UnifiedHudOffset
                : state.headRelativeOffset;
            panel.initializePosition = state.initializePosition;
            panel.followPosition = state.followPosition;
            EditorUtility.SetDirty(panel);
        }
    }

    private static GameObject CreateSpatialPanel(
        string name,
        Transform parent,
        Camera camera,
        Vector3 headRelativeOffset,
        Vector2 canvasSize,
        float pixelsToMeters,
        int sortingOrder)
    {
        GameObject panel = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(WukongSpatialUi));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = canvasSize;
        panel.transform.localScale = Vector3.one * pixelsToMeters;

        Canvas canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = panel.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.referencePixelsPerUnit = 100f;

        WukongSpatialUi spatialUi = panel.GetComponent<WukongSpatialUi>();
        spatialUi.targetCamera = camera;
        spatialUi.headRelativeOffset = headRelativeOffset;
        spatialUi.followPosition = false;
        spatialUi.initializePosition = true;
        return panel;
    }

    private static GameObject CreateScreenOverlayPanel(string name, Transform parent, Camera camera, Vector2 referenceSize)
    {
        GameObject panel = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;

        Canvas canvas = panel.GetComponent<Canvas>();
        // Screen Space - Camera remains a full settlement page while rendering
        // correctly in both PICO stereo eyes. It is head-locked, not a world HUD.
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.75f;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = panel.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
        return panel;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        Image image = gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage CreateRawImage(string name, Transform parent, Texture texture, Color color)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        RawImage image = gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage CreateLiquidGlassCard(string name, Transform parent, Texture texture, Color color)
    {
        RawImage image = CreateRawImage(name, parent, texture, color);
        // The generated horizontal card occupies the center of a wide preview.
        // Crop away the presentation margins before stretching it into the HUD.
        image.uvRect = new Rect(0.3f, 0.1f, 0.4f, 0.8f);
        return image;
    }

    private static Text CreateText(string name, Transform parent, Font font, int size, TextAnchor alignment, Color color)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        Text text = gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Outline outline = gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.03f, 0.008f, 0.002f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
