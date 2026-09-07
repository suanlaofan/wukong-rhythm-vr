using UnityEngine;

public sealed class WukongBeatRock : MonoBehaviour
{
    private WukongRhythmGame game;
    private Vector3 startPoint, targetPoint, controlPoint;
    private float spawnSongTime, targetSongTime;
    private bool spellRock, resolved;
    private Renderer cachedRenderer;
    private Collider cachedCollider;
    private TrailRenderer cachedTrail;
    private WukongHitWarningRing warningRing;
    public bool IsResolved => resolved;
    public float TargetSongTime => targetSongTime;
    public float HitRadius => spellRock ? 0.72f : 0.58f;
    public Vector3 TargetPosition => targetPoint;

    public void Prepare(WukongRhythmGame owner)
    {
        game = owner;
        if (cachedRenderer == null) cachedRenderer = GetComponent<Renderer>();
        if (cachedCollider == null) cachedCollider = GetComponent<Collider>();
        if (cachedTrail == null) cachedTrail = GetComponent<TrailRenderer>();
        Light light = GetComponent<Light>();
        if (light != null) light.enabled = false;
        if (warningRing == null) warningRing = WukongHitWarningRing.Create(owner);
    }

    public void Initialize(WukongRhythmGame owner, Vector3 start, Vector3 target,
        float spawnTime, float targetTime, int targetLane, bool powerful, float warningStartTime)
    {
        Prepare(owner);
        startPoint = start; targetPoint = target;
        spawnSongTime = spawnTime; targetSongTime = targetTime; spellRock = powerful;
        resolved = false;
        controlPoint = Vector3.Lerp(start, target, 0.5f) + Vector3.up * (powerful ? 2.5f : 1.8f)
            + game.ArenaRotation * Vector3.right * targetLane * 0.2f;
        transform.position = PositionAt(game.SongTime);
        transform.localScale = Vector3.one * (powerful ? 0.54f : 0.43f);
        transform.rotation = Quaternion.identity;
        if (cachedTrail != null) cachedTrail.Clear();
        if (cachedRenderer != null) { cachedRenderer.enabled = true; cachedRenderer.sharedMaterial = game.rockMaterial; }
        if (cachedCollider != null) cachedCollider.enabled = true;
        if (cachedCollider is SphereCollider sphere)
        {
            sphere.radius = HitRadius / Mathf.Max(.001f, transform.lossyScale.x);
            sphere.isTrigger = true;
        }
        warningRing.Initialize(owner, target, targetTime, warningStartTime, powerful);
    }

    public Vector3 PositionAt(double songTime)
    {
        float t = Mathf.Max(0f, (float)(songTime - spawnSongTime) / Mathf.Max(0.1f, targetSongTime - spawnSongTime));
        float inverse = 1f - t;
        return inverse * inverse * startPoint + 2f * inverse * t * controlPoint + t * t * targetPoint;
    }

    private void Update()
    {
        if (resolved || game == null) return;
        if (game.State != WukongRhythmGame.BattleState.Playing && game.State != WukongRhythmGame.BattleState.CountIn) return;
        transform.position = PositionAt(game.SongTime);
        transform.rotation = Quaternion.Euler(new Vector3(87f, 123f, 56f) * game.SongTime);
        if (game.State != WukongRhythmGame.BattleState.Playing) return;
        WukongStaffController staff = game.staff;
        if (staff != null && staff.TryGetStrike(PositionAt(game.MusicTimeAtDsp(staff.PreviousSampleDsp)),
                PositionAt(game.MusicTimeAtDsp(staff.SampleDsp)), HitRadius,
                out double contactDsp, out int actionId))
        {
            // Positive calibration moves an input later on the music timeline.
            float error = (float)(game.MusicTimeAtDsp(contactDsp) + game.inputTimingOffsetSeconds - targetSongTime);
            WukongTimingGrade grade = WukongRhythmTiming.Judge(error, game.PerfectWindow, game.hitWindow);
            if ((grade == WukongTimingGrade.Perfect || grade == WukongTimingGrade.Good) && staff.ConsumeSwing(actionId))
            {
                resolved = true;
                warningRing.CompleteHit(grade == WukongTimingGrade.Perfect);
                game.ResolveHit(this, error);
                game.SpawnRockExplosion(transform.position, spellRock);
                game.RecycleRock(this);
                return;
            }
            if (grade == WukongTimingGrade.TooEarly) game.ShowEarlyHit();
        }
        // Consume this frame's swept contacts before expiring a note. Offset must
        // also shift expiry, otherwise a negative input correction loses late hits.
        if (game.SongTime + game.inputTimingOffsetSeconds > targetSongTime + game.hitWindow)
        {
            resolved = true;
            warningRing.CompleteMiss();
            game.ResolveMiss(this);
            game.SpawnMissEffect(transform.position);
            game.RecycleRock(this);
        }
    }

    public void Cancel() { resolved = true; if (warningRing != null) warningRing.Cancel(); }
    private void OnDestroy() { if (warningRing != null) Destroy(warningRing.gameObject); }
}
