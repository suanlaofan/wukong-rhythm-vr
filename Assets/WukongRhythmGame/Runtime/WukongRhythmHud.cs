using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum WukongHudRating
{
    None,
    Perfect,
    Good,
    Miss,
    Early
}

public sealed class WukongRhythmHud : MonoBehaviour
{
    private enum HudMode
    {
        SongSelect,
        Gameplay,
        Results
    }

    [Header("Mode Roots")]
    public GameObject songSelectContent;
    public GameObject gameplayContent;
    public GameObject resultsContent;

    [Header("Song Selection")]
    public Text musicLibraryLabel;
    public Text songSelectTitle;
    public Text songSelectSubtitle;
    public Text[] songRows;
    public Text songDetails;
    public Text songSelectHint;

    [Header("Gameplay Labels")]
    public Text nowPlayingLabel;
    public Text scoreCaption;
    public Text comboCaption;
    public Text accuracyCaption;
    public Text rhythmCaption;
    public Text operationCaption;

    [Header("Gameplay Values")]
    public Text songNameValue;
    public Text phaseValue;
    public Text bpmValue;
    public Text beatValue;
    public Image beatProgress;
    public Image[] beatMarkers;
    public Text scoreValue;
    public Text comboValue;
    public Text accuracyValue;
    public Text operationHint;

    [Header("Rating State")]
    public Text ratingIdleLabel;
    public RawImage perfectCard;
    public RawImage goodCard;
    public RawImage missCard;
    public Text ratingLabel;

    [Header("Results")]
    public Text resultEyebrow;
    public Text resultTitle;
    public Text resultScore;
    public Text resultAccuracy;
    public Text resultCombo;
    public Text resultBreakdown;
    public Text resultHint;

    private CanvasGroup ratingGroup;
    private IList<WukongSongDefinition> displayedSongs;
    private WukongSongDefinition displayedSong;
    private HudMode mode;
    private WukongHudRating activeRating;
    private float ratingHideAt;
    private float ratingAnimationStarted;
    private bool hasGameplaySnapshot;
    private int displayedSelectedIndex;
    private int countInValue = -1;
    private int cachedScore;
    private int cachedCombo;
    private float cachedAccuracy = 100f;
    private int cachedPhase = 1;
    private float cachedBpm = 120f;
    private float cachedBeat;
    private bool forceGameplayText = true;
    private bool pausedOverlay;
    private float displayedOffset;
    private int resultScoreValue;
    private float resultAccuracyValue;
    private int resultComboValue;
    private int resultPerfectValue;
    private int resultGoodValue;
    private int resultMissValue;

    private void Awake()
    {
        ratingGroup = ratingLabel != null ? ratingLabel.GetComponentInParent<CanvasGroup>() : null;
        if (ratingGroup == null && ratingLabel != null)
        {
            ratingGroup = ratingLabel.gameObject.AddComponent<CanvasGroup>();
        }

        ClearRating();
        RefreshLocalizedText();
    }

    private void Update()
    {
        if (ratingGroup == null || activeRating == WukongHudRating.None)
        {
            return;
        }

        float elapsed = Time.unscaledTime - ratingAnimationStarted;
        float fadeIn = Mathf.Clamp01(elapsed / 0.12f);
        float fadeOut = ratingHideAt > 0f
            ? Mathf.Clamp01((ratingHideAt - Time.unscaledTime) / 0.18f)
            : 1f;
        ratingGroup.alpha = fadeIn * fadeOut;
        float scale = Mathf.Lerp(1.08f, 1f, Mathf.SmoothStep(0f, 1f, fadeIn));
        if (ratingLabel != null)
        {
            ratingLabel.transform.localScale = Vector3.one * scale;
        }

        if (ratingHideAt > 0f && Time.unscaledTime >= ratingHideAt)
        {
            ClearRating();
        }
    }

    public void ShowSongSelect(IList<WukongSongDefinition> songs, int selectedIndex)
    {
        mode = HudMode.SongSelect;
        displayedSongs = songs;
        displayedSelectedIndex = selectedIndex;
        displayedSong = songs != null && songs.Count > 0
            ? songs[Mathf.Clamp(selectedIndex, 0, songs.Count - 1)]
            : null;
        SetMode(songSelectContent, true);
        SetMode(gameplayContent, false);
        SetMode(resultsContent, false);
        RefreshLocalizedText();
    }

    public void ShowGameplay(WukongSongDefinition song)
    {
        mode = HudMode.Gameplay;
        pausedOverlay = false;
        forceGameplayText = true;
        displayedSong = song;
        countInValue = -1;
        SetMode(songSelectContent, false);
        SetMode(gameplayContent, true);
        SetMode(resultsContent, false);
        ClearRating();
        RefreshLocalizedText();
    }

    public void ShowCountIn(int count)
    {
        pausedOverlay = false;
        countInValue = count;
        if (phaseValue != null)
        {
            phaseValue.text = "READY  " + count;
        }
    }

    public void UpdateGameplay(int score, int combo, float accuracy, int phase, float bpm, float beat, float beatProgressValue)
    {
        bool first = forceGameplayText || !hasGameplaySnapshot;
        if (scoreValue != null && (first || cachedScore != score)) scoreValue.text = score.ToString("D6");
        if (comboValue != null && (first || cachedCombo != combo)) comboValue.text = combo.ToString("D3");
        if (accuracyValue != null && (first || Mathf.RoundToInt(cachedAccuracy) != Mathf.RoundToInt(accuracy))) accuracyValue.text = accuracy.ToString("0") + "%";
        if (bpmValue != null && (first || !Mathf.Approximately(cachedBpm, bpm))) bpmValue.text = "BPM " + bpm.ToString("0");
        if (beatValue != null && (first || Mathf.FloorToInt(cachedBeat) != Mathf.FloorToInt(beat))) beatValue.text = "BEAT " + (Mathf.FloorToInt(beat) + 1);
        if (phaseValue != null && (first || countInValue > 0 || cachedPhase != phase)) phaseValue.text = "PHASE " + phase;
        if (beatProgress != null) beatProgress.fillAmount = Mathf.Clamp01(beatProgressValue);
        cachedScore = score; cachedCombo = combo; cachedAccuracy = accuracy;
        cachedPhase = phase; cachedBpm = bpm; cachedBeat = beat;
        hasGameplaySnapshot = true; forceGameplayText = false; countInValue = -1;

        if (beatMarkers == null || beatMarkers.Length == 0)
        {
            return;
        }

        int activeMarker = Mathf.FloorToInt(Mathf.Max(0f, beat)) % beatMarkers.Length;
        for (int i = 0; i < beatMarkers.Length; i++)
        {
            Image marker = beatMarkers[i];
            if (marker == null)
            {
                continue;
            }

            bool active = i == activeMarker;
            marker.color = active
                ? new Color(0.52f, 0.98f, 1f, 0.96f)
                : new Color(0.66f, 0.82f, 0.9f, 0.24f);
            float pulseScale = active ? Mathf.Lerp(1.22f, 1f, beatProgressValue) : 1f;
            marker.rectTransform.localScale = Vector3.one * pulseScale;
        }
    }

    public void ShowPaused(float offset)
    {
        pausedOverlay = true; displayedOffset = offset;
        ClearRating();
        SetText(phaseValue, "PAUSED");
        SetText(operationHint, "A / ENTER  RESUME     HOLD B / ESC  MUSIC\nLEFT / RIGHT  TIMING " + (offset * 1000f).ToString("+0;-0;0") + " ms");
    }

    public void ShowTimingDetail(float error)
    {
        if (ratingLabel != null && activeRating == WukongHudRating.Good)
            ratingLabel.text = RatingText(activeRating) + (error < 0 ? " · EARLY" : " · LATE");
    }

    public void ShowRating(WukongHudRating rating, float duration)
    {
        ClearRating();
        activeRating = rating;
        ratingAnimationStarted = Time.unscaledTime;
        ratingHideAt = ratingAnimationStarted + Mathf.Max(0.1f, duration);
        if (ratingIdleLabel != null)
        {
            ratingIdleLabel.gameObject.SetActive(false);
        }

        RawImage card = CardFor(rating);
        if (card != null)
        {
            card.gameObject.SetActive(true);
        }
        if (ratingLabel != null)
        {
            ratingLabel.gameObject.SetActive(true);
            ratingLabel.text = RatingText(rating);
        }
        if (ratingGroup != null)
        {
            ratingGroup.alpha = 0f;
        }
    }

    public void ShowResults(int score, float accuracy, int maxCombo, int perfect, int good, int miss)
    {
        mode = HudMode.Results;
        resultScoreValue = score;
        resultAccuracyValue = accuracy;
        resultComboValue = maxCombo;
        resultPerfectValue = perfect;
        resultGoodValue = good;
        resultMissValue = miss;
        SetMode(songSelectContent, false);
        SetMode(gameplayContent, false);
        SetMode(resultsContent, true);
        ClearRating();
        RefreshLocalizedText();
    }

    public void ClearRating()
    {
        activeRating = WukongHudRating.None;
        ratingHideAt = 0f;
        if (ratingGroup != null)
        {
            ratingGroup.alpha = 0f;
        }
        SetCardActive(perfectCard, false);
        SetCardActive(goodCard, false);
        SetCardActive(missCard, false);
        if (ratingLabel != null)
        {
            ratingLabel.gameObject.SetActive(false);
        }
        if (ratingIdleLabel != null)
        {
            ratingIdleLabel.gameObject.SetActive(true);
            ratingIdleLabel.text = "RHYTHM READY";
        }
    }

    private void RefreshLocalizedText()
    {
        forceGameplayText = true;
        SetText(musicLibraryLabel, "MUSIC LIBRARY");
        SetText(songSelectTitle, "SELECT MUSIC");
        SetText(songSelectSubtitle, "CHOOSE A TRACK TO BEGIN");
        SetText(nowPlayingLabel, "NOW PLAYING");
        SetText(scoreCaption, "SCORE");
        SetText(comboCaption, "COMBO");
        SetText(accuracyCaption, "TIMING");
        SetText(rhythmCaption, "RHYTHM");
        SetText(operationCaption, "CONTROLS");
        SetText(resultEyebrow, "SESSION RESULTS");

        switch (mode)
        {
            case HudMode.SongSelect:
                RefreshSongSelectionText();
                break;
            case HudMode.Gameplay:
                if (pausedOverlay) ShowPaused(displayedOffset); else RefreshGameplayText();
                break;
            case HudMode.Results:
                RefreshResultsText();
                break;
        }

        if (activeRating == WukongHudRating.None)
        {
            SetText(ratingIdleLabel, "RHYTHM READY");
        }
        else
        {
            SetText(ratingLabel, RatingText(activeRating));
        }
    }

    private void RefreshSongSelectionText()
    {
        SetText(songSelectHint, UnityEngine.XR.XRSettings.isDeviceActive
            ? "STICK UP / DOWN SELECT   A START"
            : "UP / DOWN SELECT   ENTER / T START");

        for (int i = 0; i < (songRows != null ? songRows.Length : 0); i++)
        {
            Text row = songRows[i];
            if (row == null)
            {
                continue;
            }

            int songIndex = displayedSelectedIndex - songRows.Length / 2 + i;
            if (displayedSongs == null || songIndex < 0 || songIndex >= displayedSongs.Count || displayedSongs[songIndex] == null)
            {
                row.text = string.Empty;
                continue;
            }

            WukongSongDefinition song = displayedSongs[songIndex];
            row.text = (songIndex == displayedSelectedIndex ? "> " : "  ")
                + song.title + "   " + song.bpm.ToString("0") + " BPM";
            row.color = songIndex == displayedSelectedIndex
                ? new Color(0.62f, 0.95f, 1f, 1f)
                : new Color(0.9f, 0.94f, 1f, 0.78f);
        }

        if (songDetails == null)
        {
            return;
        }

        songDetails.text = displayedSong == null
            ? "NO SONG MAP"
            : displayedSong.artist + "\n"
                + displayedSong.bpm.ToString("0") + " BPM   "
                + displayedSong.notes.Count + " NOTES";
    }

    private void RefreshGameplayText()
    {
        if (displayedSong != null)
        {
            SetText(songNameValue, displayedSong.title.ToUpperInvariant());
        }
        SetText(operationHint, UnityEngine.XR.XRSettings.isDeviceActive
            ? "STRIKE WHEN THE RINGS MEET · A THROW\nB PAUSE"
            : "MOUSE MOVE · CLICK / SPACE HIT\nT THROW · ESC PAUSE");

        if (countInValue > 0)
        {
            SetText(phaseValue, "READY  " + countInValue);
        }
        else if (hasGameplaySnapshot)
        {
            UpdateGameplay(
                cachedScore,
                cachedCombo,
                cachedAccuracy,
                cachedPhase,
                cachedBpm,
                cachedBeat,
                beatProgress != null ? beatProgress.fillAmount : 0f);
        }
        else
        {
            SetText(phaseValue, "PHASE 1");
        }
    }

    private void RefreshResultsText()
    {
        SetText(resultTitle, "BATTLE COMPLETE");
        SetText(resultScore, "SCORE  " + resultScoreValue.ToString("D6"));
        SetText(resultAccuracy, "TIMING  " + resultAccuracyValue.ToString("0") + "%");
        SetText(resultCombo, "MAX COMBO  " + resultComboValue.ToString("D3"));
        SetText(resultBreakdown,
            "PERFECT " + resultPerfectValue + "    GOOD " + resultGoodValue + "    MISS " + resultMissValue);
        SetText(resultHint, UnityEngine.XR.XRSettings.isDeviceActive
            ? "A REPLAY   B SELECT MUSIC"
            : "ENTER / T / R REPLAY   ESC MUSIC");
    }

    private string RatingText(WukongHudRating rating)
    {
        switch (rating)
        {
            case WukongHudRating.Perfect: return "PERFECT";
            case WukongHudRating.Good: return "GOOD";
            case WukongHudRating.Miss: return "MISS";
            case WukongHudRating.Early: return "TOO EARLY";
            default: return string.Empty;
        }
    }

    private RawImage CardFor(WukongHudRating rating)
    {
        switch (rating)
        {
            case WukongHudRating.Perfect: return perfectCard;
            case WukongHudRating.Good: return goodCard;
            case WukongHudRating.Miss: return missCard;
            default: return null;
        }
    }

    private static void SetCardActive(RawImage card, bool active)
    {
        if (card != null)
        {
            card.gameObject.SetActive(active);
        }
    }

    private static void SetMode(GameObject modeRoot, bool active)
    {
        if (modeRoot != null)
        {
            modeRoot.SetActive(active);
        }
    }

    private static void SetText(Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }
}
