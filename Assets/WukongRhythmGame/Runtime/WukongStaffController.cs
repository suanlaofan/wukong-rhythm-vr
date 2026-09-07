using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

[DefaultExecutionOrder(-200)]
public sealed class WukongStaffController : MonoBehaviour
{
    private enum ThrowState
    {
        Held,
        Outbound,
        Returning
    }

    public Transform trackingOrigin;
    public Transform handAnchor;
    public Camera playerCamera;
    public Collider staffCollider;
    public TrailRenderer swingTrail;
    public AudioClip whooshSound;
    [Range(0f, 1f)] public float whooshVolume = 0.2f;
    public float minimumStrikeSpeed = 0.58f;
    [Tooltip("Extra contact radius to prevent fast swings from skipping between frames.")]
    public float contactForgiveness = 0.16f;
    [Header("Desktop Mouse")]
    [Tooltip("Distance of the mouse aim plane from the camera. The striking tip follows the cursor on this plane.")]
    [Range(.5f, 3f)] public float desktopAimDistance = 1.8f;
    [Header("Throw")]
    public float maximumThrowDistance = 4.5f;
    public float outboundDuration = 0.35f;
    public float returnDuration = 0.35f;
    public float totalSpinDegrees = 1440f;
    public Vector3 localLongAxis = Vector3.up;

    private AudioSource audioSource;
    private UnityEngine.XR.InputDevice rightHandDevice;
    private Vector3 heldLocalPosition;
    private Quaternion heldLocalRotation;
    private Vector3 previousStaffCenter;
    private Vector3 lastStaffCenter;
    private Vector3 previousStaffTip;
    private Vector3 lastStaffTip;
    private Vector3 previousStaffBase, lastStaffBase;
    private float swingTime = -1f;
    private float swingSpeed;
    private double previousSampleDsp;
    private double sampleDsp;
    private int swingId;
    private int consumedSwingId = -1;
    private bool swingActive;
    private Vector3 previousMotion;
    private double swingStartedDsp;
    private WukongRhythmGame game;
    private bool usingXr;
    private ThrowState throwState;
    private Transform throwTarget;
    private Vector3 throwStartPosition;
    private Vector3 throwDestination;
    private Vector3 returnStartPosition;
    private Quaternion throwStartRotation;
    private float throwStateStarted;
    private double throwStartedDsp;
    private WukongBeatRock aimedRock;
    private float aimedRockTime;

    public float SwingSpeed => swingSpeed;
    public bool IsThrown => throwState != ThrowState.Held;
    public int SwingId => swingId;
    public double SampleDsp => sampleDsp;
    public double PreviousSampleDsp => previousSampleDsp;
    public bool HasTrackedHand => usingXr;
    public bool IsThrowingAt(WukongBeatRock rock) => IsThrown && aimedRock == rock
        && rock != null && !rock.IsResolved && Mathf.Abs(rock.TargetSongTime - aimedRockTime) < .0001f;
    public bool KeepsThrowTargetAlive(WukongBeatRock rock) => throwState == ThrowState.Outbound
        && IsThrowingAt(rock) && AudioSettings.dspTime <= throwStartedDsp + outboundDuration + .1f;
    public float ThrowTimingError(WukongBeatRock rock, float impactError)
    {
        if (!IsThrowingAt(rock) || game == null) return impactError;
        float launchError=(float)(game.MusicTimeAtDsp(throwStartedDsp)+game.inputTimingOffsetSeconds-rock.TargetSongTime);
        // Either a rhythmic release or a rhythmic impact earns the timing grade.
        // Travel time must not make an on-beat button press impossible to score.
        return Mathf.Abs(launchError)<Mathf.Abs(impactError)?launchError:impactError;
    }
    public void BindGame(WukongRhythmGame owner) { game = owner; }

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.25f;
        audioSource.volume = Mathf.Clamp01(whooshVolume);
        audioSource.priority = 100;
        if (whooshSound != null) whooshSound.LoadAudioData();
    }

    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        heldLocalPosition = transform.localPosition;
        heldLocalRotation = transform.localRotation;
        lastStaffCenter = CalculateStaffCenter();
        lastStaffTip = CalculateStaffTip();
        previousStaffBase = lastStaffBase = lastStaffCenter * 2f - lastStaffTip;
        previousStaffCenter = lastStaffCenter;
        previousStaffTip = lastStaffTip;
        sampleDsp = previousSampleDsp = AudioSettings.dspTime;
        if (swingTrail != null)
        {
            swingTrail.emitting = false;
        }
    }

    private void Update()
    {
        bool wasTracked = usingXr;
        UpdateXrDevice();
        if (wasTracked != usingXr || (!usingXr && XRSettings.isDeviceActive))
        {
            ResetContactHistory();
            return;
        }
        if (!usingXr && (game == null || game.State != WukongRhythmGame.BattleState.Paused && game.State != WukongRhythmGame.BattleState.Resuming)) UpdateDesktopHand();
        if (game != null && game.State != WukongRhythmGame.BattleState.Playing)
        {
            ResetContactHistory();
            return;
        }
        if (throwState != ThrowState.Held)
        {
            UpdateThrow();
        }

        Vector3 currentCenter = CalculateStaffCenter();
        Vector3 currentTip = CalculateStaffTip();
        previousStaffBase = lastStaffBase;
        lastStaffBase = currentCenter * 2f - currentTip;
        previousStaffCenter = lastStaffCenter;
        previousStaffTip = lastStaffTip;
        previousSampleDsp = sampleDsp;
        sampleDsp = AudioSettings.dspTime;
        float frameDuration = Mathf.Max(0.0001f, (float)(sampleDsp - previousSampleDsp));
        float centerSpeed = Vector3.Distance(currentCenter, lastStaffCenter) / frameDuration;
        float tipSpeed = Vector3.Distance(currentTip, lastStaffTip) / frameDuration;
        // A wrist rotation can leave the collider center almost stationary while
        // the striking end of the staff moves quickly. Use the faster point.
        swingSpeed = Mathf.Max(centerSpeed, tipSpeed);
        Vector3 motion = currentTip - lastStaffTip;
        bool moving = swingSpeed >= minimumStrikeSpeed || swingTime >= 0f || IsThrown;
        bool reversed = swingTime < 0f && !IsThrown && motion.sqrMagnitude > 0.00001f && previousMotion.sqrMagnitude > 0.00001f
            && Vector3.Dot(motion.normalized, previousMotion.normalized) < -0.2f
            && sampleDsp - swingStartedDsp > 0.10;
        if (moving && (!swingActive || reversed))
        {
            swingId++; swingStartedDsp = sampleDsp;
            if (usingXr) PlayWhoosh();
        }
        swingActive = moving;
        previousMotion = motion;
        lastStaffCenter = currentCenter;
        lastStaffTip = currentTip;
        if (swingTrail != null)
        {
            swingTrail.emitting = swingSpeed >= minimumStrikeSpeed * 0.75f || swingTime >= 0f;
        }
    }

    private void UpdateXrDevice()
    {
        if (!rightHandDevice.isValid)
        {
            rightHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        // A simulator can expose a valid device before it exposes pose features.
        // Treat that state as desktop mode so the fallback swing remains usable.
        usingXr = false;
        if (!rightHandDevice.isValid || trackingOrigin == null || handAnchor == null)
        {
            return;
        }

        InputFeatureUsage<Vector3> gripPosition = new InputFeatureUsage<Vector3>("gripPosition");
        InputFeatureUsage<Quaternion> gripRotation = new InputFeatureUsage<Quaternion>("gripRotation");
        // Grip pose is the controller's handle pose. Prefer it whenever the
        // runtime exposes it; device pose is only a compatibility fallback.
        bool hasPosition = rightHandDevice.TryGetFeatureValue(
            gripPosition, out Vector3 position)
            || rightHandDevice.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.devicePosition, out position);
        bool hasRotation = rightHandDevice.TryGetFeatureValue(
            gripRotation, out Quaternion rotation)
            || rightHandDevice.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
        // Some desktop/PICO simulator builds report a valid device with an
        // all-zero pose until tracking has started. Do not snap the staff to
        // the rig origin in that state; use the visible desktop fallback below
        // until a real controller pose arrives.
        bool reportsTracking = rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
        bool validPosition = hasPosition && IsFinite(position)
            && (reportsTracking ? tracked : position.sqrMagnitude > .0001f);
        bool validRotation = hasRotation && IsFinite(rotation)
            && Quaternion.Dot(rotation, rotation) > 0.5f;
        if (reportsTracking && !tracked) return;
        if (!validPosition || !validRotation)
        {
            return;
        }

        Vector3 worldPosition = trackingOrigin.TransformPoint(position);
        Quaternion worldRotation = trackingOrigin.rotation * rotation;
        handAnchor.SetPositionAndRotation(worldPosition, worldRotation);
        usingXr = true;

    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsNaN(value.w)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y)
            && !float.IsInfinity(value.z) && !float.IsInfinity(value.w);
    }

    private void UpdateDesktopHand()
    {
        if (handAnchor == null || playerCamera == null) return;
        Quaternion baseRotation = playerCamera.transform.rotation * Quaternion.Euler(10f, -8f, -18f);

        bool swingPressed = false;
        if (Keyboard.current != null)
        {
            swingPressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
        }
        Vector2 viewport = new Vector2(.5f, .5f);
        if (Mouse.current != null)
        {
            swingPressed |= Mouse.current.leftButton.wasPressedThisFrame;
            Vector2 mouse = Mouse.current.position.ReadValue();
            Rect pixels = playerCamera.pixelRect;
            viewport = new Vector2(Mathf.Clamp01((mouse.x-pixels.x)/Mathf.Max(1f,pixels.width)),
                Mathf.Clamp01((mouse.y-pixels.y)/Mathf.Max(1f,pixels.height)));
        }
        Vector3 aimPoint = playerCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, desktopAimDistance));
        Vector3 tipInHand = HeldTipInHand();

        if (swingPressed && swingTime < 0f && throwState == ThrowState.Held)
        {
            swingTime = 0f;
            // A new click is a new action even if the previous animation's
            // return motion has not yet fallen below the XR speed threshold.
            swingActive = false;
            PlayWhoosh();
        }

        if (swingTime >= 0f && throwState == ThrowState.Held)
        {
            swingTime += Time.deltaTime;
            float duration = 0.28f;
            float t = Mathf.Clamp01(swingTime / duration);
            float arc = Mathf.Sin(t * Mathf.PI);
            float side = Mathf.Lerp(-1f, 1f, t);
            // Animate around the aimed tip so clicking never pulls the staff
            // away from the cursor or delays the input behind an animation.
            baseRotation *= Quaternion.Euler(-18f * arc, side * 20f * arc, -side * 16f * arc);
            if (t >= 1f) swingTime = -1f;
        }
        handAnchor.SetPositionAndRotation(aimPoint-baseRotation*tipInHand,baseRotation);
    }

    private Vector3 HeldTipInHand()
    {
        if (!(staffCollider is CapsuleCollider capsule)) return Vector3.zero;
        Vector3 axis = capsule.direction==0?Vector3.right:capsule.direction==1?Vector3.up:Vector3.forward;
        Vector3 tip = capsule.center+axis*Mathf.Max(0,capsule.height*.5f-capsule.radius);
        return heldLocalPosition+heldLocalRotation*Vector3.Scale(tip,transform.localScale);
    }

    public bool TryGetStrike(Vector3 previousRock, Vector3 currentRock, float radius, out double contactDsp, out int actionId)
    {
        contactDsp = sampleDsp;
        actionId = swingId;
        if (staffCollider == null || !swingActive || consumedSwingId == swingId || sampleDsp <= previousSampleDsp)
        {
            return false;
        }
        float hitRadius = Mathf.Max(0.03f, radius) + Mathf.Max(0f, contactForgiveness);
        float fraction = 2f;
        // Cover both ends and the full shaft. Read transforms directly: physics
        // ClosestPoint/bounds may still describe the previous physics frame.
        if (staffCollider is CapsuleCollider capsule)
            hitRadius += capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.y), Mathf.Abs(capsule.transform.lossyScale.z)));
        for (int i = 0; i <= 4; i++)
        {
            float along = i * .25f;
            Vector3 previous = Vector3.Lerp(previousStaffBase, previousStaffTip, along);
            Vector3 current = Vector3.Lerp(lastStaffBase, lastStaffTip, along);
            if (SweepSphere(previous - previousRock, current - currentRock, hitRadius, out float contact))
                fraction = Mathf.Min(fraction, contact);
        }
        if (fraction > 1f) return false;
        contactDsp = previousSampleDsp + (sampleDsp - previousSampleDsp) * fraction;
        return true;
    }

    public bool ConsumeSwing(int actionId)
    {
        if (actionId != swingId || consumedSwingId == actionId) return false;
        consumedSwingId = actionId;
        return true;
    }

    public void ResetContactHistory()
    {
        previousStaffCenter = lastStaffCenter = CalculateStaffCenter();
        previousStaffTip = lastStaffTip = CalculateStaffTip();
        previousStaffBase = lastStaffBase = lastStaffCenter * 2f - lastStaffTip;
        previousSampleDsp = sampleDsp = AudioSettings.dspTime;
        swingActive = false;
        swingSpeed = 0f;
        previousMotion = Vector3.zero;
        if (swingTrail != null) swingTrail.emitting = false;
    }

    public static bool SweepSphere(Vector3 from, Vector3 to, float radius, out float fraction)
    {
        fraction = 0f;
        float c = from.sqrMagnitude - radius * radius;
        if (c <= 0f) return true;
        Vector3 delta = to - from;
        float a = delta.sqrMagnitude;
        if (a < 0.0000001f) return false;
        float b = Vector3.Dot(from, delta);
        float discriminant = b * b - a * c;
        if (discriminant < 0f) return false;
        fraction = (-b - Mathf.Sqrt(discriminant)) / a;
        return fraction >= 0f && fraction <= 1f;
    }

    private Vector3 CalculateStaffTip()
    {
        if (staffCollider == null)
        {
            return transform.position;
        }

        UnityEngine.CapsuleCollider capsule = staffCollider as UnityEngine.CapsuleCollider;
        if (capsule == null)
        {
            return staffCollider.bounds.center + staffCollider.transform.forward * staffCollider.bounds.extents.z;
        }

        Vector3 axis = capsule.direction == 0 ? Vector3.right
            : capsule.direction == 1 ? Vector3.up
            : Vector3.forward;
        float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
        return capsule.transform.TransformPoint(capsule.center + axis * halfSegment);
    }

    private Vector3 CalculateStaffCenter()
    {
        if (staffCollider is CapsuleCollider capsule) return capsule.transform.TransformPoint(capsule.center);
        return staffCollider != null ? staffCollider.bounds.center : transform.position;
    }

    private void PlayWhoosh()
    {
        if (whooshSound == null || audioSource == null) return;
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(whooshSound);
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.000001f)
        {
            return Vector3.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return Vector3.Distance(point, Vector3.Lerp(start, end, t));
    }

    public void ConfirmHit(float strength)
    {
        if (!rightHandDevice.isValid)
        {
            return;
        }

        rightHandDevice.SendHapticImpulse(0u, Mathf.Clamp01(strength), 0.075f);
    }

    public bool BeginThrow(Transform target)
    {
        if (throwState != ThrowState.Held || handAnchor == null)
        {
            return false;
        }

        throwTarget = target;
        aimedRock = target != null ? target.GetComponent<WukongBeatRock>() : null;
        aimedRockTime = aimedRock != null ? aimedRock.TargetSongTime : -1f;
        throwStartPosition = transform.position;
        throwStartRotation = transform.rotation;
        throwDestination = ResolveThrowDestination(target);
        throwStateStarted = Time.unscaledTime;
        throwStartedDsp = AudioSettings.dspTime;
        ResetContactHistory();
        swingId++;
        swingActive = true;
        swingStartedDsp = throwStartedDsp;
        throwState = ThrowState.Outbound;
        swingTime = -1f;
        transform.SetParent(null, true);
        if (swingTrail != null)
        {
            swingTrail.emitting = true;
        }
        SendHaptic(0.28f, 0.045f);
        PlayWhoosh();
        return true;
    }

    public void CancelThrowAndReattach()
    {
        throwState = ThrowState.Held;
        throwTarget = null;
        aimedRock = null;
        if (handAnchor != null)
        {
            transform.SetParent(handAnchor, false);
            transform.localPosition = heldLocalPosition;
            transform.localRotation = heldLocalRotation;
        }
        if (swingTrail != null)
        {
            swingTrail.emitting = false;
        }
        swingTime = -1f;
        ResetContactHistory();
    }

    private void UpdateThrow()
    {
        if (throwState == ThrowState.Outbound)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - throwStateStarted) / Mathf.Max(0.1f, outboundDuration));
            if (throwTarget != null && (aimedRock == null || IsThrowingAt(aimedRock)))
            {
                throwDestination = ResolveThrowDestination(throwTarget);
            }
            Vector3 control = Vector3.Lerp(throwStartPosition, throwDestination, 0.5f)
                + (playerCamera != null ? playerCamera.transform.up : Vector3.up) * 0.34f;
            transform.position = QuadraticBezier(throwStartPosition, control, throwDestination, Mathf.SmoothStep(0f, 1f, t));
            transform.rotation = throwStartRotation * Quaternion.AngleAxis(totalSpinDegrees * 0.5f * t, SafeLongAxis());
            if (t >= 1f)
            {
                returnStartPosition = transform.position;
                throwStartRotation = transform.rotation;
                throwStateStarted = Time.unscaledTime;
                throwState = ThrowState.Returning;
            }
        }
        else if (throwState == ThrowState.Returning)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - throwStateStarted) / Mathf.Max(0.1f, returnDuration));
            Vector3 handPosition = handAnchor != null
                ? handAnchor.TransformPoint(heldLocalPosition)
                : returnStartPosition;
            Vector3 control = Vector3.Lerp(returnStartPosition, handPosition, 0.5f)
                + (playerCamera != null ? playerCamera.transform.up : Vector3.up) * 0.22f;
            transform.position = QuadraticBezier(returnStartPosition, control, handPosition, Mathf.SmoothStep(0f, 1f, t));
            transform.rotation = throwStartRotation * Quaternion.AngleAxis(totalSpinDegrees * 0.5f * t, SafeLongAxis());
            if (t >= 1f || Vector3.Distance(transform.position, handPosition) < 0.035f)
            {
                CancelThrowAndReattach();
                SendHaptic(0.18f, 0.04f);
            }
        }
    }

    private Vector3 ResolveThrowDestination(Transform target)
    {
        Vector3 desired = target != null
            ? target.position
            : throwStartPosition + (playerCamera != null ? playerCamera.transform.forward : transform.forward) * maximumThrowDistance;
        Vector3 offset = desired - throwStartPosition;
        if (offset.sqrMagnitude > maximumThrowDistance * maximumThrowDistance)
        {
            desired = throwStartPosition + offset.normalized * maximumThrowDistance;
        }
        return desired;
    }

    private Vector3 SafeLongAxis()
    {
        return localLongAxis.sqrMagnitude > 0.001f ? localLongAxis.normalized : Vector3.up;
    }

    private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * a + 2f * inverse * t * b + t * t * c;
    }

    private void SendHaptic(float amplitude, float duration)
    {
        if (rightHandDevice.isValid)
        {
            rightHandDevice.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
        }
    }
}
