using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// @tags: camera, cinemachine, ceiling, clamp
/// 카메라가 특정 월드 높이 위로 올라가지 못하게 막는 Cinemachine 확장.
/// 상한선은 '화면 위쪽 가장자리'가 멈추는 월드 Y이며, 카메라 중심이 아니다.
/// 설계: Assets/Docs/camera-ceiling-exit-prompt.md
/// </summary>
[SaveDuringPlay]
public class CameraCeilingExtension : CinemachineExtension
{
    [Header("천장 설정")]
    [Tooltip("화면 위쪽 가장자리가 멈출 월드 Y. 카메라 중심이 아니다.")]
    [SerializeField] private float ceilingY = 0f;

    [Tooltip("클램프 on/off. 끄면 카메라가 자유롭게 올라간다.")]
    [SerializeField] private bool ceilingEnabled = true;

    /// <summary>런타임에서 천장 높이를 바꿀 때 사용 (씬별 세팅 등).</summary>
    public float CeilingY
    {
        get => ceilingY;
        set => ceilingY = value;
    }

    /// <summary>런타임에서 클램프를 켜고 끌 때 사용.</summary>
    public bool CeilingEnabled
    {
        get => ceilingEnabled;
        set => ceilingEnabled = value;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        // Finalize: Body/Aim 파이프라인이 끝나 최종 위치가 확정된 시점.
        // CinemachineFollow의 damping 결과까지 반영된 값을 클램프한다.
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (!ceilingEnabled) return;

        // 던전은 지하 지형과 겹치지 않도록 y=+10000 오프셋에 생성된다(DungeonOverlayController.DungeonOffset).
        // 천장선(y≈0)을 그대로 적용하면 카메라가 지상 경계로 처박히고 플레이어만 1만 유닛 위에 남는다.
        // → 던전 동안은 클램프 자체를 건너뛴다. DungeonOverlayController가 CinemachineConfiner2D를
        //   같은 이유로 꺼두는 것과 동일한 취지지만, 여기선 suspend/restore 상태 없이 매 프레임 조회한다
        //   (이탈 경로를 하나라도 놓쳤을 때 클램프가 영구히 죽는 사고가 없다).
        if (DungeonOverlayController.IsInDungeon) return;

        // 이 프로젝트는 항상 직교 카메라. 원근 모드에서는 half-height 계산이 달라지므로 건너뛴다.
        if (!state.Lens.Orthographic) return;

        // orthographicSize는 CameraFollow가 드릴 대시·UI 오픈에 따라 매 프레임 바꾸므로
        // 캐싱하지 않고 state에서 직접 읽는다. 캐싱하면 줌 전환 중 천장선이 흔들린다.
        float currentY = state.GetFinalPosition().y;
        float clampedY = CameraCeilingMath.ClampCenterY(currentY, ceilingY, state.Lens.OrthographicSize);
        if (clampedY == currentY) return;

        // RawPosition을 덮지 않고 보정값을 더한다 (Confiner2D와 동일한 관례).
        // 다른 확장이 붙어도 보정이 합성된다.
        state.PositionCorrection += new Vector3(0f, clampedY - currentY, 0f);
    }
}

/// <summary>
/// @tags: camera, ceiling, math
/// 천장 클램프 순수 계산. Cinemachine 타입에 의존하지 않아 EditMode 테스트가 가능하다.
/// </summary>
public static class CameraCeilingMath
{
    /// <summary>
    /// 화면 위쪽 가장자리가 <paramref name="ceilingY"/>를 넘지 않도록 카메라 중심 Y를 제한한다.
    /// </summary>
    /// <param name="currentY">현재 카메라 중심 Y</param>
    /// <param name="ceilingY">화면 위쪽 가장자리가 멈출 월드 Y</param>
    /// <param name="halfHeight">직교 카메라의 orthographicSize (= 화면 절반 높이)</param>
    public static float ClampCenterY(float currentY, float ceilingY, float halfHeight)
        => Mathf.Min(currentY, ceilingY - halfHeight);
}
