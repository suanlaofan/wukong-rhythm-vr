using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public sealed class WukongRhythmGame : MonoBehaviour
{
    public enum BattleState
    {
        SongSelect,
        CountIn,
        Playing,
        Ending,
        Results
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
    [Range(0.2f, 0.55f)] public float hitWindow = 0.38f;

    [Header("Audio Mix")]
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float effectsVolume = 0.28f;

    private readonly List<WukongBeatRock> activeRocks = new List<WukongBeatRock>();
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
    public float SongTime => state == BattleState.Playing || state == BattleState.Ending
        ? Mathf.Max(0f, (float)(AudioSettings.dspTime - songStartDsp))
        : 0f;
    public float CurrentBeat => currentSong != null
        ? (SongTime - currentSong.beatOffsetSeconds) / Mathf.Max(0.001f, beatDuration)
        : 0f;

    private void Awake()
    {
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

        if (state == BattleState.Playing)
        {
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
        currentSong = song;
        currentSong.SortAndSanitize();
        beatDuration = currentSong.BeatDuration;
        ResetScoreState();
        if (hud != null)
        {
            hud.ShowGameplay(currentSong);
        }
        battleRoutine = StartCoroutine(BeginBattle());
    }

    private IEnumerator BeginBattle()
    {
        state = BattleState.CountIn;
        TryMonsterAnimation("Ready", 0.12f);
        for (int count = 4; count >= 1; count--)
        {
            hud?.ShowCountIn(count);
            yield return new WaitForSecondsRealtime(Mathf.Clamp(beatDuration, 0.34f, 0.8f));
        }

        musicSource.clip = currentSong.audioClip;
        songStartDsp = AudioSettings.dspTime + 0.12d;
        musicSource.PlayScheduled(songStartDsp);
        nextNoteIndex = 0;
        state = BattleState.Playing;
        if (hud != null)
        {
            hud.ShowGameplay(currentSong);
        }
        TryMonsterAnimation("Roar", 0.08f);
        monsterReturnAt = 1.35f;
        battleRoutine = null;
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
            float targetSongTime = currentSong.TimeAtBeat(note.beat);
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
        GameObject instance = Instantiate(rockTemplate, transform);
        instance.name = spellRock ? "Magma Rhythm Rock" : "Rhythm Rock";
        instance.SetActive(true);

        Vector3 target = playerCamera.transform.position
            + playerCamera.transform.forward * 1.8f
            + playerCamera.transform.right * lane * 0.78f
            - playerCamera.transform.up * 0.22f;
        Vector3 start = monsterThrowPoint.position;
        Vector3 targetToMonster = start - target;
        const float maximumVisibleTravelDistance = 8.5f;
        if (targetToMonster.sqrMagnitude > maximumVisibleTravelDistance * maximumVisibleTravelDistance)
        {
            start = target + targetToMonster.normalized * maximumVisibleTravelDistance;
        }

        WukongBeatRock rock = instance.GetComponent<WukongBeatRock>();
        rock.Initialize(this, start, target, spawnSongTime, targetSongTime, lane, spellRock, note.warningBeats);
        activeRocks.Add(rock);
        totalSpawned++;

        TryMonsterAnimation(spellRock ? "Spell" : (nextNoteIndex % 2 == 0 ? "attack" : "Attack01"), 0.1f);
        monsterReturnAt = SongTime + (spellRock ? 0.9f : 0.72f);
    }

    public void ResolveHit(WukongBeatRock rock, float timingError)
    {
        activeRocks.Remove(rock);
        totalHit++;
        combo++;
        maxCombo = Mathf.Max(maxCombo, combo);

        bool perfect = timingError <= hitWindow * 0.46f;
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
        activeRocks.Remove(rock);
        totalMiss++;
        combo = 0;
        hud?.ShowRating(WukongHudRating.Miss, 0.55f);
        UpdateHud();
    }

    public void SpawnRockExplosion(Vector3 position, bool powerful)
    {
        int shardCount = powerful ? 12 : 7;
        for (int i = 0; i < shardCount; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Lava Stone Shard";
            shard.transform.position = position + Random.insideUnitSphere * 0.14f;
            shard.transform.rotation = Random.rotation;
            float size = Random.Range(0.065f, powerful ? 0.2f : 0.15f);
            shard.transform.localScale = new Vector3(size, size * Random.Range(0.5f, 1.5f), size);
            Renderer renderer = shard.GetComponent<Renderer>();
            renderer.sharedMaterial = rockMaterial;
            Rigidbody body = shard.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.useGravity = true;
            body.AddExplosionForce(powerful ? 8f : 5.2f, position, 2.4f, 1.5f, ForceMode.Impulse);
            shard.AddComponent<WukongTransientEffect>().life = Random.Range(0.9f, 1.55f);
        }

        GameObject burstObject = new GameObject("Lava Rock Burst");
        burstObject.transform.position = position;
        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.38f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, powerful ? 0.8f : 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, powerful ? 8.5f : 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, powerful ? 0.22f : 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.16f, 0.01f), new Color(1f, 0.78f, 0.12f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;
        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        if (particleMaterial != null)
        {
            particleRenderer.sharedMaterial = particleMaterial;
        }
        particles.Emit(powerful ? 42 : 25);

        Light flashLight = burstObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.19f, 0.025f);
        flashLight.intensity = powerful ? 1800f : 950f;
        flashLight.range = powerful ? 7f : 4.5f;
        WukongTransientEffect transient = burstObject.AddComponent<WukongTransientEffect>();
        transient.life = powerful ? 1.1f : 0.8f;
        transient.fadeLight = true;

        PlayEffect(rockShatterSound, Random.Range(0.9f, 1.08f), 0.88f);
        if (powerful)
        {
            PlayEffect(fireImpactSound, 0.92f, 0.92f);
        }
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

        float accuracy = totalSpawned > 0 ? totalHit / (float)totalSpawned * 100f : 0f;
        hud?.ShowResults(score, accuracy, maxCombo, perfectCount, goodCount, totalMiss);
        state = BattleState.Results;
        endRoutine = null;
    }

    private void ReturnToSongSelection()
    {
        StopActiveRoutines();
        musicSource.Stop();
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
                Destroy(activeRocks[i].gameObject);
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
        float accuracy = resolved > 0 ? totalHit / (float)resolved * 100f : 100f;
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
