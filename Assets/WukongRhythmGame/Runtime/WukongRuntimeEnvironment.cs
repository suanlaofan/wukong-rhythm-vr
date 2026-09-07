using UnityEngine;

/// <summary>Keep emulator-only input and sensor fallbacks off physical headsets.</summary>
public static class WukongRuntimeEnvironment
{
    private static bool? emulator;

    public static bool IsEmulator
    {
        get
        {
            if (emulator.HasValue) return emulator.Value;
            emulator = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var properties = new AndroidJavaClass("android.os.SystemProperties"))
                    emulator = properties.CallStatic<string>("get", "ro.kernel.qemu", "0") == "1";
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("WUKONG_EMULATOR_DETECTION " + e.GetType().Name);
            }
#endif
            return emulator.Value;
        }
    }
}
