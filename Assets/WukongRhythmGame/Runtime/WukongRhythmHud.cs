using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum WukongHudRating
{
    None,
    Perfect,
    Good,
    Miss
}

public sealed class WukongRhythmHud : MonoBehaviour
{
    private const string LanguagePreferenceKey = "Wukong.UiLanguage";

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
    private bool isChinese;
    private bool hasGameplaySnapshot;
    private int displayedSelectedIndex;
    private int countInValue = -1;
    private int cachedScore;
    private int cachedCombo;
    private float cachedAccuracy = 100f;
    private int cachedPhase = 1;
    private float cachedBpm = 120f;
    private float cachedBeat;
    private int resultScoreValue;
    private float resultAccuracyValue;
    private int resultComboValue;
    private int resultPerfectValue;
    private int resultGoodValue;
    private int resultMissValue;

    public bool IsChinese => isChinese;

    private void Awake()
    {
        isChinese = PlayerPrefs.GetInt(LanguagePreferenceKey, 0) == 1;
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

    public void ToggleLanguage()
    {
        isChinese = !isChinese;
        PlayerPrefs.SetInt(LanguagePreferenceKey, isChinese ? 1 : 0);
        PlayerPrefs.Save();
        RefreshLocalizedText();
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
        countInValue = count;
        if (phaseValue != null)
        {
            phaseValue.text = Localize("READY  " + count, "准备  " + count);
        }
    }

    public void UpdateGameplay(int score, int combo, float accuracy, int phase, float bpm, float beat, float beatProgressValue)
    {
        cachedScore = score;
        cachedCombo = combo;
        cachedAccuracy = accuracy;
        cachedPhase = phase;
        cachedBpm = bpm;
        cachedBeat = beat;
        hasGameplaySnapshot = true;
        countInValue = -1;

        if (scoreValue != null) scoreValue.text = score.ToString("D6");
        if (comboValue != null) comboValue.text = combo.ToString("D3");
        if (accuracyValue != null) accuracyValue.text = accuracy.ToString("0") + "%";
        if (bpmValue != null) bpmValue.text = "BPM " + bpm.ToString("0");
        if (beatValue != null) beatValue.text = Localize("BEAT ", "节拍 ") + beat.ToString("0.00");
        if (beatProgress != null) beatProgress.fillAmount = Mathf.Clamp01(beatProgressValue);
        if (phaseValue != null) phaseValue.text = Localize("PHASE ", "阶段 ") + phase;

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
            ratingIdleLabel.text = Localize("RHYTHM READY", "节奏就绪");
        }
    }

    private void RefreshLocalizedText()
    {
        SetText(musicLibraryLabel, Localize("MUSIC LIBRARY", "音乐列表"));
        SetText(songSelectTitle, Localize("SELECT MUSIC", "选择音乐"));
        SetText(songSelectSubtitle, Localize("CHOOSE A TRACK TO BEGIN", "选择曲目开始游戏"));
        SetText(nowPlayingLabel, Localize("NOW PLAYING", "正在播放"));
        SetText(scoreCaption, Localize("SCORE", "得分"));
        SetText(comboCaption, Localize("COMBO", "连击"));
        SetText(accuracyCaption, Localize("ACCURACY", "准确率"));
        SetText(rhythmCaption, Localize("RHYTHM", "节奏"));
        SetText(operationCaption, Localize("CONTROLS", "操作"));
        SetText(resultEyebrow, Localize("SESSION RESULTS", "本局结算"));

        switch (mode)
        {
            case HudMode.SongSelect:
                RefreshSongSelectionText();
                break;
            case HudMode.Gameplay:
                RefreshGameplayText();
                break;
            case HudMode.Results:
                RefreshResultsText();
                break;
        }

        if (activeRating == WukongHudRating.None)
        {
            SetText(ratingIdleLabel, Localize("RHYTHM READY", "节奏就绪"));
        }
        else
        {
            SetText(ratingLabel, RatingText(activeRating));
        }
    }

    private void RefreshSongSelectionText()
    {
        SetText(songSelectHint, Localize(
            "UP / DOWN  SELECT     A  START\nX / L  中文",
            "上 / 下  选择     A  开始\nX / L  ENGLISH"));

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
                + song.LocalizedTitle(isChinese) + "   " + song.bpm.ToString("0") + " BPM";
            row.color = songIndex == displayedSelectedIndex
                ? new Color(0.62f, 0.95f, 1f, 1f)
                : new Color(0.9f, 0.94f, 1f, 0.78f);
        }

        if (songDetails == null)
        {
            return;
        }

        songDetails.text = displayedSong == null
            ? Localize("NO SONG MAP", "暂无谱面")
            : displayedSong.LocalizedArtist(isChinese) + "\n"
                + displayedSong.bpm.ToString("0") + " BPM   "
                + displayedSong.notes.Count + Localize(" NOTES", " 个音符");
    }

    private void RefreshGameplayText()
    {
        if (displayedSong != null)
        {
            SetText(songNameValue, displayedSong.LocalizedTitle(isChinese).ToUpperInvariant());
        }
        SetText(operationHint, Localize(
            "SWING   A  THROW + RETURN   HOLD B  MUSIC\nX / L  中文",
            "挥棒   A  投掷并召回   长按 B  选歌\nX / L  ENGLISH"));

        if (countInValue > 0)
        {
            SetText(phaseValue, Localize("READY  ", "准备  ") + countInValue);
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
            SetText(phaseValue, Localize("PHASE 1", "阶段 1"));
        }
    }

    private void RefreshResultsText()
    {
        SetText(resultTitle, Localize("BATTLE COMPLETE", "战斗完成"));
        SetText(resultScore, Localize("SCORE  ", "得分  ") + resultScoreValue.ToString("D6"));
        SetText(resultAccuracy, Localize("ACCURACY  ", "准确率  ") + resultAccuracyValue.ToString("0") + "%");
        SetText(resultCombo, Localize("MAX COMBO  ", "最高连击  ") + resultComboValue.ToString("D3"));
        SetText(resultBreakdown, Localize(
            "PERFECT " + resultPerfectValue + "    GOOD " + resultGoodValue + "    MISS " + resultMissValue,
            "完美 " + resultPerfectValue + "    良好 " + resultGoodValue + "    失误 " + resultMissValue));
        SetText(resultHint, Localize(
            "A  REPLAY     B  SELECT MUSIC\nX / L  中文",
            "A  再来一局     B  选择音乐\nX / L  ENGLISH"));
    }

    private string RatingText(WukongHudRating rating)
    {
        switch (rating)
        {
            case WukongHudRating.Perfect: return Localize("PERFECT", "完美");
            case WukongHudRating.Good: return Localize("GOOD", "良好");
            case WukongHudRating.Miss: return Localize("MISS", "失误");
            default: return string.Empty;
        }
    }

    private string Localize(string english, string chinese)
    {
        return isChinese ? chinese : english;
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
