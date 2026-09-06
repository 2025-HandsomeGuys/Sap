using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.Entities
{
    /// <summary>
    /// 빙하 슬라이드 구역 등에 배치되어, 플레이어가 통과 시 즉발 파괴되는 얼음 장애물입니다.
    /// </summary>
    public class FragileIceBlock : MonoBehaviour
    {
        [Header("Effects")]
        [Tooltip("파괴 시 재생될 파티클(얼음 조각 등) 프리팹")]
        public GameObject breakVFXPrefab;

        [Tooltip("파괴될 때 발생시킬 오디오 클립 (옵션)")]
        public AudioClip breakSound;

        private void OnTriggerEnter2D(Collider2D collision)
        {
            // "Player" 태그를 가진 오브젝트와 충돌했을 때 파괴
            if (collision.CompareTag("Player"))
            {
                BreakIce();
            }
        }

        private void BreakIce()
        {
            // 파티클 스폰
            if (breakVFXPrefab != null)
            {
                // 약간 랜덤한 회전값을 주어 파티클 생성
                Instantiate(breakVFXPrefab, transform.position, Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
            }

            // 인스펙터 클립 우선(기존 프리팹 설정 보존), 없으면 SoundManager 키 경로
            if (breakSound != null)
            {
                // PlayClipAtPoint는 믹서 그룹 없는 임시 소스를 만든다 → 효과음 슬라이더가 안 먹는다.
                AudioRouting.PlayClipAt(breakSound, transform.position, 1.0f);
            }
            else if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXAt(SfxKeys.IceBreak, transform.position);
            }

            // 오브젝트 즉시 삭제
            Destroy(gameObject);
        }
    }
}
