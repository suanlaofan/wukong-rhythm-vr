using UnityEngine;
using UnityEngine.UI;

/// <summary>Persistent controls outside the right-hand score panel.</summary>
public sealed class WukongControlsGuide : MonoBehaviour
{
    public WukongRhythmGame game;
    public Text titleLabel;
    public Text bindingsLabel;
    public RectTransform contentRoot;
    private int previousState=-1;
    private bool previousChinese, previousXr;

    private void LateUpdate()
    {
        if (game==null || game.hud==null) return;
        bool chinese=game.hud.IsChinese, xr=UnityEngine.XR.XRSettings.isDeviceActive;
        if(previousState<0 || xr!=previousXr) ConfigurePresentation(xr);
        if(previousState==(int)game.State && chinese==previousChinese && xr==previousXr)return;
        previousState=(int)game.State;previousChinese=chinese;previousXr=xr;
        if(titleLabel!=null)titleLabel.text=chinese?(xr?"操作提示 · 手柄":"操作提示 · 键盘与鼠标"):(xr?"CONTROLS · VR":"CONTROLS · KEYBOARD & MOUSE");
        if(bindingsLabel!=null)bindingsLabel.text=Bindings(game.State,xr,chinese);
    }

    private void ConfigurePresentation(bool xr)
    {
        var canvas=GetComponent<Canvas>();
        var scaler=GetComponent<CanvasScaler>();
        var spatial=GetComponent<WukongSpatialUi>();
        if(canvas==null || contentRoot==null)return;
        // Desktop guidance belongs on the screen so the courtyard cannot occlude
        // it. Headsets retain a world-space panel in front of the tracked player.
        if(spatial!=null)spatial.enabled=xr;
        canvas.renderMode=xr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;
        if(scaler!=null)
        {
            scaler.uiScaleMode=xr?CanvasScaler.ScaleMode.ConstantPixelSize:CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight=.5f;
        }
        contentRoot.anchorMin=contentRoot.anchorMax=contentRoot.pivot=xr?new Vector2(.5f,.5f):new Vector2(.5f,0f);
        contentRoot.sizeDelta=new Vector2(1500,190);
        contentRoot.anchoredPosition=xr?Vector2.zero:new Vector2(0,24);
        if(xr)
        {
            var root=(RectTransform)transform;
            root.sizeDelta=new Vector2(1500,190);
            root.localScale=Vector3.one*.00115f;
        }
    }

    public static string Bindings(WukongRhythmGame.BattleState state,bool xr,bool zh)
    {
        if(state==WukongRhythmGame.BattleState.SongSelect)
            return xr
                ?(zh?"右摇杆 ↑/↓ 选曲   A 开始   X 中英切换\n游戏中：移动右手挥棒   A 投掷   B 暂停":"RIGHT STICK ↑/↓ SELECT   A START   X LANGUAGE\nIN GAME: MOVE RIGHT HAND TO STRIKE   A THROW   B PAUSE")
                :(zh?"↑ / ↓ 选曲   Enter / T 开始   L 中英切换\n游戏中：鼠标移棒   左键 / 空格挥棒   T 投掷   Esc 暂停":"↑ / ↓ SELECT   ENTER / T START   L LANGUAGE\nIN GAME: MOUSE MOVE   CLICK / SPACE STRIKE   T THROW   ESC PAUSE");
        if(state==WukongRhythmGame.BattleState.Paused)
            return xr
                ?(zh?"A 继续   长按 B 返回选曲   X 中英切换\n右摇杆 ←/→ 调整时间补偿 · 每次 10 ms":"A RESUME   HOLD B MUSIC   X LANGUAGE\nRIGHT STICK ←/→ TIMING OFFSET · 10 ms PER STEP")
                :(zh?"Enter / T 继续   长按 Esc 返回选曲   L 中英切换\n← / → 调整时间补偿 · 每次 10 ms":"ENTER / T RESUME   HOLD ESC MUSIC   L LANGUAGE\n← / → TIMING OFFSET · 10 ms PER STEP");
        if(state==WukongRhythmGame.BattleState.Results)
            return xr
                ?(zh?"A 再玩一局   B 返回选曲   X 中英切换\n合圈时挥棒，或踩拍投掷，可获得节奏得分":"A REPLAY   B SELECT MUSIC   X LANGUAGE\nSTRIKE OR RELEASE YOUR THROW ON THE BEAT TO SCORE")
                :(zh?"Enter / T / R 再玩一局   Esc 返回选曲   L 中英切换\n合圈时挥棒，或踩拍投掷，可获得节奏得分":"ENTER / T / R REPLAY   ESC SELECT MUSIC   L LANGUAGE\nSTRIKE OR RELEASE YOUR THROW ON THE BEAT TO SCORE");
        if(state==WukongRhythmGame.BattleState.CountIn || state==WukongRhythmGame.BattleState.Resuming)
            return xr
                ?(zh?"请等待倒计时结束   X 中英切换\n游戏开始后：移动右手挥棒   A 投掷   B 暂停":"WAIT FOR THE COUNT-IN   X LANGUAGE\nAFTER COUNT-IN: MOVE RIGHT HAND TO STRIKE   A THROW   B PAUSE")
                :(zh?"请等待倒计时结束   L 中英切换\n游戏开始后：鼠标移棒   左键 / 空格挥棒   T 投掷   Esc 暂停":"WAIT FOR THE COUNT-IN   L LANGUAGE\nAFTER COUNT-IN: MOUSE MOVE   CLICK / SPACE STRIKE   T THROW   ESC PAUSE");
        if(state==WukongRhythmGame.BattleState.Ending)
            return zh?(xr?"曲目结束，正在结算 · X 中英切换":"曲目结束，正在结算 · L 中英切换"):(xr?"TRACK COMPLETE · PREPARING RESULTS · X LANGUAGE":"TRACK COMPLETE · PREPARING RESULTS · L LANGUAGE");
        return xr
            ?(zh?"移动右手 挥棒   A 投掷   B 暂停   X 中英切换\n长按 B 返回选曲 · 合圈时挥棒或出手投掷得分":"MOVE RIGHT HAND TO STRIKE   A THROW   B PAUSE   X LANGUAGE\nHOLD B MUSIC · STRIKE OR RELEASE YOUR THROW ON THE BEAT TO SCORE")
            :(zh?"鼠标 移棒   左键 / 空格 挥棒   T / Enter 投掷\nEsc / P 暂停   长按 Esc 选曲   L 中英切换 · 合圈时出手得分":"MOUSE MOVE   CLICK / SPACE STRIKE   T / ENTER THROW\nESC / P PAUSE   HOLD ESC MUSIC   L LANGUAGE · RELEASE ON THE BEAT TO SCORE");
    }
}
