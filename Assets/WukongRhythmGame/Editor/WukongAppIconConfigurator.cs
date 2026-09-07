using System;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public static class WukongAppIconConfigurator
{
    private const string Directory = "Assets/WukongRhythmGame/UI/AppIcon/";

    [MenuItem("Wukong Rhythm/Build/Apply Game App Icons")]
    public static void Apply()
    {
        Texture2D icon = Load("WukongAppIcon.png");
        Texture2D foreground = Load("WukongAdaptiveForeground.png");
        Texture2D background = Load("WukongAdaptiveBackground.png");
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] {icon}, IconKind.Any);
        foreach (var kind in new[] {AndroidPlatformIconKind.Legacy, AndroidPlatformIconKind.Round})
        {
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var slot in slots) slot.SetTexture(icon);
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
        }
        var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
        // Unity serializes adaptive layers as background, then foreground.
        // Reversing these makes Android draw the opaque background over the art.
        foreach (var slot in adaptive) slot.SetTextures(new[] {background, foreground});
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive, adaptive);
        AssetDatabase.SaveAssets();
        Debug.Log("WUKONG_APP_ICON_APPLIED rhythm talisman; default, legacy, round and adaptive");
    }

    private static Texture2D Load(string file)
    {
        string path = Directory + file;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing game icon: " + path);
        importer.textureType = TextureImporterType.Default;
        // This project imports some new images with a cubemap preset.
        // App icons must remain ordinary 2D textures.
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null) throw new InvalidOperationException("Could not load game icon: " + path);
        return texture;
    }
}
