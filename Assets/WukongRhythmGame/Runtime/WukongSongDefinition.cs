using System;
using System.Collections.Generic;
using UnityEngine;

public enum WukongBeatNoteType
{
    Normal,
    Spell,
    Double
}

[Serializable]
public struct WukongBeatNote
{
    [Min(0f)] public float beat;
    [Range(-1, 1)] public int lane;
    public WukongBeatNoteType type;
    [Min(1f)] public float warningBeats;
    [Range(0.25f, 2f)] public float strength;

    [Tooltip("Per-note seconds correction for a detected or manually reviewed musical attack.")]
    public float timingOffsetSeconds;

    public WukongBeatNote(float beat, int lane, WukongBeatNoteType type, float warningBeats, float strength)
    {
        this.timingOffsetSeconds = 0f;
        this.beat = Mathf.Max(0f, beat);
        this.lane = Mathf.Clamp(lane, -1, 1);
        this.type = type;
        this.warningBeats = Mathf.Max(2f, warningBeats);
        this.strength = Mathf.Clamp(strength, 0.25f, 2f);
    }
}

[CreateAssetMenu(menuName = "Wukong Rhythm/Song Definition", fileName = "WukongSong")]
public sealed class WukongSongDefinition : ScriptableObject
{
    public string songId = "song";
    public string title = "Untitled Song";
    public string artist = "Unknown";
    [Header("Chinese Localization")]
    public string titleZh;
    public string artistZh;
    public AudioClip audioClip;
    public AudioClip previewClip;

    [Min(40f)] public float bpm = 120f;
    [Tooltip("Seconds from the start of the clip to beat zero. Positive values move the beat grid later.")]
    public float beatOffsetSeconds;
    [Min(2f)] public float travelBeats = 6f;
    [Min(1)] public int beatsPerBar = 4;
    [HideInInspector] public int generatorVersion;
    [Tooltip("Prevents automatic regeneration of a reviewed or imported chart.")]
    public bool lockBeatmap;
    public bool auditoryReviewed;
    public string chartProvenance;
    [Tooltip("Detected musical beat positions in clip seconds. Shared by notes, spawning, warnings and HUD; supports tempo drift.")]
    public List<float> beatTimesSeconds = new List<float>();
    public List<WukongBeatNote> notes = new List<WukongBeatNote>();

    public float Duration => audioClip != null ? audioClip.length : 0f;
    public float BeatDuration => 60f / Mathf.Max(40f, bpm);

    public float TimeAtBeat(float beat)
    {
        if (beatTimesSeconds == null || beatTimesSeconds.Count < 2)
            return beatOffsetSeconds + beat * BeatDuration;
        int index = Mathf.Clamp(Mathf.FloorToInt(beat), 0, beatTimesSeconds.Count - 2);
        return Mathf.LerpUnclamped(beatTimesSeconds[index], beatTimesSeconds[index + 1], beat - index);
    }

    public float BeatAtTime(float seconds)
    {
        if (beatTimesSeconds == null || beatTimesSeconds.Count < 2)
            return (seconds - beatOffsetSeconds) / BeatDuration;
        int low = 0, high = beatTimesSeconds.Count - 1;
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (beatTimesSeconds[middle] <= seconds) low = middle; else high = middle;
        }
        return low + (seconds - beatTimesSeconds[low]) / Mathf.Max(.001f, beatTimesSeconds[low + 1] - beatTimesSeconds[low]);
    }

    public float SpawnTimeAtNote(WukongBeatNote note) => TimeAtBeat(note.beat - travelBeats);
    public float WarningTimeAtNote(WukongBeatNote note) => TimeAtBeat(note.beat - note.warningBeats);

    public float TimeAtNote(WukongBeatNote note) => TimeAtBeat(note.beat) + note.timingOffsetSeconds;

    public string LocalizedTitle(bool chinese)
    {
        return chinese && !string.IsNullOrWhiteSpace(titleZh) ? titleZh : title;
    }

    public string LocalizedArtist(bool chinese)
    {
        return chinese && !string.IsNullOrWhiteSpace(artistZh) ? artistZh : artist;
    }

    public void SortAndSanitize()
    {
        for (int i = 0; i < notes.Count; i++)
        {
            WukongBeatNote note = notes[i];
            note.beat = Mathf.Max(0f, note.beat);
            note.lane = Mathf.Clamp(note.lane, -1, 1);
            note.warningBeats = Mathf.Max(2f, note.warningBeats);
            note.strength = Mathf.Clamp(note.strength, 0.25f, 2f);
            notes[i] = note;
        }

        notes.Sort((left, right) => TimeAtNote(left).CompareTo(TimeAtNote(right)));
    }
}
