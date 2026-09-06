// @tags: earnings, ledger, save, data, daysummary, gold
using System;

/// <summary>
/// 하루 동안의 카테고리별 골드 증감 기록 (침대 수면 정산 연출용).
/// PlayerData.dayEarnings로 SaveManager에 엮인다 (CoinSaveData와 동일 패턴).
/// </summary>
[Serializable]
public class DayEarningsSaveData
{
    public bool hasData;
    public int dayStartGold;   // 오늘 아침 기준 보유 골드 (최종 손익 = 현재 골드 - 이 값)
    public int mineralSale;    // 광물 판매 수익 (+)
    public int stock;          // 주식 실현 손익 (매도/강제매각 시 매도금액-매입원가만 집계, 매수는 미기록)
    public int coin;           // 코인 순손익 (fee·출금한도·청산 반영 후 delta)
    public int shopPurchase;   // 상점 구매 지출 (-)
    public int upgrade;        // 업그레이드 지출 (-)
}
