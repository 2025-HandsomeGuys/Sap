// @tags: vfx, special-chunk, trap, collapse, interface
using UnityEngine;

/// <summary>
/// ICollapseEffect 구현 — 시각(VFX) 피드백.
/// SRP: VFX 활성화 책임만 담당한다.
/// OCP: 추후 파티클, 카메라 흔들기 등 별도 클래스로 확장 가능.
/// </summary>
public class VFXCollapseEffect : MonoBehaviour, ICollapseEffect
{
    [Tooltip("붕괴 경고 이펙트 오브젝트 (균열 애니메이션)")]
    public GameObject warningVFX;

    [Tooltip("붕괴 완료 이펙트 오브젝트 (선택 사항)")]
    public GameObject collapseVFX;

    public void PlayWarning()
    {
        if (warningVFX != null)
            warningVFX.SetActive(true);
    }

    public void PlayCollapse()
    {
        if (warningVFX != null)
            warningVFX.SetActive(false);

        if (collapseVFX != null)
            collapseVFX.SetActive(true);
    }
}
