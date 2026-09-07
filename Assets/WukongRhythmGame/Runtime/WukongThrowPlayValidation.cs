#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class WukongThrowPlayValidation : MonoBehaviour
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private readonly List<string> checks=new List<string>();
    private WukongRhythmGame game;
    public static void Run()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        new GameObject("Wukong Throw Acceptance").AddComponent<WukongThrowPlayValidation>();
    }
    private IEnumerator Start()
    {
        game=FindFirstObjectByType<WukongRhythmGame>();
        Application.runInBackground=true;
        var staff=game.staff;
        Vector3 heldScale=staff.transform.localScale;
        var guide=FindFirstObjectByType<WukongControlsGuide>();
        Check(guide!=null && guide.bindingsLabel!=null,"persistent controls panel exists");
        yield return null;
        Check(guide.GetComponent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"desktop controls render above scene geometry");
        var corners=new Vector3[4];guide.contentRoot.GetWorldCorners(corners);
        Check(corners[0].x>=0 && corners[0].y>=0 && corners[2].x<=Screen.width && corners[2].y<=Screen.height,"controls fit inside the game viewport");
        foreach(WukongRhythmGame.BattleState state in Enum.GetValues(typeof(WukongRhythmGame.BattleState)))
        foreach(bool xr in new[]{false,true})foreach(bool zh in new[]{false,true})
            Check(!string.IsNullOrWhiteSpace(WukongControlsGuide.Bindings(state,xr,zh)),"controls mapped for "+state+" xr="+xr+" zh="+zh);
        Check(Mathf.Abs(game.hud.GetComponent<WukongSpatialUi>().headRelativeOffset.y-.16f)<.001,"right panel raised 12 cm");
        if(Mouse.current!=null)
        {
            Rect p=game.playerCamera.pixelRect;
            InputSystem.QueueDeltaStateEvent(Mouse.current.position,new Vector2(p.x+p.width*.78f,p.y+p.height*.65f));
            yield return null;
        }

        yield return StartSong();
        var song=(WukongSongDefinition)Get(game,"currentSong");
        float first=song.TimeAtNote(song.notes[0]);
        yield return WaitUntilSong(first-.025f);
        Check(Keyboard.current!=null,"desktop keyboard input present");
        int previousAction=staff.SwingId;
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.T));
        yield return null;
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
        Check(staff.IsThrown && staff.SwingId>previousAction,"T key starts an independent throw action");
        yield return WaitForReturn();
        Check((int)Get(game,"totalHit")==1,"real on-beat throw trajectory shatters and scores one rock");
        Check(staff.transform.parent==staff.handAnchor && (staff.transform.localScale-heldScale).sqrMagnitude<.000001,"enlarged staff returns to hand without scale drift");

        yield return StartSong();
        yield return WaitUntilSong(first-.65f);
        var target=(Transform)Invoke(game,"FindThrowTarget");
        Check(target!=null,"early ranged throw acquires a rock");
        int used=staff.SwingId;
        Set(staff,"consumedSwingId",used);Set(staff,"swingActive",true);
        Check(staff.BeginThrow(target) && staff.SwingId>used,"throw after an already-consumed swing remains hittable");
        yield return WaitForReturn();
        Check((int)Get(game,"totalHit")==0 && (int)Get(game,"totalMiss")==1,"off-beat physical throw breaks one rock without awarding rhythm points");
        Check((int)Get(game,"totalHit")+(int)Get(game,"totalMiss")==1,"outbound and return cannot count the same throw twice");
        Check(staff.BeginThrow(null),"empty-space throw starts");
        game.PauseBattle();
        Check(!staff.IsThrown && staff.transform.parent==staff.handAnchor,"pause recalls an in-flight staff");
        yield return null;
        Check(guide.bindingsLabel.text.Contains("继续")||guide.bindingsLabel.text.Contains("RESUME"),"persistent guide switches to pause controls");
        Invoke(game,"ReturnToSongSelection");
        yield return null;
        Check(guide.bindingsLabel.text.Contains("选曲")||guide.bindingsLabel.text.Contains("SELECT"),"persistent guide switches to song selection controls");
        string dir=Path.Combine(Environment.GetEnvironmentVariable("WUKONG_AUDIT_DIR")??"Logs/WukongValidation","throw-ui");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"throw-play-validation.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {status="PASS",checks,utc=DateTime.UtcNow},Newtonsoft.Json.Formatting.Indented));
        Debug.Log("WUKONG_THROW_PLAY_PASS checks="+checks.Count);
        Destroy(gameObject);
    }
    private IEnumerator StartSong()
    {
        Invoke(game,"ReturnToSongSelection");Set(game,"selectedSongIndex",2);Invoke(game,"StartSelectedSong");
        double timeout=Time.realtimeSinceStartupAsDouble+20;
        while(game.State!=WukongRhythmGame.BattleState.Playing && Time.realtimeSinceStartupAsDouble<timeout)yield return null;
        Check(game.State==WukongRhythmGame.BattleState.Playing,"real song enters gameplay");
    }
    private IEnumerator WaitUntilSong(float time)
    {
        double timeout=Time.realtimeSinceStartupAsDouble+15;
        while(game.SongTime<time && Time.realtimeSinceStartupAsDouble<timeout)yield return null;
        Check(game.State==WukongRhythmGame.BattleState.Playing && game.SongTime>=time,"playback reaches throw cue");
    }
    private IEnumerator WaitForReturn()
    {
        double timeout=Time.realtimeSinceStartupAsDouble+2;
        while(game.staff.IsThrown && Time.realtimeSinceStartupAsDouble<timeout)yield return null;
        Check(!game.staff.IsThrown,"throw completes outbound and return");
    }
    private void Check(bool value,string message)
    {
        if(!value)throw new InvalidOperationException("WUKONG_THROW_PLAY_FAIL "+message);
        checks.Add(message);
    }
    private static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    private static object Invoke(object o,string n)=>o.GetType().GetMethod(n,Flags).Invoke(o,null);
}
#endif
