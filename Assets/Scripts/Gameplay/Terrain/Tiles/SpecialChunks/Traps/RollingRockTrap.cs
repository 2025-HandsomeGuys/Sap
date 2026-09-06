// @tags: trap, special-chunk, rock, digging, trigger, player
using UnityEngine;

/// <summary>
/// 매몰 갱도 — 함정 트리거.
///
/// 플레이어가 밟으면:
///  1. 천장 청크의 하단 픽셀을 공기로 제거 (PixelFloorCollapser 동일 방식)
///  2. RollingRockEntity.Activate() 호출
///
/// SOLID:
///  SRP: 함정 감지·천장 제거·바위 활성화만. 바위 물리·충돌은 RollingRockEntity가 담당.
///  OCP: 천장 제거 두께(ceilingThicknessPx)만 바꿔 다른 높이의 갱도에 재사용 가능.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(AudioSource))]
public class RollingRockTrap : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("이 트리거가 활성화할 바위 엔티티.")]
    public RollingRockEntity rockEntity;

    [Header("천장 제거")]
    [Tooltip("바위가 떨어질 천장 TerrainChunk. 비워두면 rockEntity 위치에서 자동 탐색.")]
    public TerrainChunk ceilingChunk;
    [Tooltip("청크 하단에서 제거할 픽셀 두께. PixelFloorCollapser.floorThicknessPx와 동일 방식.")]
    public int ceilingThicknessPx = 80;

    [Header("오디오")]
    [Tooltip("함정 발동 사운드 (딸깍 / 우르릉). 없으면 생략.")]
    public AudioClip triggerSFX;

    private AudioSource _audio;
    private bool _triggered;

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();

        // 프리팹의 AudioSource는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
        AudioRouting.Route(_audio, AudioChannel.SFX);
        // isTrigger 강제 보장
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered) return;
        if (!other.CompareTag("Player")) return;

        _triggered = true;

        if (triggerSFX != null) _audio.PlayOneShot(triggerSFX);

        OpenCeiling();
        rockEntity?.Activate();

        Debug.Log("[RollingRockTrap] 발동 — 천장 제거 + 바위 활성화");
    }

    /// <summary>
    /// 바위 위 천장 픽셀을 제거한다.
    /// PixelFloorCollapser.CollapseFloor()와 동일한 NativeArray 직접 수정 방식.
    /// 청크 하단(y=0)부터 ceilingThicknessPx 행을 air로 클리어.
    /// </summary>
    private void OpenCeiling()
    {
        // Inspector 미연결 시 rockEntity의 TerrainChunk에서 자동 탐색
        TerrainChunk chunk = ceilingChunk;
        if (chunk == null && rockEntity != null)
            chunk = rockEntity.GetComponentInParent<TerrainChunk>();

        if (chunk == null)
        {
            Debug.LogWarning("[RollingRockTrap] ceilingChunk를 찾을 수 없습니다. Inspector에서 직접 연결해주세요.");
            return;
        }

        var data = chunk.GetData();
        if (data == null)
        {
            Debug.LogWarning("[RollingRockTrap] ChunkData가 null입니다.");
            return;
        }

        chunk.EnsureJobsCompleted();

        int chunkW  = chunk.width;
        int clearEnd = Mathf.Min(chunk.height, ceilingThicknessPx);
        Color32 air  = new Color32(0, 0, 0, 0);

        // 하단 ceilingThicknessPx 행을 air로 클리어 (바위 낙하 구멍)
        for (int y = 0; y < clearEnd; y++)
        {
            int rowOffset = y * chunkW;
            for (int x = 0; x < chunkW; x++)
            {
                data.BasePixels[rowOffset + x]    = air;
                data.PixelInfo [rowOffset + x] = 0; // PIXEL_ID_AIR
            }
        }

        chunk.isTextureDirty = true;
        chunk.isDirty        = true;

        // 시각 갱신 (Visualizer 대신 외부 API 사용)
        chunk.DoVisualUpdate(new RectInt(0, 0, chunkW, clearEnd));
        chunk.EnsureJobsCompleted();

        chunk.ApplyTexture();

        Debug.Log($"[RollingRockTrap] 천장 제거 완료 — {clearEnd}px ({chunk.name})");
    }
}
