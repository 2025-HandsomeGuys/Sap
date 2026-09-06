using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>플레이어가 밟으면 연동 타겟을 Activate, 이탈 시 Deactivate. targets는 임포터가 배선.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class PressurePlate : MonoBehaviour
    {
        [Tooltip("ILinkTarget 구현 컴포넌트(문/다트/가시). 임포터가 링크 그룹으로 채운다.")]
        public List<MonoBehaviour> targets = new List<MonoBehaviour>();
        [SerializeField] private string playerTag = "Player";

        [Tooltip("true면 한 번 밟으면 계속 활성(이탈해도 닫히지 않음). 문 뒤 보상처럼 혼자 지나가야 할 때 사용.")]
        [SerializeField] private bool latching = false;

        private int _contacts;
        private bool _latched;

        private void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            _contacts++;
            if (_contacts == 1)
            {
                foreach (var t in targets) (t as ILinkTarget)?.Activate();
                if (latching) _latched = true;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            _contacts = Mathf.Max(0, _contacts - 1);
            if (_latched) return; // latch 상태면 이탈해도 유지
            if (_contacts == 0) foreach (var t in targets) (t as ILinkTarget)?.Deactivate();
        }
    }
}
