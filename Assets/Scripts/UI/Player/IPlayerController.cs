using System;
using UnityEngine;

public interface IPlayerController
{
    bool IsWallClimbing { get; }

    /// <summary>
    /// 외부 충격(바위, 폭발 등)에 의한 넉백을 적용한다.
    /// isDashing 제어를 내부에서 처리해 외부가 PlayerController 구현 세부사항을 알 필요가 없다.
    /// </summary>
    void ApplyExternalKnockback(Vector2 velocity, float duration);

    // ── 유물 훅 ──
    /// <summary>착지하는 순간 발행된다. (유물: 이단점프 리셋 등)</summary>
    event Action Landed;

    /// <summary>
    /// 공중에서 점프 입력 시 호출된다. true를 반환하면 추가(공중) 점프를 허용한다.
    /// RelicManager가 설정하며, 유물이 없으면 null이다.
    /// </summary>
    Func<bool> AirJumpQuery { get; set; }

    /// <summary>현재 접지 여부. (유물: 차징 점프는 지면에서만 충전)</summary>
    bool IsGrounded { get; }

    /// <summary>
    /// 지면 점프 입력 시 호출된다. false를 반환하면 일반(즉발) 점프를 억제한다.
    /// 유물이 점프를 직접 처리할 때 사용. RelicManager가 설정하며, 없으면 null(=일반 점프 허용).
    /// </summary>
    Func<bool> GroundJumpQuery { get; set; }

    /// <summary>지정한 상방 속도로 즉시 점프시킨다. 이 점프에 한해 상승속도 리미터를 일시 상향.
    /// (유물: 고물 스프링 차징 슈퍼 점프. 단, 전역 maxFallSpeed 상한은 넘지 못함.)</summary>
    void SuperJump(float upVelocity);

    /// <summary>제트팩 추진(유물). active면 중력 0 + 상방 ascendSpeed로 구동. false면 자동 복원.
    /// PlayerController의 HandleNormalMovement에 통합되어 FixedUpdate 속도 확정에 반영된다.</summary>
    void SetJetpackThrust(bool active, float ascendSpeed);
}
