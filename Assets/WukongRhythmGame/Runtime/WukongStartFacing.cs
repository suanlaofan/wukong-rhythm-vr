using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// A non-zero head yaw at launch (e.g. on the PICO emulator) leaves the player facing away from the monster.
// Once head tracking settles, yaw the rig around the head so the view faces the monster; repeat after a recenter.
public sealed class WukongStartFacing : MonoBehaviour
{
    private const string MonsterPointName = "Monster Throw Point";
    private const float MinCorrectionDegrees = 20f;
    private const float StableSeconds = 0.5f;
    private const float StableYawTolerance = 2f;
    private const float MaxWaitSeconds = 4f;

    private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
    private readonly HashSet<XRInputSubsystem> hooked = new HashSet<XRInputSubsystem>();
    private Camera targetCamera;
    private Transform rig;
    private Transform monsterPoint;
    private Vector3 authoredForward;
    private bool pending = true;
    private float pendingSince;
    private float stableSince = -1f;
    private float lastYaw;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<WukongStartFacing>() == null)
        {
            new GameObject("Wukong Start Facing").AddComponent<WukongStartFacing>();
        }
    }

    private void Start()
    {
        targetCamera = FindPlayerCamera();
        if (targetCamera == null)
        {
            enabled = false;
            return;
        }
        Transform offset = targetCamera.transform.parent;
        rig = offset != null && offset.parent != null ? offset.parent : offset;
        GameObject point = GameObject.Find(MonsterPointName);
        monsterPoint = point != null ? point.transform : null;
        authoredForward = rig != null ? Flat(rig.forward) : Vector3.zero;
        pendingSince = Time.unscaledTime;
    }

    private void OnDestroy()
    {
        foreach (XRInputSubsystem subsystem in hooked)
        {
            if (subsystem != null) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }
        hooked.Clear();
    }

    private void Update()
    {
        HookSubsystems();
        if (!pending || rig == null || !XRSettings.isDeviceActive) return;

        Vector3 cameraForward = Flat(targetCamera.transform.forward);
        if (cameraForward == Vector3.zero) return;
        float yaw = Mathf.Atan2(cameraForward.x, cameraForward.z) * Mathf.Rad2Deg;
        bool headMoved = Quaternion.Angle(targetCamera.transform.localRotation, Quaternion.identity) > 0.5f;
        if (!headMoved && Time.unscaledTime - pendingSince < MaxWaitSeconds) return;

        if (stableSince < 0f || Mathf.Abs(Mathf.DeltaAngle(yaw, lastYaw)) > StableYawTolerance)
        {
            stableSince = Time.unscaledTime;
            lastYaw = yaw;
            return;
        }
        if (Time.unscaledTime - stableSince < StableSeconds) return;

        Align(cameraForward);
        pending = false;
    }

    private void Align(Vector3 cameraForward)
    {
        Vector3 wanted = monsterPoint != null ? Flat(monsterPoint.position - targetCamera.transform.position) : authoredForward;
        if (wanted == Vector3.zero) return;
        float delta = Vector3.SignedAngle(cameraForward, wanted, Vector3.up);
        bool apply = Mathf.Abs(delta) >= MinCorrectionDegrees;
        Debug.Log("WUKONG_FACING headLocalYaw=" + targetCamera.transform.localEulerAngles.y.ToString("F1")
            + " delta=" + delta.ToString("F1") + " applied=" + apply);
        if (apply) rig.RotateAround(targetCamera.transform.position, Vector3.up, delta);
    }

    private void HookSubsystems()
    {
        if (hooked.Count > 0) return;
        SubsystemManager.GetSubsystems(subsystems);
        foreach (XRInputSubsystem subsystem in subsystems)
        {
            if (subsystem != null && hooked.Add(subsystem)) subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
        }
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
    {
        pending = true;
        pendingSince = Time.unscaledTime;
        stableSince = -1f;
    }

    private static Camera FindPlayerCamera()
    {
        if (Camera.main != null) return Camera.main;
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (camera.enabled && camera.targetTexture == null && camera.stereoTargetEye != StereoTargetEyeMask.None) return camera;
        }
        return null;
    }

    private static Vector3 Flat(Vector3 value)
    {
        Vector3 flat = Vector3.ProjectOnPlane(value, Vector3.up);
        return flat.sqrMagnitude < 0.0001f ? Vector3.zero : flat.normalized;
    }
}
