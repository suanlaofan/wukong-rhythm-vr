using UnityEngine;
using UnityEngine.UI;

/// <summary>A small, state-specific row of controls above the play area.</summary>
public sealed class WukongControlsGuide : MonoBehaviour
{
    public static readonly Vector2 ReferenceSize = new Vector2(1040f, 36f);
    public static readonly Vector3 XrOffset = new Vector3(0f, .92f, 1.95f);
    public const float PixelsToMeters = .00125f;

    public WukongRhythmGame game;
    public Text bindingsLabel;
    public RectTransform contentRoot;
    private int previousState = -1;
    private int previousOffset = int.MinValue;
    private bool previousChinese, previousXr;

    private void LateUpdate()
    {
        if (game == null || game.hud == null) return;
        bool chinese = game.hud.IsChinese;
        bool xr = UnityEngine.XR.XRSettings.isDeviceActive;
        int offset = Mathf.RoundToInt(game.inputTimingOffsetSeconds * 1000f);
        if (previousState < 0 || xr != previousXr) ConfigurePresentation(xr);
        if (previousState == (int)game.State && chinese == previousChinese && xr == previousXr && offset == previousOffset) return;
        previousState = (int)game.State;
        previousChinese = chinese;
        previousXr = xr;
        previousOffset = offset;
        if (bindingsLabel != null)
        {
            bindingsLabel.text = Bindings(game.State, xr, chinese);
            if (game.State == WukongRhythmGame.BattleState.Paused)
                bindingsLabel.text += " " + offset.ToString("+0;-0;0") + " ms";
        }
    }

    private void ConfigurePresentation(bool xr)
    {
        var canvas = GetComponent<Canvas>();
        var scaler = GetComponent<CanvasScaler>();
        var spatial = GetComponent<WukongSpatialUi>();
        if (canvas == null || contentRoot == null) return;
        // Desktop text stays clear of scene geometry; headsets use spatial text.
        if (spatial != null) spatial.enabled = xr;
        canvas.renderMode = xr ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        if (scaler != null)
        {
            scaler.uiScaleMode = xr ? CanvasScaler.ScaleMode.ConstantPixelSize : CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
        }
        contentRoot.anchorMin = contentRoot.anchorMax = contentRoot.pivot = xr ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
        contentRoot.sizeDelta = ReferenceSize;
        // Leave a safe inset, including when the Editor Game View is zoomed slightly.
        contentRoot.anchoredPosition = xr ? Vector2.zero : new Vector2(0, -72);
        if (xr)
        {
            var root = (RectTransform)transform;
            root.sizeDelta = ReferenceSize;
            root.localScale = Vector3.one * PixelsToMeters;
        }
    }

    public static string Bindings(WukongRhythmGame.BattleState state, bool xr, bool zh)
    {
        switch (state)
        {
            case WukongRhythmGame.BattleState.SongSelect:
                return xr
                    ? (zh ? "右摇杆选曲   A 开始   X 中英" : "STICK SELECT   A START   X LANGUAGE")
                    : (zh ? "↑↓ 选曲   Enter 开始   L 中英" : "↑↓ SELECT   ENTER START   L LANGUAGE");
            case WukongRhythmGame.BattleState.Paused:
                return xr
                    ? (zh ? "A 继续   长按 B 选曲   摇杆 ←→ 校准" : "A RESUME   HOLD B MUSIC   STICK ←→ CALIBRATE")
                    : (zh ? "Enter 继续   长按 Esc 选曲   ←→ 校准" : "ENTER RESUME   HOLD ESC MUSIC   ←→ CALIBRATE");
            case WukongRhythmGame.BattleState.Results:
                return xr
                    ? (zh ? "A 重玩   B 选曲   X 中英" : "A REPLAY   B MUSIC   X LANGUAGE")
                    : (zh ? "R 重玩   Esc 选曲   L 中英" : "R REPLAY   ESC MUSIC   L LANGUAGE");
            case WukongRhythmGame.BattleState.CountIn:
            case WukongRhythmGame.BattleState.Resuming:
                return zh ? "跟随倒计时，准备出手" : "FOLLOW THE COUNT-IN · GET READY";
            case WukongRhythmGame.BattleState.Ending:
                return zh ? "曲目结束，正在结算" : "TRACK COMPLETE · RESULTS COMING";
            default:
                return xr
                    ? (zh ? "挥动右手击打   A 投掷   B 暂停" : "SWING TO STRIKE   A THROW   B PAUSE")
                    : (zh ? "鼠标移棒   左键/空格击打   T 投掷   Esc 暂停" : "MOUSE MOVE   CLICK/SPACE STRIKE   T THROW   ESC PAUSE");
        }
    }
}
