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

    public float SwingSpeed => swingSpeed;
    public bool IsThrown => throwState != ThrowState.Held;
    public int SwingId => swingId;
    public double SampleDsp => sampleDsp;
    public double PreviousSampleDsp => previousSampleDsp;
    public bool HasTrackedHand => usingXr;
    public void BindGame(WukongRhythmGame owner) { game = owner; }

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.25f;
        audioSource.volume = Mathf.Clamp01(whooshVolume);
    }

    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        heldLocalPosition = transform.localPosition;
        heldLocalRotation = transform.localRotation;
        lastStaffCenter = staffCollider != null ? staffCollider.bounds.center : transform.position;
        lastStaffTip = CalculateStaffTip();
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

        Vector3 currentCenter = staffCollider != null ? staffCollider.bounds.center : transform.position;
        Vector3 currentTip = CalculateStaffTip();
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
        bool reversed = !IsThrown && motion.sqrMagnitude > 0.00001f && previousMotion.sqrMagnitude > 0.00001f
            && Vector3.Dot(motion.normalized, previousMotion.normalized) < -0.2f
            && sampleDsp - swingStartedDsp > 0.10;
        if (moving && (!swingActive || reversed)) { swingId++; swingStartedDsp = sampleDsp; }
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
        bool validPosition = hasPosition
            && position.sqrMagnitude > 0.01f
            && position.sqrMagnitude < 9f
            && IsFinite(position);
        bool validRotation = hasRotation && IsFinite(rotation)
            && Quaternion.Dot(rotation, rotation) > 0.5f;
        if (rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && !tracked) return;
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
        Vector3 basePosition = handAnchor != null ? handAnchor.position : transform.position;
        Quaternion baseRotation = handAnchor != null ? handAnchor.rotation : transform.rotation;
        if (playerCamera != null)
        {
            // Keep the fallback staff in the first-person view when the editor or
            // simulator has no right-hand pose yet. A real XR grip pose always
            // takes precedence in UpdateXrDevice.
            basePosition = playerCamera.transform.TransformPoint(new Vector3(0.38f, -0.18f, 0.58f));
            baseRotation = playerCamera.transform.rotation * Quaternion.Euler(10f, -8f, -18f);
        }

        bool swingPressed = false;
        if (Keyboard.current != null)
        {
            swingPressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
        }
        Vector2 normalizedMouse = Vector2.zero;
        if (Mouse.current != null)
        {
            swingPressed |= Mouse.current.leftButton.wasPressedThisFrame;
            Vector2 mouse = Mouse.current.position.ReadValue();
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            normalizedMouse = new Vector2(mouse.x / width, mouse.y / height) - Vector2.one * 0.5f;
        }
        if (swingTime < 0f)
        {
            Vector3 mouseOffset = playerCamera != null
                ? playerCamera.transform.right * (normalizedMouse.x * 0.12f)
                    + playerCamera.transform.up * (normalizedMouse.y * 0.08f)
                : Vector3.zero;
            handAnchor.SetPositionAndRotation(
                basePosition + mouseOffset,
                baseRotation * Quaternion.Euler(-normalizedMouse.y * 8f, normalizedMouse.x * 12f, -normalizedMouse.x * 8f));
        }

        if (swingPressed && swingTime < 0f && throwState == ThrowState.Held)
        {
            swingTime = 0f;
            if (whooshSound != null)
            {
                audioSource.pitch = Random.Range(0.92f, 1.06f);
                audioSource.PlayOneShot(whooshSound);
            }
        }

        if (swingTime >= 0f && throwState == ThrowState.Held)
        {
            swingTime += Time.deltaTime;
            float duration = 0.28f;
            float t = Mathf.Clamp01(swingTime / duration);
            float arc = Mathf.Sin(t * Mathf.PI);
            float side = Mathf.Lerp(-1f, 1f, t);
            Vector3 swingOffset = playerCamera != null
                ? playerCamera.transform.right * (side * 0.16f)
                    + playerCamera.transform.up * (arc * 0.07f)
                    + playerCamera.transform.forward * (arc * 0.08f)
                : new Vector3(side * 0.16f, arc * 0.07f, arc * 0.08f);
            handAnchor.SetPositionAndRotation(
                basePosition + swingOffset,
                baseRotation * Quaternion.Euler(-18f * arc, side * 35f, -side * 22f));
            if (t >= 1f)
            {
                swingTime = -1f;
                handAnchor.SetPositionAndRotation(basePosition, baseRotation);
            }
        }
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
        if (SweepSphere(previousStaffTip - previousRock, lastStaffTip - currentRock, hitRadius, out float tip)) fraction = tip;
        if (SweepSphere(previousStaffCenter - previousRock, lastStaffCenter - currentRock, hitRadius, out float center)) fraction = Mathf.Min(fraction, center);
        if (Vector3.Distance(staffCollider.ClosestPoint(currentRock), currentRock) <= hitRadius) fraction = Mathf.Min(fraction, 1f);
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
        previousStaffCenter = lastStaffCenter = staffCollider != null ? staffCollider.bounds.center : transform.position;
        previousStaffTip = lastStaffTip = CalculateStaffTip();
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
        throwStartPosition = transform.position;
        throwStartRotation = transform.rotation;
        throwDestination = ResolveThrowDestination(target);
        throwStateStarted = Time.unscaledTime;
        throwState = ThrowState.Outbound;
        swingTime = -1f;
        transform.SetParent(null, true);
        if (swingTrail != null)
        {
            swingTrail.emitting = true;
        }
        SendHaptic(0.28f, 0.045f);
        return true;
    }

    public void CancelThrowAndReattach()
    {
        throwState = ThrowState.Held;
        throwTarget = null;
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
            if (throwTarget != null)
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
