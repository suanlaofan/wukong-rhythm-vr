using System.Collections;
using UnityEngine;

/// <summary>
/// Keeps a world-space UI panel at a stable head-relative position and turns its
/// front toward the current headset camera. The panel is intentionally not parented
/// to the camera so it remains a real spatial object in the scene hierarchy.
/// </summary>
public sealed class WukongSpatialUi : MonoBehaviour
{
    public Camera targetCamera;
    public Vector3 headRelativeOffset = new Vector3(0f, 0f, 1.8f);
    [Tooltip("When enabled, place the panel once relative to the headset at startup. Disable this to use the Transform authored in the scene.")]
    public bool initializePosition = false;
    public bool followPosition = false;
    public bool faceCamera = true;
    [Tooltip("Keep the panel parallel to the player's view so offset HUD text stays horizontal.")]
    public bool alignWithView = false;

    private bool positionInitialized;

    private IEnumerator Start()
    {
        ResolveCamera();
        if (!initializePosition || targetCamera == null)
        {
            positionInitialized = true;
            yield break;
        }

        // PICO applies the tracked head pose after normal scene startup. Waiting
        // through a few rendered frames prevents locking the panel to the authored
        // editor-camera pose before XR tracking takes control.
        for (int i = 0; i < 3; i++)
        {
            yield return new WaitForEndOfFrame();
        }

        ResolveCamera();
        if (targetCamera != null)
        {
            PlaceRelativeToCamera();
        }
        positionInitialized = true;
    }

    private void LateUpdate()
    {
        ResolveCamera();
        if (targetCamera == null)
        {
            return;
        }

        if (followPosition)
        {
            PlaceRelativeToCamera();
            positionInitialized = true;
        }

        if (faceCamera && positionInitialized)
        {
            if (alignWithView)
            {
                transform.rotation = targetCamera.transform.rotation;
                return;
            }
            Vector3 toCamera = targetCamera.transform.position - transform.position;
            if (toCamera.sqrMagnitude > 0.000001f)
            {
                // Canvas graphics render from their local negative-Z side. Point
                // positive Z away from the viewer, while world-up keeps the panel
                // level instead of copying headset roll.
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }
    }

    private void ResolveCamera()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
        }
    }

    private void PlaceRelativeToCamera()
    {
        Transform cameraTransform = targetCamera.transform;
        if (alignWithView)
        {
            transform.position = cameraTransform.position + cameraTransform.rotation * headRelativeOffset;
            return;
        }
        Vector3 horizontalForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.000001f)
        {
            horizontalForward = Vector3.forward;
        }

        horizontalForward.Normalize();
        Vector3 horizontalRight = Vector3.Cross(Vector3.up, horizontalForward).normalized;
        transform.position = cameraTransform.position
            + horizontalRight * headRelativeOffset.x
            + Vector3.up * headRelativeOffset.y
            + horizontalForward * headRelativeOffset.z;
    }
}
