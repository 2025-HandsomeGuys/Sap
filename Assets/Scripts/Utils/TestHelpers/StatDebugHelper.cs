using UnityEngine;

/// <summary>
/// 스탯 시스템이 잘 작동하는지 확인하기 위한 디버깅 헬퍼
/// Player 오브젝트에 추가해서 사용하세요.
/// </summary>
public class StatDebugHelper : MonoBehaviour
{
    [Header("조회할 스탯")]
    public StatType targetStat = StatType.MoveSpeed;

    [Header("테스트 버프 설정")]
    public float buffValue = 5f;
    public float buffDuration = 3f;

    private PlayerStat _playerStat;
    private BuffStatProvider _buffProvider;

    void Start()
    {
        _playerStat = GetComponent<PlayerStat>();
        _buffProvider = GetComponent<BuffStatProvider>();

        if (_playerStat != null)
        {
            // 스탯이 변할 때마다 로그 출력
            _playerStat.OnStatChanged += OnStatChanged;
        }
    }

    private void OnDestroy()
    {
        if (_playerStat != null)
        {
            _playerStat.OnStatChanged -= OnStatChanged;
        }
    }

    private void OnStatChanged()
    {
        // 너무 시끄러우면 주석 처리
        // Debug.Log("[StatDebug] 스탯이 재계산되었습니다.");
    }

    [ContextMenu("현재 스탯 값 로그 출력")]
    public void PrintCurrentValue()
    {
        if (_playerStat == null) _playerStat = GetComponent<PlayerStat>();

        float baseVal = _playerStat.GetBaseValue(targetStat);
        float finalVal = _playerStat.GetFinalValue(targetStat);

        Debug.Log($"<color=cyan>[Stat Check] {targetStat}</color> : 기준값({baseVal}) -> 최종값({finalVal})");
    }

    [ContextMenu("테스트 버프 추가 (3초)")]
    public void AddTestBuff()
    {
        if (_buffProvider == null) _buffProvider = GetComponent<BuffStatProvider>();
        if (_buffProvider == null)
        {
            Debug.LogError("BuffStatProvider가 없습니다!");
            return;
        }

        Debug.Log($"[Test] {targetStat}에 {buffValue}만큼 버프 추가 (지속시간 {buffDuration}초)");
        _buffProvider.AddBuff("TestBuff", targetStat, ModifierType.Flat, buffValue, buffDuration);
        
        // 적용 직후 확인
        PrintCurrentValue();
    }
}
