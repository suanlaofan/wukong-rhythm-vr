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
    private bool previousXr;

    private void LateUpdate()
    {
        if (game == null || game.hud == null) return;
        bool xr = UnityEngine.XR.XRSettings.isDeviceActive;
        int offset = Mathf.RoundToInt(game.inputTimingOffsetSeconds * 1000f);
        if (previousState < 0 || xr != previousXr) ConfigurePresentation(xr);
        if (previousState == (int)game.State && xr == previousXr && offset == previousOffset) return;
        previousState = (int)game.State;
        previousXr = xr;
        previousOffset = offset;
        if (bindingsLabel != null)
        {
            bindingsLabel.text = Bindings(game.State, xr);
            if (WukongRuntimeEnvironment.IsEmulator)
                bindingsLabel.text = SimulatorBindings(game.State);
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

    public static string Bindings(WukongRhythmGame.BattleState state, bool xr)
    {
        switch (state)
        {
            case WukongRhythmGame.BattleState.SongSelect:
                return xr ? "STICK SELECT   A START" : "↑↓ SELECT   ENTER START";
            case WukongRhythmGame.BattleState.Paused:
                return xr ? "A RESUME   HOLD B MUSIC   STICK ←→ CALIBRATE" : "ENTER RESUME   HOLD ESC MUSIC   ←→ CALIBRATE";
            case WukongRhythmGame.BattleState.Results:
                return xr ? "A REPLAY   B MUSIC" : "R REPLAY   ESC MUSIC";
            case WukongRhythmGame.BattleState.CountIn:
            case WukongRhythmGame.BattleState.Resuming:
                return "FOLLOW THE COUNT-IN · GET READY";
            case WukongRhythmGame.BattleState.Ending:
                return "TRACK COMPLETE · RESULTS COMING";
            default:
                return xr ? "SWING TO STRIKE   A THROW   B PAUSE" : "MOUSE MOVE   CLICK/SPACE STRIKE   T THROW   ESC PAUSE";
        }
    }

    public static string SimulatorBindings(WukongRhythmGame.BattleState state)
    {
        switch (state)
        {
            case WukongRhythmGame.BattleState.SongSelect:
                return "I/K SELECT   SPACE (A) START";
            case WukongRhythmGame.BattleState.Paused:
                return "SPACE RESUME   HOLD DELETE MUSIC   J/L CALIBRATE";
            case WukongRhythmGame.BattleState.Results:
                return "SPACE REPLAY   DELETE MUSIC";
            case WukongRhythmGame.BattleState.Playing:
                return "MOUSE MOVE RIGHT HAND   SPACE THROW   DELETE PAUSE";
            default:
                return Bindings(state, true);
        }
    }
}
