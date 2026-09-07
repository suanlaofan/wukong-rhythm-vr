#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Regression acceptance on the actual scene, music and pooled targets.</summary>
public sealed class WukongRhythmRepairPlayValidation : MonoBehaviour
{
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private readonly List<string> checks=new List<string>();
    public static void Run()
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        new GameObject("Wukong Rhythm Repair Acceptance").AddComponent<WukongRhythmRepairPlayValidation>();
    }
    private IEnumerator Start()
    {
        Application.runInBackground=true;
        var game=FindFirstObjectByType<WukongRhythmGame>();
        Invoke(game,"ReturnToSongSelection");
        var mouse=UnityEngine.InputSystem.Mouse.current;
        Vector2 savedMouse=mouse!=null?mouse.position.ReadValue():Vector2.zero;
        if(mouse!=null)
        {
            foreach(Vector2 viewport in new[]{new Vector2(.25f,.4f),new Vector2(.75f,.6f)})
            {
                Rect pixels=game.playerCamera.pixelRect;
                UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(mouse.position,new Vector2(pixels.x+pixels.width*viewport.x,pixels.y+pixels.height*viewport.y));
                yield return null;
                // Input is now delivered through the same path as a mouse event.
                Invoke(game.staff,"UpdateDesktopHand");
                Vector3 tip=(Vector3)typeof(WukongStaffController).GetMethod("CalculateStaffTip",Flags).Invoke(game.staff,null);
                Vector3 screen=game.playerCamera.WorldToViewportPoint(tip);
                Check(Vector2.Distance(new Vector2(screen.x,screen.y),viewport)<.005f,"staff tip follows mouse across viewport "+viewport.x);
                Set(game.staff,"swingTime",.08f);
            }
            Set(game.staff,"swingTime",-1f);
            UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(mouse.position,savedMouse);
            yield return null;
        }
        Set(game,"selectedSongIndex",2);
        Invoke(game,"StartSelectedSong");
        double deadline=Time.realtimeSinceStartupAsDouble+20;
        while(game.State!=WukongRhythmGame.BattleState.Playing && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
        Check(game.State==WukongRhythmGame.BattleState.Playing,"real song scheduled successfully");
        var song=(WukongSongDefinition)Get(game,"currentSong");
        var music=(AudioSource)Get(game,"musicSource");
        Check(song.beatTimesSeconds.Count>2 && song.notes[0].timingOffsetSeconds==0,"scene plays rebuilt pulse chart");
        var note=song.notes[0];
        float spawn=song.SpawnTimeAtNote(note),warning=song.WarningTimeAtNote(note),target=song.TimeAtNote(note);
        while(game.SongTime<spawn+.03f) yield return null;
        var rocks=(List<WukongBeatRock>)Get(game,"activeRocks");
        var rock=rocks.Find(r=>Mathf.Abs(r.TargetSongTime-target)<.001f);
        Check(rock!=null,"pooled rock spawned at four-beat lead");
        Check(Vector3.Distance(rock.PositionAt(target),rock.TargetPosition)<.00001,"rock crosses warning center at musical pulse");
        var ring=(WukongHitWarningRing)Get(rock,"warningRing");
        var fixedRing=(LineRenderer)Get(ring,"targetRing");
        var approach=(LineRenderer)Get(ring,"approachRing");
        while(game.SongTime<warning+.04f) yield return null;
        Check(fixedRing.enabled && approach.enabled && approach.transform.localScale.x>1,"full two-beat warning visible before hit");
        Check(Mathf.Abs(fixedRing.GetPosition(0).magnitude-.28f)<.001f && approach.transform.localScale.x<=1.32f && Mathf.Abs(ring.transform.localScale.x-1)<.001f,"compact original ring size is independent of collider forgiveness");
        Check(Mathf.Abs(rock.GetComponent<SphereCollider>().radius*rock.transform.lossyScale.x-rock.HitRadius)<.0001,"runtime collider matches restored gameplay radius");

        var staff=game.staff;
        staff.enabled=false; rock.enabled=false;
        while(game.SongTime<target-.20f) yield return null;
        SetContact(staff,rock,game,7001);
        Invoke(rock,"Update");
        Check(!rock.IsResolved,"early contact does not destroy the rock");
        while(game.SongTime<target+.005f) yield return null;
        float actualError=game.SongTime-target;
        Check(actualError<game.PerfectWindow,"test contact delivered inside Perfect window");
        Invoke(ring,"Update");
        Check(Mathf.Abs(approach.transform.localScale.x-1)<.001,"approach and target circles meet on the same pulse");
        SetContact(staff,rock,game,7001);
        Invoke(rock,"Update");
        Check(rock.IsResolved,"same swing succeeds on beat after early overlap");
        int hits=(int)Get(game,"totalHit");
        Invoke(rock,"Update");
        Check((int)Get(game,"totalHit")==hits && hits==1,"single hit scores once");
        var impact=(AudioSource)Get(game,"impactSource");
        float peak=0; var samples=new float[512];
        double until=Time.realtimeSinceStartupAsDouble+.20;
        while(Time.realtimeSinceStartupAsDouble<until)
        {
            impact.GetOutputData(samples,0);
            foreach(float sample in samples)peak=Mathf.Max(peak,Mathf.Abs(sample));
            yield return null;
        }
        Check(peak>.001,"hit voice produces nonzero audio samples alongside music");
        Check(music.isPlaying,"hit effect does not interrupt the music");
        Check(game.staffImpactSound.length<.5f && game.rockShatterSound.length<.6f && staff.whooshSound.length<.4f,"gameplay sounds have short bounded duration");
        staff.enabled=true; rock.enabled=true;
        Invoke(game,"ReturnToSongSelection");
        string dir=Path.Combine(Environment.GetEnvironmentVariable("WUKONG_AUDIT_DIR")??"Logs/WukongValidation","rhythm-fix");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"repair-play-validation.json"),JsonUtility.ToJson(new ValidationReport { checks=checks, actualError=actualError, impactOutputPeak=peak, song=song.songId, utc=DateTime.UtcNow.ToString("O") },true));
        Debug.Log("WUKONG_RHYTHM_REPAIR_PLAY_PASS checks="+checks.Count);
        Destroy(gameObject);
    }
    [Serializable]
    private sealed class ValidationReport
    {
        public string status="PASS";
        public List<string> checks;
        public float actualError, impactOutputPeak;
        public string song, utc;
    }
    private static void SetContact(WukongStaffController staff,WukongBeatRock rock,WukongRhythmGame game,int action)
    {
        double now=AudioSettings.dspTime;
        Set(staff,"previousSampleDsp",now-.002);Set(staff,"sampleDsp",now);
        Set(staff,"swingActive",true);Set(staff,"swingId",action);
        var p=rock.PositionAt(game.MusicTimeAtDsp(now));
        foreach(string name in new[]{"previousStaffBase","lastStaffBase","previousStaffTip","lastStaffTip","previousStaffCenter","lastStaffCenter"})Set(staff,name,p);
    }
    private void Check(bool condition,string name)
    {
        if(!condition)throw new InvalidOperationException("WUKONG_RHYTHM_REPAIR_FAIL "+name);
        checks.Add(name);
    }
    private static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
    private static void Invoke(object o,string name)=>o.GetType().GetMethod(name,Flags).Invoke(o,null);
}
#endif
