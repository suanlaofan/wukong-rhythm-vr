using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class WukongSongImporter
{
    private const string AudioRoot = "Assets/WukongRhythmGame/Audio/Imported";
    private const string SongsRoot = "Assets/WukongRhythmGame/Audio/Songs/Imported";
    private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".aif",
        ".aiff",
        ".mp3",
        ".ogg",
        ".wav"
    };

    [MenuItem("Tools/Wukong/Music/Import Songs From Folder...")]
    public static void ImportSongsFromFolderMenu()
    {
        string sourceFolder = EditorUtility.OpenFolderPanel("Import Wukong Rhythm Songs", string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(sourceFolder))
        {
            ImportSongsFromFolder(sourceFolder);
        }
    }

    public static int ImportSongsFromFolder(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException("Song import folder was not found: " + sourceFolder);
        }

        string[] sourceFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            Debug.LogWarning("No supported audio files were found in " + sourceFolder);
            return 0;
        }

        EnsureFolder(AudioRoot);
        EnsureFolder(SongsRoot);

        List<PendingSong> pendingSongs = new List<PendingSong>(sourceFiles.Length);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string sourcePath in sourceFiles)
            {
                string sourceTitle = Path.GetFileNameWithoutExtension(sourcePath).Trim();
                string assetName = MakeAssetName(sourceTitle);
                string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                string audioAssetPath = AudioRoot + "/" + assetName + extension;
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string destinationPath = Path.Combine(projectRoot, audioAssetPath);
                bool audioChanged = !File.Exists(destinationPath) || !FilesMatch(sourcePath, destinationPath);
                if (audioChanged)
                {
                    File.Copy(sourcePath, destinationPath, true);
                }

                pendingSongs.Add(new PendingSong(sourceTitle, assetName, audioAssetPath, audioChanged));
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        int generatedCount = 0;
        foreach (PendingSong pending in pendingSongs)
        {
            ConfigureMusicImporter(pending.audioAssetPath);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(pending.audioAssetPath);
            if (clip == null)
            {
                Debug.LogError("Unity could not import song audio: " + pending.audioAssetPath);
                continue;
            }

            string songAssetPath = SongsRoot + "/" + pending.assetName + ".asset";
            WukongSongDefinition song = AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(songAssetPath);
            bool isNewSong = song == null;
            if (isNewSong)
            {
                song = ScriptableObject.CreateInstance<WukongSongDefinition>();
                AssetDatabase.CreateAsset(song, songAssetPath);
            }

            song.songId = pending.assetName.ToLowerInvariant();
            song.title = EnglishTitle(pending.sourceTitle);
            song.titleZh = ChineseTitle(pending.sourceTitle);
            song.artist = "EDM Test Collection";
            song.artistZh = "EDM 测试曲库";
            song.audioClip = clip;
            song.previewClip = null;
            song.travelBeats = 6f;
            song.beatsPerBar = 4;
            EditorUtility.SetDirty(song);

            if (isNewSong
                || pending.audioChanged
                || song.notes == null
                || song.notes.Count == 0
                || song.generatorVersion < WukongBeatmapGenerator.CurrentGeneratorVersion)
            {
                WukongBeatmapGenerator.GenerateForSong(song);
                generatedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        WukongBeatmapGenerator.RefreshSongLibrary();
        Debug.Log("Imported " + pendingSongs.Count + " songs from " + sourceFolder
            + "; generated " + generatedCount + " beatmaps.");
        return pendingSongs.Count;
    }

    private static void ConfigureMusicImporter(string audioAssetPath)
    {
        AudioImporter importer = AssetImporter.GetAtPath(audioAssetPath) as AudioImporter;
        if (importer == null)
        {
            return;
        }

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        bool needsUpdate = settings.loadType != AudioClipLoadType.Streaming
            || settings.compressionFormat != AudioCompressionFormat.Vorbis
            || !Mathf.Approximately(settings.quality, 0.78f)
            || settings.preloadAudioData
            || !importer.loadInBackground;
        if (!needsUpdate)
        {
            return;
        }

        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.78f;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = true;
        importer.SaveAndReimport();
    }

    private static string EnglishTitle(string sourceTitle)
    {
        return sourceTitle
            .Replace("萌萌futurebass?", "Cute Future Bass")
            .Replace("萌萌Electropop", "Cute Electropop");
    }

    private static string ChineseTitle(string sourceTitle)
    {
        return sourceTitle
            .Replace("萌萌futurebass?", "萌萌 Future Bass")
            .Replace("萌萌Electropop", "萌萌 Electropop");
    }

    private static string MakeAssetName(string sourceTitle)
    {
        string englishTitle = EnglishTitle(sourceTitle);
        StringBuilder result = new StringBuilder(englishTitle.Length);
        bool previousWasSeparator = false;
        foreach (char character in englishTitle)
        {
            bool isAsciiLetterOrDigit = character <= 127 && char.IsLetterOrDigit(character);
            if (isAsciiLetterOrDigit)
            {
                result.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && result.Length > 0)
            {
                result.Append('_');
                previousWasSeparator = true;
            }
        }

        string assetName = result.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(assetName) ? "Imported_Song" : assetName;
    }

    private static bool FilesMatch(string leftPath, string rightPath)
    {
        FileInfo left = new FileInfo(leftPath);
        FileInfo right = new FileInfo(rightPath);
        if (left.Length != right.Length)
        {
            return false;
        }

        using (SHA256 hash = SHA256.Create())
        using (FileStream leftStream = File.OpenRead(leftPath))
        using (FileStream rightStream = File.OpenRead(rightPath))
        {
            byte[] leftHash = hash.ComputeHash(leftStream);
            byte[] rightHash = hash.ComputeHash(rightStream);
            return leftHash.SequenceEqual(rightHash);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private readonly struct PendingSong
    {
        public readonly string sourceTitle;
        public readonly string assetName;
        public readonly string audioAssetPath;
        public readonly bool audioChanged;

        public PendingSong(string sourceTitle, string assetName, string audioAssetPath, bool audioChanged)
        {
            this.sourceTitle = sourceTitle;
            this.assetName = assetName;
            this.audioAssetPath = audioAssetPath;
            this.audioChanged = audioChanged;
        }
    }
}
