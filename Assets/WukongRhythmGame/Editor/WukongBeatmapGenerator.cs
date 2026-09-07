using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WukongBeatmapGenerator
{
    public const int CurrentGeneratorVersion = 4;
    private const string AudioRoot = "Assets/WukongRhythmGame/Audio";
    private const string SongsRoot = AudioRoot + "/Songs";
    private const string LibraryPath = SongsRoot + "/WukongSongLibrary.asset";
    private const string DefaultSongPath = SongsRoot + "/Fantasy_Battle_Suspense.asset";
    private const string DefaultAudioPath = AudioRoot + "/Fantasy_Battle_Suspense.mp3";

    [MenuItem("Tools/Wukong/Music/Refresh Song Library")]
    public static void RefreshSongLibrary()
    {
        WukongSongLibrary library = EnsureDefaultLibrary();
        string[] songGuids = AssetDatabase.FindAssets("t:WukongSongDefinition", new[] { SongsRoot });
        library.songs.Clear();
        List<WukongSongDefinition> discoveredSongs = new List<WukongSongDefinition>();
        for (int i = 0; i < songGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(songGuids[i]);
            WukongSongDefinition song = AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(path);
            if (song != null && song.audioClip != null && !discoveredSongs.Contains(song))
            {
                discoveredSongs.Add(song);
            }
        }

        library.songs.AddRange(discoveredSongs
            .OrderBy(song => song.title, StringComparer.OrdinalIgnoreCase));

        library.RemoveNullEntries();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Selection.activeObject = library;
        Debug.Log("Wukong song library refreshed: " + library.Count + " songs.");
    }

    [MenuItem("Tools/Wukong/Music/Generate Beatmap For Selected Song")]
    public static void GenerateSelectedSong()
    {
        WukongSongDefinition song = Selection.activeObject as WukongSongDefinition;
        if (song == null)
        {
            Debug.LogError("Select a WukongSongDefinition asset first.");
            return;
        }

        GenerateForSong(song);
    }

    public static WukongSongLibrary EnsureDefaultLibrary()
    {
        EnsureFolder("Assets/WukongRhythmGame");
        EnsureFolder(AudioRoot);
        EnsureFolder(SongsRoot);

        WukongSongLibrary library = AssetDatabase.LoadAssetAtPath<WukongSongLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<WukongSongLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        WukongSongDefinition defaultSong = AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(DefaultSongPath);
        AudioClip defaultClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultAudioPath);
        if (defaultSong == null && defaultClip != null)
        {
            defaultSong = ScriptableObject.CreateInstance<WukongSongDefinition>();
            defaultSong.songId = "fantasy_battle_suspense";
            defaultSong.title = "Fantasy Battle Suspense";
            defaultSong.artist = "Wukong Rhythm Studio";
            defaultSong.titleZh = "幻想战斗悬念曲";
            defaultSong.artistZh = "悟空节奏工作室";
            defaultSong.audioClip = defaultClip;
            defaultSong.bpm = 120f;
            defaultSong.beatOffsetSeconds = 0f;
            defaultSong.travelBeats = 6f;
            AssetDatabase.CreateAsset(defaultSong, DefaultSongPath);
        }

        if (defaultSong != null
            && defaultSong.audioClip != null
            && (defaultSong.notes.Count == 0 || defaultSong.generatorVersion < CurrentGeneratorVersion))
        {
            GenerateForSong(defaultSong);
        }
        if (defaultSong != null && !library.songs.Contains(defaultSong))
        {
            library.songs.Add(defaultSong);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        library.RemoveNullEntries();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return library;
    }

    public static void GenerateForSong(WukongSongDefinition song)
    {
        if (song == null || song.audioClip == null)
        {
            Debug.LogError("A song definition and an audio clip are required.");
            return;
        }

        if (song.lockBeatmap) { Debug.Log("Chart is locked: " + song.title); return; }
        AudioClip clip = song.audioClip;
        string clipPath = AssetDatabase.GetAssetPath(clip);
        AudioImporter importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
        AudioImporterSampleSettings originalSettings = importer != null
            ? importer.defaultSampleSettings
            : default(AudioImporterSampleSettings);
        bool originalLoadInBackground = importer != null && importer.loadInBackground;
        bool changedImportSettings = importer != null
            && (originalSettings.loadType != AudioClipLoadType.DecompressOnLoad
                || originalSettings.compressionFormat != AudioCompressionFormat.PCM
                || !originalSettings.preloadAudioData
                || originalLoadInBackground);

        try
        {
            if (changedImportSettings)
            {
                AudioImporterSampleSettings analysisSettings = originalSettings;
                analysisSettings.loadType = AudioClipLoadType.DecompressOnLoad;
                analysisSettings.compressionFormat = AudioCompressionFormat.PCM;
                analysisSettings.preloadAudioData = true;
                importer.defaultSampleSettings = analysisSettings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            }

            clip.LoadAudioData();
            float[] samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
            {
                GenerateFallback(song, clip);
                return;
            }

            List<float> envelope = BuildOnsetEnvelope(samples, clip.channels, clip.frequency, out int hopSamples);
            float estimatedBpm = EstimateBpm(envelope, hopSamples, clip.frequency, song.bpm);
            song.bpm = estimatedBpm;
            song.beatOffsetSeconds = EstimateBeatOffset(envelope, hopSamples, clip.frequency, song.BeatDuration);
            song.notes = BuildNotes(envelope, hopSamples, clip.frequency, song, clip.length);
            song.generatorVersion = CurrentGeneratorVersion;
            song.auditoryReviewed = false;
            song.chartProvenance = "Automatic onset analysis; listening review required";
            song.SortAndSanitize();
            EditorUtility.SetDirty(song);
            AssetDatabase.SaveAssets();
            Debug.Log("Generated " + song.notes.Count + " rhythm notes for " + song.title + " at " + song.bpm.ToString("0.0") + " BPM.");
        }
        finally
        {
            if (changedImportSettings && importer != null)
            {
                importer.defaultSampleSettings = originalSettings;
                importer.loadInBackground = originalLoadInBackground;
                importer.SaveAndReimport();
            }
        }
    }

    private static List<float> BuildOnsetEnvelope(float[] samples, int channels, int sampleRate, out int hopSamples)
    {
        hopSamples = Mathf.Max(128, sampleRate / 100);
        int windowSamples = hopSamples * 2;
        int frameCount = Mathf.Max(1, samples.Length / Mathf.Max(1, channels * hopSamples));
        List<float> envelope = new List<float>(frameCount);
        float previousEnergy = 0f;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int start = frame * hopSamples;
            int end = Mathf.Min(samples.Length / channels, start + windowSamples);
            double sum = 0d;
            int count = 0;
            for (int sample = start; sample < end; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    int index = sample * channels + channel;
                    if (index >= samples.Length)
                    {
                        break;
                    }
                    float value = samples[index];
                    sum += value * value;
                    count++;
                }
            }

            float energy = count > 0 ? Mathf.Sqrt((float)(sum / count)) : 0f;
            envelope.Add(Mathf.Max(0f, energy - previousEnergy));
            previousEnergy = energy;
        }
        return envelope;
    }

    private static float EstimateBpm(List<float> envelope, int hopSamples, int sampleRate, float fallback)
    {
        int minLag = Mathf.Max(1, Mathf.RoundToInt(sampleRate * 60f / 180f / hopSamples));
        int maxLag = Mathf.Min(envelope.Count / 2, Mathf.RoundToInt(sampleRate * 60f / 70f / hopSamples));
        float bestScore = float.MinValue;
        int bestLag = Mathf.Max(minLag, Mathf.RoundToInt(sampleRate * 60f / Mathf.Max(40f, fallback) / hopSamples));
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float score = 0f;
            for (int i = lag; i < envelope.Count; i++)
            {
                score += envelope[i] * envelope[i - lag];
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        float bpm = 60f * sampleRate / (bestLag * hopSamples);
        while (bpm < 80f) bpm *= 2f;
        while (bpm > 170f) bpm *= 0.5f;
        return Mathf.Clamp(Mathf.Round(bpm * 10f) / 10f, 80f, 170f);
    }

    private static float EstimateBeatOffset(List<float> envelope, int hopSamples, int sampleRate, float beatDuration)
    {
        float bestScore = float.MinValue;
        float bestOffset = 0f;
        int phaseSteps = 32;
        for (int step = 0; step < phaseSteps; step++)
        {
            float phase = beatDuration * step / phaseSteps;
            float score = 0f;
            for (float time = phase; time < envelope.Count * hopSamples / (float)sampleRate; time += beatDuration)
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(time * sampleRate / hopSamples), 0, envelope.Count - 1);
                score += envelope[index];
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = phase;
            }
        }
        return bestOffset;
    }

    private static List<WukongBeatNote> BuildNotes(
        List<float> envelope,
        int hopSamples,
        int sampleRate,
        WukongSongDefinition song,
        float clipLength)
    {
        List<WukongBeatNote> notes = new List<WukongBeatNote>();
        float beatDuration = song.BeatDuration;
        float minimumSeparation = Mathf.Max(0.42f, beatDuration * 0.8f);
        float lastNoteTime = -100f;
        int minFrame = Mathf.Max(0, Mathf.RoundToInt((song.TimeAtBeat(song.travelBeats + 1f)) * sampleRate / hopSamples));
        for (int frame = Mathf.Max(1, minFrame); frame < envelope.Count - 1; frame++)
        {
            float time = frame * hopSamples / (float)sampleRate;
            if (time >= clipLength - 0.2f || time - lastNoteTime < minimumSeparation)
            {
                continue;
            }

            float localMean = 0f;
            int meanStart = Mathf.Max(0, frame - 12);
            int meanEnd = Mathf.Min(envelope.Count - 1, frame + 12);
            for (int i = meanStart; i <= meanEnd; i++) localMean += envelope[i];
            localMean /= Mathf.Max(1, meanEnd - meanStart + 1);
            float threshold = localMean * 1.6f + 0.0015f;
            if (envelope[frame] <= threshold || envelope[frame] < envelope[frame - 1] || envelope[frame] < envelope[frame + 1])
            {
                continue;
            }

            float beat = Mathf.Round((time - song.beatOffsetSeconds) / beatDuration * 4f) / 4f;
            if (beat < song.travelBeats + 1f)
            {
                continue;
            }

            int lane = Mathf.RoundToInt(Mathf.Sin(beat * 0.83f));
            float strength = Mathf.Clamp(envelope[frame] / Mathf.Max(0.005f, localMean + 0.001f), 0.5f, 1.8f);
            WukongBeatNoteType type = Mathf.RoundToInt(beat) % 16 == 0
                ? WukongBeatNoteType.Spell
                : WukongBeatNoteType.Normal;
            var note = new WukongBeatNote(beat, lane, type, 2f, strength);
            note.timingOffsetSeconds = time + 0.01f - song.TimeAtBeat(beat);
            notes.Add(note);
            lastNoteTime = time;
            if (notes.Count >= 500)
            {
                break;
            }
        }

        if (notes.Count == 0)
        {
            for (float beat = song.travelBeats + 2f; song.TimeAtBeat(beat) < clipLength - 0.2f; beat += 2f)
            {
                notes.Add(new WukongBeatNote(beat, Mathf.RoundToInt(Mathf.Sin(beat)), WukongBeatNoteType.Normal, 2f, 1f));
            }
        }
        return notes;
    }

    private static void GenerateFallback(WukongSongDefinition song, AudioClip clip)
    {
        song.notes = new List<WukongBeatNote>();
        for (float beat = song.travelBeats + 2f; song.TimeAtBeat(beat) < clip.length - 0.2f; beat += 2f)
        {
            song.notes.Add(new WukongBeatNote(beat, Mathf.RoundToInt(Mathf.Sin(beat)), WukongBeatNoteType.Normal, 2f, 1f));
        }
        song.SortAndSanitize();
        // Keep fallback maps eligible for regeneration when audio decoding becomes available.
        song.generatorVersion = Mathf.Max(0, CurrentGeneratorVersion - 1);
        EditorUtility.SetDirty(song);
        AssetDatabase.SaveAssets();
        Debug.LogWarning("Audio data was unavailable; generated a conservative fallback beatmap for " + song.title + ".");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string folder = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, folder);
    }
}
