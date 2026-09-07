using UnityEngine;

public sealed class WukongHitWarningRing : MonoBehaviour
{
    private const int SegmentCount = 40;
    private WukongRhythmGame game;
    private LineRenderer targetRing, approachRing;
    private float targetSongTime, warningBeats, resolveStarted;
    private bool resolved, powerful;
    private Color resolveColor;

    public static WukongHitWarningRing Create(WukongRhythmGame owner)
    {
        GameObject root = new GameObject("Pooled Rhythm Timing Ring");
        root.transform.SetParent(owner.transform, false);
        WukongHitWarningRing ring = root.AddComponent<WukongHitWarningRing>();
        ring.game = owner;
        ring.targetRing = ring.CreateLine("Hit Circle", 0.016f);
        ring.approachRing = ring.CreateLine("Approach Circle", 0.012f);
        root.SetActive(false);
        return ring;
    }

    public void Initialize(WukongRhythmGame owner, Vector3 position, float targetTime, float warningBeatCount, bool strong)
    {
        game = owner; targetSongTime = targetTime; warningBeats = Mathf.Clamp(warningBeatCount, 1f, 1.5f); powerful = strong;
        resolved = false;
        transform.SetPositionAndRotation(position, owner.ArenaRotation);
        transform.localScale = Vector3.one;
        gameObject.SetActive(true);
        UpdateVisual();
    }

    private void Update()
    {
        if (resolved)
        {
            float fade = 1f - Mathf.Clamp01((Time.unscaledTime - resolveStarted) / 0.22f);
            Color color = resolveColor; color.a = fade;
            SetColor(targetRing, color); SetColor(approachRing, color);
            if (fade <= 0f) gameObject.SetActive(false);
            return;
        }
        if (game == null || (game.State != WukongRhythmGame.BattleState.Playing && game.State != WukongRhythmGame.BattleState.CountIn)) return;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        float remaining = (targetSongTime - game.SongTime) * game.CurrentBpm / 60f;
        bool visible = remaining <= warningBeats;
        targetRing.enabled = visible; approachRing.enabled = visible;
        if (!visible) return;
        float progress = Mathf.Clamp01(1f - remaining / warningBeats);
        // Only the moving circle shrinks. At the authored target time it meets
        // the fixed circle, whose radius is a stable spatial reference.
        approachRing.transform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, progress);
        float beatPhase = Mathf.Repeat(game.CurrentBeat, 1f);
        float accent = 1f - Mathf.Clamp01(beatPhase / 0.22f);
        Color color = powerful ? new Color(1f, 0.64f, 0.16f) : new Color(0.36f, 0.92f, 1f);
        color.a = Mathf.Lerp(0.08f, 0.80f, progress);
        SetColor(approachRing, color);
        color.a *= 0.65f + accent * 0.25f;
        SetColor(targetRing, color);
    }

    public void CompleteHit(bool perfect)
    {
        targetRing.enabled = true; approachRing.enabled = true;
        resolved = true; resolveStarted = Time.unscaledTime;
        resolveColor = perfect ? new Color(0.42f, 1f, 0.95f) : new Color(1f, 0.85f, 0.42f);
    }
    public void CompleteMiss() { resolved = true; resolveStarted = Time.unscaledTime; resolveColor = new Color(0.65f, 0.22f, 0.3f); }
    public void Cancel() { resolved = true; gameObject.SetActive(false); }

    private LineRenderer CreateLine(string label, float width)
    {
        GameObject child = new GameObject(label); child.transform.SetParent(transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.loop = true; line.positionCount = SegmentCount;
        line.widthMultiplier = width; line.numCornerVertices = 0; line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        line.sharedMaterial = game.WarningMaterial;
        for (int i = 0; i < SegmentCount; i++)
        {
            float a = i * Mathf.PI * 2f / SegmentCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.28f);
        }
        return line;
    }
    private static void SetColor(LineRenderer line, Color color) { line.startColor = color; line.endColor = color; }
}
