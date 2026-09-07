using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

[DefaultExecutionOrder(-100)]
public sealed class WukongRhythmGame : MonoBehaviour
{
    public enum BattleState
    {
        SongSelect,
        CountIn,
        Playing,
        Ending,
        Results,
        Paused,
        Resuming
    }

    [Header("Battle Assets")]
    public WukongSongLibrary songLibrary;
    public AudioClip rockShatterSound;
    public AudioClip fireImpactSound;
    public AudioClip staffImpactSound;
    public AudioClip victorySound;
    public Material rockMaterial;
    public Material particleMaterial;
    public GameObject rockTemplate;

    [Header("Scene References")]
    public Animator monsterAnimator;
    public Transform monsterRoot;
    public Transform monsterThrowPoint;
    public Camera playerCamera;
    public WukongStaffController staff;
    public WukongRhythmHud hud;

    [Header("Rhythm")]
    [Range(0.08f, 0.25f)] public float hitWindow = 0.14f;
    [Range(0.02f, 0.12f)] public float perfectWindow = 0.07f;
    [Range(-0.25f, 0.25f)] public float inputTimingOffsetSeconds;
    private const string TimingOffsetKey = "Wukong.InputOffsetSeconds";
    public float PerfectWindow => Mathf.Min(perfectWindow, hitWindow);
    public Quaternion ArenaRotation => arenaRotation;
    public Material WarningMaterial => warningMaterial;
    public double MusicTimeAtDsp(double dsp) => dsp - songStartDsp;
    public float RhythmAccuracy => WukongRhythmTiming.Accuracy(perfectCount, goodCount, totalHit + totalMiss);

    [Header("Audio Mix")]
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float effectsVolume = 0.28f;

    private readonly List<WukongBeatRock> activeRocks = new List<WukongBeatRock>();
    private readonly Stack<WukongBeatRock> rockPool = new Stack<WukongBeatRock>();
    private readonly List<WukongBeatRock> allRocks = new List<WukongBeatRock>();
    private WukongHitEffectPool effectPool;
    private Material warningMaterial;
    private Vector3 arenaCenter;
    private Quaternion arenaRotation;
    private float pausedSongTime;
    private float lastStableSongTime;
    private float inputStickX;
    private float previousStickX;
    private bool hasFocus = true;
    private bool songScheduled;
    private float lastEarlyHint;
    private AudioSource musicSource;
    private AudioSource effectsSource;
    private UnityEngine.XR.InputDevice leftHandDevice;
    private UnityEngine.XR.InputDevice rightHandDevice;
    private WukongSongDefinition currentSong;
    private BattleState state;
    private double songStartDsp;
    private float beatDuration = 0.5f;
    private float monsterReturnAt;
    private float secondaryHoldTime;
    private int selectedSongIndex;
    private int nextNoteIndex;
    private int totalSpawned;
    private int totalHit;
    private int totalMiss;
    private int combo;
    private int maxCombo;
    private int score;
    private int perfectCount;
    private int goodCount;
    private int lastPhase;
    private bool previousPrimaryButton;
    private bool previousSecondaryButton;
    private bool previousLanguageButton;
    private float previousStickY;
    private Coroutine battleRoutine;
    private Coroutine endRoutine;

    public BattleState State => state;
    public float CurrentBpm => currentSong != null ? currentSong.bpm : 120f;
    public float SongTime => state == BattleState.Paused || state == BattleState.Resuming ? pausedSongTime
        : state == BattleState.Playing || state == BattleState.Ending || (state == BattleState.CountIn && songScheduled)
            ? (float)(AudioSettings.dspTime - songStartDsp) : 0f;
    public float CurrentBeat => currentSong != null
        ? (SongTime - currentSong.beatOffsetSeconds) / Mathf.Max(0.001f, beatDuration)
        : 0f;

    private void Awake()
    {
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        inputTimingOffsetSeconds = Mathf.Clamp(PlayerPrefs.GetFloat(TimingOffsetKey, inputTimingOffsetSeconds), -0.25f, 0.25f);
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = Mathf.Clamp01(musicVolume);

        effectsSource = gameObject.AddComponent<AudioSource>();
        effectsSource.playOnAwake = false;
        effectsSource.spatialBlend = 0f;
        effectsSource.volume = Mathf.Clamp01(effectsVolume);
    }

    private void Start()
    {
        if (rockTemplate != null)
        {
            rockTemplate.SetActive(false);
        }

        staff?.BindGame(this);
        Shader ringShader = Shader.Find("Sprites/Default");
        if (ringShader != null) warningMaterial = new Material(ringShader) { name = "Wukong Shared Timing Ring" };
        effectPool = gameObject.AddComponent<WukongHitEffectPool>();
        effectPool.Initialize(particleMaterial);
        PrewarmRocks();
        state = BattleState.SongSelect;
        selectedSongIndex = Mathf.Clamp(selectedSongIndex, 0, Mathf.Max(0, songLibrary != null ? songLibrary.Count - 1 : 0));
        ShowSongSelection();
    }

    private void Update()
    {
        ReadInput(out bool primaryPressed, out bool secondaryPressed, out bool secondaryHeld, out bool languagePressed, out float stickY);
        if (languagePressed)
        {
            hud?.ToggleLanguage();
        }

        if (state == BattleState.SongSelect)
        {
            UpdateSongSelection(primaryPressed, stickY);
            return;
        }

        if (state == BattleState.Paused)
        {
            UpdateCalibration();
            secondaryHoldTime = secondaryHeld ? secondaryHoldTime + Time.unscaledDeltaTime : 0f;
            if (secondaryHoldTime >= 0.85f) ReturnToSongSelection();
            else if (primaryPressed && hasFocus && IsUserPresent()) battleRoutine = StartCoroutine(ResumeBattle());
            return;
        }
        if (state == BattleState.CountIn) { if (songScheduled) UpdateSpawning(); return; }
        if (state == BattleState.Playing)
        {
            if (secondaryPressed || (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame) || !IsUserPresent())
            { PauseBattle(); return; }
            lastStableSongTime = SongTime;
            UpdateBattlePhases();
            UpdateSpawning();
            UpdateMonsterReturn();
            UpdateHud();

            if (primaryPressed && staff != null)
            {
                staff.BeginThrow(FindThrowTarget());
            }

            secondaryHoldTime = secondaryHeld ? secondaryHoldTime + Time.unscaledDeltaTime : 0f;
            if (secondaryHoldTime >= 0.85f)
            {
                ReturnToSongSelection();
                return;
            }

            float musicLength = currentSong != null ? currentSong.Duration : 0f;
            if (currentSong != null
                && SongTime >= musicLength + hitWindow
                && activeRocks.Count == 0
                && endRoutine == null)
            {
                endRoutine = StartCoroutine(EndBattle());
            }
        }
        else if (state == BattleState.Results)
        {
            bool keyboardReplay = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            if (primaryPressed || keyboardReplay)
            {
                StartSelectedSong();
            }
            else if (secondaryPressed)
            {
                ReturnToSongSelection();
            }
        }
    }

    private void UpdateSongSelection(bool primaryPressed, float stickY)
    {
        int direction = 0;
        if (stickY >= 0.65f && previousStickY < 0.65f) direction = -1;
        if (stickY <= -0.65f && previousStickY > -0.65f) direction = 1;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame) direction = -1;
            if (Keyboard.current.downArrowKey.wasPressedThisFrame) direction = 1;
        }

        previousStickY = stickY;
        if (direction != 0 && songLibrary != null && songLibrary.Count > 0)
        {
            selectedSongIndex = (selectedSongIndex + direction + songLibrary.Count) % songLibrary.Count;
            ShowSongSelection();
        }

        if (primaryPressed)
        {
            StartSelectedSong();
        }
    }

    private void ShowSongSelection()
    {
        if (songLibrary != null)
        {
            songLibrary.RemoveNullEntries();
        }
        if (hud != null)
        {
            hud.ShowSongSelect(songLibrary != null ? songLibrary.songs : null, selectedSongIndex);
        }
        TryMonsterAnimation("idle", 0.16f);
    }

    private void StartSelectedSong()
    {
        WukongSongDefinition song = songLibrary != null ? songLibrary.Get(selectedSongIndex) : null;
        if (song == null || song.audioClip == null || song.notes == null || song.notes.Count == 0)
        {
            ShowSongSelection();
            return;
        }

        StopActiveRoutines();
        ClearActiveRocks();
        staff?.CancelThrowAndReattach();
        musicSource.Stop();
        songScheduled = false;
        currentSong = song;
        currentSong.SortAndSanitize();
        beatDuration = currentSong.BeatDuration;
        ResetScoreState();
        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        arenaRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        arenaCenter = playerCamera.transform.position + arenaRotation * new Vector3(0f, -0.22f, 1.8f);
        if (hud != null)
        {
            hud.ShowGameplay(currentSong);
        }
        battleRoutine = StartCoroutine(BeginBattle());
    }

    private IEnumerator BeginBattle()
    {
        state = BattleState.CountIn;
        musicSource.clip = currentSong.audioClip;
        musicSource.clip.LoadAudioData();
        double timeout = Time.realtimeSinceStartupAsDouble + 10;
        while (musicSource.clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartupAsDouble < timeout)
            yield return null;
        if (musicSource.clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogError("WUKONG_AUDIO_LOAD_FAILED " + currentSong.songId);
            battleRoutine = null;
            ReturnToSongSelection();
            yield break;
        }
        float firstNote = currentSong.TimeAtNote(currentSong.notes[0]);
        double preroll = Mathf.Max(4 * beatDuration, currentSong.travelBeats * beatDuration - firstNote + 0.2f);
        songStartDsp = AudioSettings.dspTime + preroll;
        songScheduled = true;
        musicSource.PlayScheduled(songStartDsp);
        TryMonsterAnimation("Ready", 0.12f);
        int displayedCount = -1;
        while (AudioSettings.dspTime < songStartDsp)
        {
            int count = Mathf.CeilToInt((float)(songStartDsp - AudioSettings.dspTime) / beatDuration);
            if (displayedCount != count) { hud?.ShowCountIn(count); displayedCount = count; }
            yield return null;
        }
        state = BattleState.Playing;
        effectPool?.SetPaused(false);
        if (monsterAnimator != null) monsterAnimator.speed = 1f;
        staff?.ResetContactHistory();
        hud?.ShowGameplay(currentSong);
        TryMonsterAnimation("Roar", 0.08f);
        monsterReturnAt = 1.35f;
        battleRoutine = null;
    }

    public void PauseBattle()
    {
        if (state != BattleState.Playing) return;
        pausedSongTime = SongTime;
        musicSource.Pause();
        state = BattleState.Paused;
        effectPool?.SetPaused(true);
        if (monsterAnimator != null) monsterAnimator.speed = 0f;
        staff?.CancelThrowAndReattach();
        secondaryHoldTime = 0f;
        hud?.ShowPaused(inputTimingOffsetSeconds);
    }

    private IEnumerator ResumeBattle()
    {
        state = BattleState.Resuming;
        musicSource.Stop();
        musicSource.timeSamples = Mathf.Clamp(Mathf.RoundToInt(pausedSongTime * currentSong.audioClip.frequency), 0, currentSong.audioClip.samples - 1);
        double resumeAt = AudioSettings.dspTime + 4 * beatDuration;
        songStartDsp = resumeAt - pausedSongTime;
        musicSource.PlayScheduled(resumeAt);
        int displayedCount = -1;
        while (AudioSettings.dspTime < resumeAt)
        {
            int count = Mathf.CeilToInt((float)(resumeAt - AudioSettings.dspTime) / beatDuration);
            if (count != displayedCount) { hud?.ShowCountIn(count); displayedCount = count; }
            yield return null;
        }
        state = BattleState.Playing;
        effectPool?.SetPaused(false);
        if (monsterAnimator != null) monsterAnimator.speed = 1f;
        staff?.ResetContactHistory();
        for (int i = 0; i < activeRocks.Count; i++) activeRocks[i].ResetContactHistory();
        hud?.ShowGameplay(currentSong);
        battleRoutine = null;
    }

    private void UpdateCalibration()
    {
        int direction = 0;
        if (inputStickX > 0.65f && previousStickX <= 0.65f) direction = 1;
        if (inputStickX < -0.65f && previousStickX >= -0.65f) direction = -1;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame) direction = 1;
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame) direction = -1;
        }
        previousStickX = inputStickX;
        if (direction == 0) return;
        inputTimingOffsetSeconds = Mathf.Clamp(inputTimingOffsetSeconds + direction * 0.01f, -0.25f, 0.25f);
        PlayerPrefs.SetFloat(TimingOffsetKey, inputTimingOffsetSeconds);
        PlayerPrefs.Save();
        hud?.ShowPaused(inputTimingOffsetSeconds);
    }

    private bool IsUserPresent()
    {
        UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (head.isValid && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && !tracked) return false;
        return !head.isValid || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) || present;
    }

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused) SuspendBattle();
    }
    private void OnApplicationPause(bool paused) { if (paused) SuspendBattle(); }
    private void SuspendBattle()
    {
        if (state == BattleState.Playing) PauseBattle();
        else if (state == BattleState.CountIn) ReturnToSongSelection();
        else if (state == BattleState.Resuming)
        {
            StopActiveRoutines(); musicSource.Stop(); state = BattleState.Paused;
            hud?.ShowPaused(inputTimingOffsetSeconds);
        }
    }

    public void ShowEarlyHit()
    {
        if (Time.unscaledTime - lastEarlyHint < 0.25f) return;
        lastEarlyHint = Time.unscaledTime;
        hud?.ShowRating(WukongHudRating.Early, 0.28f);
    }

    private void UpdateSpawning()
    {
        if (currentSong == null || currentSong.notes == null)
        {
            return;
        }

        while (nextNoteIndex < currentSong.notes.Count)
        {
            WukongBeatNote note = currentSong.notes[nextNoteIndex];
            float targetSongTime = currentSong.TimeAtNote(note);
            float spawnSongTime = targetSongTime - currentSong.travelBeats * beatDuration;
            if (SongTime < spawnSongTime)
            {
                break;
            }

            SpawnRock(note, targetSongTime, spawnSongTime, note.lane);
            if (note.type == WukongBeatNoteType.Double)
            {
                int oppositeLane = note.lane == 0 ? -1 : -note.lane;
                SpawnRock(note, targetSongTime, spawnSongTime, oppositeLane);
            }
            nextNoteIndex++;
        }
    }

    private void SpawnRock(WukongBeatNote note, float targetSongTime, float spawnSongTime, int lane)
    {
        if (rockTemplate == null || monsterThrowPoint == null || playerCamera == null)
        {
            return;
        }

        bool spellRock = note.type == WukongBeatNoteType.Spell;
        WukongBeatRock rock = rockPool.Count > 0 ? rockPool.Pop() : CreatePooledRock();
        GameObject instance = rock.gameObject;
        instance.name = spellRock ? "Magma Rhythm Rock" : "Rhythm Rock";
        instance.SetActive(true);

        Vector3 target = arenaCenter + arenaRotation * Vector3.right * lane * 0.78f;
        Vector3 start = monsterThrowPoint.position;
        Vector3 targetToMonster = start - target;
        const float maximumVisibleTravelDistance = 8.5f;
        if (targetToMonster.sqrMagnitude > maximumVisibleTravelDistance * maximumVisibleTravelDistance)
        {
            start = target + targetToMonster.normalized * maximumVisibleTravelDistance;
        }

        rock.Initialize(this, start, target, spawnSongTime, targetSongTime, lane, spellRock, note.warningBeats);
        activeRocks.Add(rock);
        totalSpawned++;

        TryMonsterAnimation(spellRock ? "Spell" : (nextNoteIndex % 2 == 0 ? "attack" : "Attack01"), 0.1f);
        monsterReturnAt = SongTime + (spellRock ? 0.9f : 0.72f);
    }

    public void ResolveHit(WukongBeatRock rock, float timingError)
    {
        WukongTimingGrade grade = WukongRhythmTiming.Judge(timingError, PerfectWindow, hitWindow);
        if ((grade != WukongTimingGrade.Perfect && grade != WukongTimingGrade.Good) || !activeRocks.Remove(rock)) return;
        totalHit++;
        combo++;
        maxCombo = Mathf.Max(maxCombo, combo);

        bool perfect = grade == WukongTimingGrade.Perfect;
        int baseScore = perfect ? 120 : 80;
        float multiplier = Mathf.Min(2.5f, 1f + Mathf.Floor(combo / 8f) * 0.25f);
        score += Mathf.RoundToInt(baseScore * multiplier);

        if (perfect)
        {
            perfectCount++;
            hud?.ShowRating(WukongHudRating.Perfect, 0.52f);
        }
        else
        {
            goodCount++;
            hud?.ShowRating(WukongHudRating.Good, 0.46f);
            hud?.ShowTimingDetail(timingError);
        }

        if (combo % 4 == 0)
        {
            TryMonsterAnimation("hit", 0.08f);
            monsterReturnAt = SongTime + 0.42f;
        }

        staff?.ConfirmHit(perfect ? 0.82f : 0.55f);
        PlayEffect(staffImpactSound, perfect ? 1.05f : 0.94f, 0.85f);
        UpdateHud();
    }

    public void ResolveMiss(WukongBeatRock rock)
    {
        if (!activeRocks.Remove(rock)) return;
        totalMiss++;
        combo = 0;
        hud?.ShowRating(WukongHudRating.Miss, 0.55f);
        UpdateHud();
    }

    public void SpawnRockExplosion(Vector3 position, bool powerful)
    {
        effectPool?.Emit(position, powerful, false);
        PlayEffect(rockShatterSound, 1f, powerful ? 0.5f : 0.3f);
        if (powerful) PlayEffect(fireImpactSound, 1f, 0.35f);
    }

    public void SpawnMissEffect(Vector3 position) { effectPool?.Emit(position, false, true); }

    private WukongBeatRock CreatePooledRock()
    {
        GameObject instance = Instantiate(rockTemplate, transform);
        instance.SetActive(false);
        WukongBeatRock rock = instance.GetComponent<WukongBeatRock>();
        if (rock == null) rock = instance.AddComponent<WukongBeatRock>();
        allRocks.Add(rock);
        return rock;
    }

    private void PrewarmRocks()
    {
        if (rockTemplate == null) return;
        for (int i = 0; i < 48; i++)
        {
            WukongBeatRock rock = CreatePooledRock();
            rock.Prepare(this);
            rockPool.Push(rock);
        }
    }

    public void RecycleRock(WukongBeatRock rock)
    {
        rock.gameObject.SetActive(false);
        rockPool.Push(rock);
    }

    private void OnAudioConfigurationChanged(bool deviceChanged)
    {
        if (state == BattleState.Playing)
        {
            PauseBattle();
            pausedSongTime = Mathf.Max(0f, lastStableSongTime);
            musicSource.Stop();
        }
        else SuspendBattle();
        // A device switch can reset DSP time; resume creates a new scheduled origin.
    }

    private void OnDestroy()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        if (warningMaterial != null) Destroy(warningMaterial);
    }

    private void UpdateBattlePhases()
    {
        int phase = GetPhase();
        if (phase == lastPhase)
        {
            return;
        }

        lastPhase = phase;
        if (phase > 1)
        {
            TryMonsterAnimation("Roar", 0.12f);
            monsterReturnAt = SongTime + 1.1f;
        }
    }

    private int GetPhase()
    {
        float length = currentSong != null ? currentSong.Duration : 1f;
        float progress = Mathf.Clamp01(SongTime / Mathf.Max(1f, length));
        return progress < 0.36f ? 1 : progress < 0.72f ? 2 : 3;
    }

    private void UpdateMonsterReturn()
    {
        if (monsterReturnAt > 0f && SongTime >= monsterReturnAt)
        {
            monsterReturnAt = -1f;
            TryMonsterAnimation("idle", 0.16f);
        }
    }

    private IEnumerator EndBattle()
    {
        state = BattleState.Ending;
        ClearActiveRocks();
        staff?.CancelThrowAndReattach();
        TryMonsterAnimation("Death", 0.1f);
        PlayEffect(victorySound, 1f, 0.95f);
        yield return new WaitForSecondsRealtime(1.1f);

        float accuracy = WukongRhythmTiming.Accuracy(perfectCount, goodCount, totalSpawned);
        hud?.ShowResults(score, accuracy, maxCombo, perfectCount, goodCount, totalMiss);
        state = BattleState.Results;
        endRoutine = null;
    }

    private void ReturnToSongSelection()
    {
        StopActiveRoutines();
        musicSource.Stop();
        effectPool?.Clear();
        if (monsterAnimator != null) monsterAnimator.speed = 1f;
        ClearActiveRocks();
        staff?.CancelThrowAndReattach();
        secondaryHoldTime = 0f;
        state = BattleState.SongSelect;
        ShowSongSelection();
    }

    private void StopActiveRoutines()
    {
        if (battleRoutine != null)
        {
            StopCoroutine(battleRoutine);
            battleRoutine = null;
        }
        if (endRoutine != null)
        {
            StopCoroutine(endRoutine);
            endRoutine = null;
        }
    }

    private void ClearActiveRocks()
    {
        for (int i = activeRocks.Count - 1; i >= 0; i--)
        {
            if (activeRocks[i] != null)
            {
                activeRocks[i].Cancel();
                RecycleRock(activeRocks[i]);
            }
        }
        activeRocks.Clear();
    }

    private void ResetScoreState()
    {
        nextNoteIndex = 0;
        totalSpawned = 0;
        totalHit = 0;
        totalMiss = 0;
        combo = 0;
        maxCombo = 0;
        score = 0;
        perfectCount = 0;
        goodCount = 0;
        lastPhase = 0;
        monsterReturnAt = -1f;
        secondaryHoldTime = 0f;
    }

    private void UpdateHud()
    {
        if (hud == null || currentSong == null)
        {
            return;
        }

        int resolved = totalHit + totalMiss;
        float accuracy = WukongRhythmTiming.Accuracy(perfectCount, goodCount, resolved);
        float beat = CurrentBeat;
        float beatProgress = beat - Mathf.Floor(beat);
        hud.UpdateGameplay(score, combo, accuracy, GetPhase(), currentSong.bpm, beat, beatProgress);
    }

    private Transform FindThrowTarget()
    {
        if (staff == null || playerCamera == null)
        {
            return null;
        }

        WukongBeatRock bestRock = null;
        float bestScore = float.MaxValue;
        Vector3 origin = staff.transform.position;
        for (int i = 0; i < activeRocks.Count; i++)
        {
            WukongBeatRock rock = activeRocks[i];
            if (rock == null || rock.IsResolved)
            {
                continue;
            }

            Vector3 offset = rock.transform.position - origin;
            float distance = offset.magnitude;
            if (distance > staff.maximumThrowDistance + 0.6f || distance < 0.1f)
            {
                continue;
            }
            float facing = Vector3.Dot(playerCamera.transform.forward, offset / distance);
            if (facing < 0.18f)
            {
                continue;
            }

            float timing = Mathf.Abs(rock.TargetSongTime - SongTime);
            float candidateScore = distance + (1f - facing) * 2f + timing * 0.08f;
            if (candidateScore < bestScore)
            {
                bestScore = candidateScore;
                bestRock = rock;
            }
        }
        return bestRock != null ? bestRock.transform : null;
    }

    private void ReadInput(
        out bool primaryPressed,
        out bool secondaryPressed,
        out bool secondaryHeld,
        out bool languagePressed,
        out float stickY)
    {
        if (!leftHandDevice.isValid)
        {
            leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        }
        if (!rightHandDevice.isValid)
        {
            rightHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        bool languageHeld = false;
        bool primaryHeld = false;
        bool secondaryHeldXr = false;
        Vector2 stick = Vector2.zero;
        if (leftHandDevice.isValid)
        {
            leftHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out languageHeld);
        }
        if (rightHandDevice.isValid)
        {
            rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out primaryHeld);
            rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out secondaryHeldXr);
            rightHandDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stick);
        }

        bool keyboardPrimary = Keyboard.current != null
            && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.tKey.wasPressedThisFrame);
        bool keyboardSecondaryPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool keyboardSecondaryHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
        bool keyboardLanguage = Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame;
        primaryPressed = (primaryHeld && !previousPrimaryButton) || keyboardPrimary;
        secondaryPressed = (secondaryHeldXr && !previousSecondaryButton) || keyboardSecondaryPressed;
        secondaryHeld = secondaryHeldXr || keyboardSecondaryHeld;
        languagePressed = (languageHeld && !previousLanguageButton) || keyboardLanguage;
        stickY = stick.y;
        inputStickX = stick.x;
        previousPrimaryButton = primaryHeld;
        previousSecondaryButton = secondaryHeldXr;
        previousLanguageButton = languageHeld;
    }

    private void TryMonsterAnimation(string stateName, float transition)
    {
        if (monsterAnimator == null)
        {
            return;
        }

        int hash = Animator.StringToHash(stateName);
        if (monsterAnimator.HasState(0, hash))
        {
            monsterAnimator.CrossFadeInFixedTime(hash, transition, 0, 0f);
        }
    }

    private void PlayEffect(AudioClip clip, float pitch, float volume)
    {
        if (clip == null || effectsSource == null)
        {
            return;
        }

        effectsSource.pitch = pitch;
        effectsSource.PlayOneShot(clip, volume);
    }

}
