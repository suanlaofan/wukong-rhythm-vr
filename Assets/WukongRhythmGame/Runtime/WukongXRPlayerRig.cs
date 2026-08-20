using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

public sealed class WukongXRPlayerRig : MonoBehaviour
{
    public Camera xrCamera;
    public Vector3 desktopHeadPosition = new Vector3(0f, 1.65f, 0f);

    private InputDevice headDevice;

    private void Start()
    {
        if (xrCamera != null && xrCamera.GetComponent<TrackedPoseDriver>() == null)
        {
            xrCamera.transform.localPosition = desktopHeadPosition;
        }
    }

    private void LateUpdate()
    {
        if (!headDevice.isValid)
        {
            headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        }
        if (!headDevice.isValid || xrCamera == null || xrCamera.GetComponent<TrackedPoseDriver>() != null)
        {
            return;
        }

        if (headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
        {
            xrCamera.transform.localPosition = position;
        }
        if (headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
        {
            xrCamera.transform.localRotation = rotation;
        }
    }
}
