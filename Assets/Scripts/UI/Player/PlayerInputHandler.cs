using UnityEngine;
using System;

/// <summary>
/// SRP: 오직 플레이어 입력 감지만 담당한다.
/// 상호작용 키(<see cref="InteractionKeys.Interact"/> = F)가 눌리면 OnInteractPressed 이벤트를 발행하고,
/// 실제 로직(감지·수행)은 구독자(PlayerInteractor)에게 위임한다.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    public event Action OnInteractPressed;

    private void Update()
    {
        // UI가 열려있으면 상호작용 입력 차단 (상태 없는 전면 오버레이 포함)
        if (UIStateManager.IsInputBlocked) return;

        if (InteractionKeys.InteractPressed)
        {
            OnInteractPressed?.Invoke();
        }
    }
}
