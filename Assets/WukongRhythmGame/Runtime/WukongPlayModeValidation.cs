#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only acceptance against the real loaded gameplay scene.</summary>
public sealed class WukongPlayModeValidation : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private int checks;
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        Application.runInBackground = true;
        new GameObject("Wukong Play Acceptance").AddComponent<WukongPlayModeValidation>();
    }
    private IEnumerator Start()
    {
        Application.runInBackground = true;
        var game = FindFirstObjectByType<WukongRhythmGame>();
        yield return null;
        Invoke(game,"ReturnToSongSelection");
        Invoke(game,"StartSelectedSong");
        double timeout = Time.realtimeSinceStartupAsDouble + 25;
        while(game.State != WukongRhythmGame.BattleState.Playing && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
        Check(game.State == WukongRhythmGame.BattleState.Playing,"scheduled music enters gameplay");
        yield return new WaitForSecondsRealtime(1);
        var source=(AudioSource)typeof(WukongRhythmGame).GetField("musicSource",Flags).GetValue(game);
        Check(source.isPlaying,"music source playing");
        float startError=Mathf.Abs(source.time-game.SongTime);
        Check(startError < .05f,"music and chart clock agree within 50 ms");
        game.PauseBattle();
        float frozen=game.SongTime;
        yield return new WaitForSecondsRealtime(.3f);
        Check(game.State == WukongRhythmGame.BattleState.Paused && Mathf.Abs(game.SongTime-frozen)<.0001f && !source.isPlaying,"pause freezes music and chart");
        var resume=(IEnumerator)typeof(WukongRhythmGame).GetMethod("ResumeBattle",Flags).Invoke(game,null);
        game.StartCoroutine(resume);
        yield return null;
        Check(game.State == WukongRhythmGame.BattleState.Resuming && Mathf.Abs(game.SongTime-frozen)<.0001f,"resume count-in freezes targets");
        timeout=Time.realtimeSinceStartupAsDouble+12;
        while(game.State == WukongRhythmGame.BattleState.Resuming && Time.realtimeSinceStartupAsDouble<timeout) yield return null;
        yield return new WaitForSecondsRealtime(.5f);
        float resumeError=Mathf.Abs(source.time-game.SongTime);
        Check(game.State == WukongRhythmGame.BattleState.Playing && source.isPlaying && resumeError < .05f,"resume seeks and resynchronizes audio");
        int particles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length;
        int bodies=FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
        int lights=FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        for(int i=0;i<100;i++) game.SpawnRockExplosion(game.transform.position,i%2==0);
        Check(particles==FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length && bodies==FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length && lights==FindObjectsByType<Light>(FindObjectsSortMode.None).Length,"100 hits keep effect objects bounded");
        Invoke(game,"OnAudioConfigurationChanged",true);
        Check(game.State==WukongRhythmGame.BattleState.Paused,"audio reconfiguration pauses gameplay");
        Invoke(game,"ReturnToSongSelection");
        Check(game.State==WukongRhythmGame.BattleState.SongSelect && !source.isPlaying && FindObjectsByType<WukongBeatRock>(FindObjectsSortMode.None).Length==0,"exit clears live notes and music");
        string dir=Environment.GetEnvironmentVariable("WUKONG_AUDIT_DIR");
        if(string.IsNullOrEmpty(dir))dir=Path.GetFullPath("Logs/WukongValidation");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"play-mode-validation.json"),"{\"status\":\"PASS\",\"checks\":"+checks+",\"startClockErrorSeconds\":"+startError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"resumeClockErrorSeconds\":"+resumeError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"particles\":"+particles+",\"rigidbodies\":"+bodies+",\"lights\":"+lights+"}");
        Debug.Log("WUKONG_PLAY_ACCEPTANCE_PASS checks="+checks);
        Destroy(gameObject);
    }
    private void Check(bool condition,string name)
    {
        if(!condition) throw new InvalidOperationException("WUKONG_PLAY_ACCEPTANCE_FAIL: "+name);
        checks++;
    }
    private static void Invoke(object target,string method,params object[] args) => target.GetType().GetMethod(method,Flags).Invoke(target,args);
}

#endif
