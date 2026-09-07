using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WukongRhythmValidation
{
    private static int assertions;

    [MenuItem("Wukong Rhythm/Validation/Run Timing Regressions")]
    public static void Run()
    {
        assertions = 0;
        Check(WukongRhythmTiming.Judge(-0.5, .07, .14) == WukongTimingGrade.TooEarly, "early 500ms rejected");
        Check(WukongRhythmTiming.Judge(.5, .07, .14) == WukongTimingGrade.TooLate, "late 500ms rejected");
        foreach (int sign in new[] { -1, 1 })
        {
            Check(WukongRhythmTiming.Judge(sign * .07, .07, .14) == WukongTimingGrade.Perfect, "perfect inclusive boundary");
            Check(WukongRhythmTiming.Judge(sign * .0701, .07, .14) == WukongTimingGrade.Good, "perfect exterior");
            Check(WukongRhythmTiming.Judge(sign * .14, .07, .14) == WukongTimingGrade.Good, "good inclusive boundary");
            Check(WukongRhythmTiming.Judge(sign * .1401, .07, .14) == (sign < 0 ? WukongTimingGrade.TooEarly : WukongTimingGrade.TooLate), "good exterior");
        }
        Check(WukongRhythmTiming.Judge(double.NaN, .07, .14) == WukongTimingGrade.TooLate, "invalid timestamp rejected");
        Check(Math.Abs(WukongRhythmTiming.Accuracy(5, 5, 10) - 82.5f) < .001, "Good has lower accuracy weight");
        Check(Math.Abs(WukongRhythmTiming.Accuracy(5, 0, 10) - 50f) < .001, "misses remain in denominator");
        Check(WukongStaffController.SweepSphere(new Vector3(-2, 0, 0), new Vector3(2, 0, 0), .25f, out float fraction), "fast sweep crosses target");
        Check(Math.Abs(fraction - .4375f) < .0001f, "sweep timestamps entry not end of frame");
        Check(!WukongStaffController.SweepSphere(new Vector3(-2, 1, 0), new Vector3(2, 1, 0), .25f, out _), "off-target sweep rejected");
        // The same physical contact is delivered by different next render frames.
        // Reconstruct its timestamp from the sweep, including a frame beyond expiry.
        foreach (int fps in new[] { 45, 72, 90 })
        foreach (double error in new[] { -.2, -.139, -.069, 0, .069, .139, .2 })
        {
            double contact = 2 + error;
            double end = Math.Ceiling(contact * fps) / fps;
            double start = end - 1.0 / fps;
            double t = (contact - start) / (end - start);
            double reconstructed = start + t * (end - start);
            Check(WukongRhythmTiming.Judge(reconstructed - 2, .07, .14) == WukongRhythmTiming.Judge(error, .07, .14), "frame-independent stamped judgement " + fps);
        }
        ValidateScoreIdempotency();
        ValidateCharts();
        ValidateStrikeConsumption();
        string directory = Environment.GetEnvironmentVariable("WUKONG_AUDIT_DIR");
        if (string.IsNullOrEmpty(directory)) directory = Path.GetFullPath("Logs/WukongValidation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "timing-regressions.json"), "{\"status\":\"PASS\",\"assertions\":" + assertions + ",\"utc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
        Debug.Log("WUKONG_TIMING_REGRESSIONS_PASS assertions=" + assertions);
    }

    private static void ValidateScoreIdempotency()
    {
        GameObject root = new GameObject("Wukong Validation Temporary");
        try
        {
            WukongRhythmGame game = root.AddComponent<WukongRhythmGame>();
            WukongBeatRock rock = root.AddComponent<WukongBeatRock>();
            FieldInfo activeField = typeof(WukongRhythmGame).GetField("activeRocks", BindingFlags.Instance | BindingFlags.NonPublic);
            var active = (List<WukongBeatRock>)activeField.GetValue(game);
            active.Add(rock);
            game.ResolveHit(rock, -.5f);
            Check(active.Count == 1 && Value(game, "score") == 0, "invalid hit cannot consume note or score");
            game.ResolveHit(rock, 0f);
            int score = Value(game, "score");
            Check(score == 120 && Value(game, "perfectCount") == 1, "valid perfect counts once");
            game.ResolveHit(rock, 0f); game.ResolveMiss(rock);
            Check(Value(game, "score") == score && Value(game, "totalMiss") == 0, "duplicate callbacks cannot score or miss twice");
            active.Add(rock); game.ResolveMiss(rock); game.ResolveMiss(rock);
            Check(Value(game, "totalMiss") == 1 && Value(game, "combo") == 0, "miss consumes one note and resets combo");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateCharts()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:WukongSongDefinition"))
        {
            var song = AssetDatabase.LoadAssetAtPath<WukongSongDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            Check(song.audioClip != null && song.notes.Count > 0, "chart has audio and notes");
            float previous = -1f;
            var activeUntil = new Queue<float>();
            foreach (var note in song.notes)
            {
                float time = song.TimeAtNote(note);
                Check(!float.IsNaN(time) && !float.IsInfinity(time) && time > previous && time < song.Duration, "ordered valid audio targets");
                float spawn = time - song.travelBeats * song.BeatDuration;
                while (activeUntil.Count > 0 && activeUntil.Peek() < spawn) activeUntil.Dequeue();
                activeUntil.Enqueue(time + .4f);
                Check(activeUntil.Count <= 48, "chart fits prewarmed rock pool");
                previous = time;
            }
        }
    }

    private static void ValidateStrikeConsumption()
    {
        GameObject root = new GameObject("Wukong Strike Test");
        try
        {
            var staff = root.AddComponent<WukongStaffController>();
            staff.staffCollider = root.AddComponent<BoxCollider>();
            Set(staff,"swingActive",true); Set(staff,"swingId",1);
            Set(staff,"previousSampleDsp",2.0); Set(staff,"sampleDsp",2.02);
            Set(staff,"previousStaffTip",new Vector3(-2,0,0)); Set(staff,"lastStaffTip",new Vector3(2,0,0));
            Set(staff,"previousStaffCenter",new Vector3(-2,0,0)); Set(staff,"lastStaffCenter",new Vector3(2,0,0));
            Check(staff.TryGetStrike(Vector3.zero,Vector3.zero,.3f,out double stamp,out int action),"swept input creates a stamped contact");
            Check(stamp > 2 && stamp < 2.02,"contact interpolates inside sampling interval");
            Check(staff.ConsumeSwing(action),"fresh swing consumes once");
            Check(!staff.TryGetStrike(Vector3.zero,Vector3.zero,.3f,out _,out _),"one swing cannot farm multiple notes");
            staff.ResetContactHistory();
            Check(!staff.TryGetStrike(Vector3.zero,Vector3.zero,.3f,out _,out _),"reset cannot hit while stationary");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Set(object obj,string name,object value) => obj.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    private static int Value(WukongRhythmGame game, string field) => (int)typeof(WukongRhythmGame).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("WUKONG_TEST_FAILED " + label);
        assertions++;
    }
}
