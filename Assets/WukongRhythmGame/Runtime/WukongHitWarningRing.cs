using UnityEngine;

public sealed class WukongHitWarningRing : MonoBehaviour
{
    private const int SegmentCount = 40;
    private WukongRhythmGame game;
    private LineRenderer targetRing, approachRing;
    private float targetSongTime, warningStartSongTime, resolveStarted;
    private bool resolved, powerful;
    private Color resolveColor;

    public static WukongHitWarningRing Create(WukongRhythmGame owner)
    {
        GameObject root = new GameObject("Pooled Rhythm Timing Ring");
        root.transform.SetParent(owner.transform, false);
        WukongHitWarningRing ring = root.AddComponent<WukongHitWarningRing>();
        ring.game = owner;
        ring.targetRing = ring.CreateLine("Hit Circle", 0.009f);
        ring.approachRing = ring.CreateLine("Approach Circle", 0.016f);
        root.SetActive(false);
        return ring;
    }

    public void Initialize(WukongRhythmGame owner, Vector3 position, float targetTime, float warningStartTime, bool strong)
    {
        game = owner; targetSongTime = targetTime; warningStartSongTime = warningStartTime; powerful = strong;
        resolved = false;
        transform.SetPositionAndRotation(position, owner.ArenaRotation);
        // Visual styling stays compact and independent of the forgiving hit volume.
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
        bool visible = game.SongTime >= warningStartSongTime;
        targetRing.enabled = visible; approachRing.enabled = visible;
        if (!visible) return;
        float progress = ApproachProgress(game.SongTime, warningStartSongTime, targetSongTime);
        // Only the moving circle shrinks. At the authored target time it meets
        // the fixed circle, whose radius is a stable spatial reference.
        approachRing.transform.localScale = Vector3.one * Mathf.Lerp(1.32f, 1f, progress);
        Color color = new Color(0.36f, 0.92f, 1f);
        color.a = Mathf.Lerp(0.18f, 0.8f, progress);
        SetColor(approachRing, color);
        color.a = Mathf.Lerp(0.14f, 0.55f, progress);
        SetColor(targetRing, color);
    }

    public static float ApproachProgress(float time, float start, float target)
        => Mathf.Clamp01((time - start) / Mathf.Max(.001f, target - start));

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
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .28f);
        }
        return line;
    }
    private static void SetColor(LineRenderer line, Color color) { line.startColor = color; line.endColor = color; }
}
