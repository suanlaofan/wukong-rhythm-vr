using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Android;

internal sealed class WukongPicoGradlePostProcessor : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string gradlePath = Path.Combine(path, "build.gradle");
        if (!File.Exists(gradlePath))
        {
            return;
        }

        string content = File.ReadAllText(gradlePath);
        const string implementationDependency = "implementation(name: 'PxrPlatform', ext:'aar')";
        const string apiDependency = "api(name: 'PxrPlatform', ext:'aar')";
        if (!content.Contains(implementationDependency))
        {
            return;
        }

        File.WriteAllText(gradlePath, content.Replace(implementationDependency, apiDependency));
        Debug.Log("PICO Gradle fix: exposed PxrPlatform Java classes to the final APK.");
    }
}
