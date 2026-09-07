using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WukongReleaseBuilder
{
    [MenuItem("Wukong Rhythm/Build/Build Diagnostic APK")]
    public static void BuildDiagnostic() { Build(false); }

    [MenuItem("Wukong Rhythm/Build/Build Release APK")]
    public static void BuildRelease() { Build(true); }

    private static void Build(bool release)
    {
        string id = Environment.GetEnvironmentVariable("WUKONG_PACKAGE_ID");
        if (string.IsNullOrWhiteSpace(id)) id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (release && (string.IsNullOrWhiteSpace(id) || id.Contains("template") || id.Contains("UnityTechnologies")))
            throw new InvalidOperationException("WUKONG_RELEASE_BLOCKED: registered application identifier required.");
        if (release && (!PlayerSettings.Android.useCustomKeystore || string.IsNullOrWhiteSpace(PlayerSettings.Android.keystoreName)))
            throw new InvalidOperationException("WUKONG_RELEASE_BLOCKED: configure the existing registered release keystore first. No key is generated automatically.");

        WukongRhythmValidation.Run();
        WukongRhythmGameBuilder.ConfigurePicoBuildSettings();
        PlayerSettings.bundleVersion = "0.2.0";
        PlayerSettings.Android.bundleVersionCode = Math.Max(2, PlayerSettings.Android.bundleVersionCode);
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.buildAppBundle = false;

        string output = Environment.GetEnvironmentVariable("WUKONG_BUILD_DIR");
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Builds/Remediation");
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, release ? "WukongRhythmVR-0.2.0-release.apk" : "WukongRhythmVR-0.2.0-validation.apk");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = path,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("WUKONG_BUILD_FAILED " + report.summary.result);
        string digest;
        using (SHA256 sha = SHA256.Create())
        using (FileStream input = File.OpenRead(path)) digest = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        File.WriteAllText(path + ".sha256", digest + "  " + Path.GetFileName(path) + "\n");
        File.WriteAllText(path + ".build.txt", "version=0.2.0\nversionCode=" + PlayerSettings.Android.bundleVersionCode
            + "\npackage=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
            + "\nrelease=" + release + "\nsha256=" + digest
            + "\nDevice and platform signature acceptance must be verified separately.\n");
        Debug.Log("WUKONG_BUILD_PASS " + path);
    }
}
