// @tags: cauldron, animation, vfx, feedback
using System.Collections;
using UnityEngine;

public partial class DokkaebiCauldron
{
    [Header("Cauldron Anim")]
    [SerializeField] private float brewDuration = 1.5f;
    [SerializeField] private Color brewColor = new Color(0.6f, 1f, 0.6f);       // 부글부글 끓는 빛
    [SerializeField] private Color greatFailColor = new Color(1f, 0.3f, 0.2f);  // 붉게 달아오름
    [SerializeField] private ParticleSystem bubbleFx;                           // 선택 — 없으면 색만

    private IEnumerator PlayBrewAnimation(CauldronResult result)
    {
        bool greatFail = result.outcome == CauldronOutcome.GreatFail;
        Color target = greatFail ? greatFailColor : brewColor;
        Color start  = bodyRenderer != null ? bodyRenderer.color : Color.white;

        if (bubbleFx != null) bubbleFx.Play();

        float t = 0f;
        while (t < brewDuration)
        {
            t += Time.deltaTime;
            // 부글부글: 색을 펄스. 대실패: 점점 붉게 고조.
            float k = greatFail ? (t / brewDuration) : Mathf.PingPong(t * 4f, 1f);
            if (bodyRenderer != null) bodyRenderer.color = Color.Lerp(start, target, k);
            yield return null;
        }

        if (bubbleFx != null) bubbleFx.Stop();
        // 소진 비주얼은 BrewRoutine이 횟수 차감 후 적용하므로 여기선 원복만(이번이 마지막 사용이 아닐 때).
        if (bodyRenderer != null && _remainingUses > 1) bodyRenderer.color = start;
    }
}
