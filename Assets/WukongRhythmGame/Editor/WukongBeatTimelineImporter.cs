using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Imports offline pulse tracking without coupling the player to Python.</summary>
public static class WukongBeatTimelineImporter
{
    [Serializable] private class Chart
    {
        public string path;
        public float bpm, beatOffsetSeconds, travelBeats;
        public List<float> beatTimesSeconds;
        public List<WukongBeatNote> notes;
    }
    [Serializable] private class Charts { public List<Chart> songs; }

    public static int Import(string jsonPath)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop gameplay before importing charts.");
        var charts=JsonUtility.FromJson<Charts>("{\"songs\":"+File.ReadAllText(jsonPath)+"}").songs;
        // Validate the entire batch before changing any assets.
        foreach(var chart in charts)
        {
            if(AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(chart.path)==null || chart.beatTimesSeconds==null || chart.beatTimesSeconds.Count<2 || chart.notes==null || chart.notes.Count==0)
                throw new InvalidDataException("Invalid chart: "+chart.path);
            float previous=-1;
            foreach(float time in chart.beatTimesSeconds)
            {
                if(float.IsNaN(time)||float.IsInfinity(time)||time<=previous) throw new InvalidDataException("Non-increasing beat times: "+chart.path);
                previous=time;
            }
            foreach(var note in chart.notes)
                if(float.IsNaN(note.beat)||note.beat<0||note.beat>=chart.beatTimesSeconds.Count||note.timingOffsetSeconds!=0)
                    throw new InvalidDataException("Note must point to a measured pulse: "+chart.path);
        }
        foreach(var chart in charts)
        {
            var song=AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(chart.path);
            song.bpm=chart.bpm; song.beatOffsetSeconds=chart.beatOffsetSeconds; song.travelBeats=chart.travelBeats;
            song.beatTimesSeconds=chart.beatTimesSeconds; song.notes=chart.notes;
            song.generatorVersion=5; song.lockBeatmap=true; song.auditoryReviewed=false;
            song.chartProvenance="2026-09-07 percussion pulse tracking; shared beat timeline; pending listening review";
            song.SortAndSanitize(); EditorUtility.SetDirty(song);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("WUKONG_BEAT_TIMELINES_IMPORTED "+charts.Count);
        return charts.Count;
    }
}
