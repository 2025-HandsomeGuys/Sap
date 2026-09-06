using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>
/// 아이템의 액티브 효과(즉시 회복, 버프, 유틸리티 등)를 관리하는 클래스입니다.
/// </summary>
public class ItemActiveEffectManager : MonoBehaviour
{
    public static ItemActiveEffectManager Instance { get; private set; }

    [Header("References")]
    public PlayerStat playerStat;
    public PlayerMining playerMining;
    public StaminaManager staminaManager;
    public string baseSceneName = "DemoUpground";

    [Header("UI References")]
    public RectTransform effectContainer;
    public GameObject effectPrefab;

    // 현재 활성화된 버프 목록 및 추적용 딕셔너리
    private List<ActiveBuff> activeBuffs = new List<ActiveBuff>();
    private Dictionary<string, Coroutine> activeBuffCoroutines = new Dictionary<string, Coroutine>();
    private Dictionary<string, ActiveEffectUIItem> activeBuffUIs = new Dictionary<string, ActiveEffectUIItem>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (playerStat == null) playerStat = GetComponentInParent<PlayerStat>();
        if (playerMining == null) playerMining = GetComponentInParent<PlayerMining>();
        if (staminaManager == null) staminaManager = GetComponentInParent<StaminaManager>();
    }

    /// <summary>
    /// 아이템 효과를 적용합니다.
    /// </summary>
    public void UseItem(ItemSO item)
    {
        if (item == null) return;

        Debug.Log($"[ItemActive] Using item: {item.DisplayName} (Type: {item.activeType})");

        switch (item.activeType)
        {
            case ItemActiveType.Instant:
                ApplyInstantEffects(item.effects);
                break;
            case ItemActiveType.Buff:
                ApplyBuffEffects(item.effects, item.DisplayName, item.icon);
                break;
            case ItemActiveType.Hybrid:
                ApplyInstantEffects(item.effects);
                ApplyBuffEffects(item.effects, item.DisplayName, item.icon);
                break;
            case ItemActiveType.Utility:
                ApplyUtilityEffects(item);
                break;
        }
    }

    private void ApplyInstantEffects(List<ItemEffectData> effects)
    {
        foreach (var data in effects)
        {
            switch (data.effect)
            {
                case ItemActiveEffect.RestoreStamina:
                    playerStat.RecoverStamina(data.value);
                    break;
                case ItemActiveEffect.RestoreDrillBattery:
                    if (playerMining != null) playerMining.RefillBattery(data.value);
                    break;
                case ItemActiveEffect.RestoreMaxStaminaLoss:
                    if (staminaManager != null) staminaManager.RecoverStatus(data.value, data.value, data.value);
                    break;
                case ItemActiveEffect.RestoreRadiation:
                    // 방사선은 전용 아이템으로만 빠진다 (RestoreMaxStaminaLoss는 부상·화상·동상만)
                    if (staminaManager != null) staminaManager.RecoverStatus(0f, 0f, 0f, data.value);
                    break;
            }
        }
    }

    private void ApplyBuffEffects(List<ItemEffectData> effects, string sourceName, Sprite icon)
    {
        // 동일한 소스의 버프가 이미 실행 중이라면 중단 및 제거
        if (activeBuffCoroutines.TryGetValue(sourceName, out Coroutine existingCoroutine))
        {
            StopCoroutine(existingCoroutine);
            activeBuffCoroutines.Remove(sourceName);
            CleanupExistingBuff(sourceName);
        }

        // 새 버프 루틴 시작
        Coroutine newCoroutine = StartCoroutine(BuffRoutine(effects, sourceName, icon));
        activeBuffCoroutines[sourceName] = newCoroutine;
    }

    private void CleanupExistingBuff(string sourceName)
    {
        // 해당 소스의 기존 버프들 제거
        activeBuffs.RemoveAll(b => {
            if (b.SourceName == sourceName)
            {
                playerStat.UnregisterProvider(b);
                return true;
            }
            return false;
        });

        // 해당 소스의 UI 제거
        if (activeBuffUIs.TryGetValue(sourceName, out ActiveEffectUIItem ui))
        {
            if (ui != null) Destroy(ui.gameObject);
            activeBuffUIs.Remove(sourceName);
        }
    }

    private IEnumerator BuffRoutine(List<ItemEffectData> effects, string sourceName, Sprite icon)
    {
        float maxDuration = 0;
        List<ActiveBuff> currentItemBuffs = new List<ActiveBuff>();

        // UI 생성 (아이템 하나당 하나의 아이콘)
        ActiveEffectUIItem uiItem = null;
        
        foreach (var data in effects)
        {
            if (data.duration <= 0) continue;

            StatType statType = GetStatTypeFromEffect(data.effect);
            if (statType == StatType.None) continue;

            if (data.duration > maxDuration) maxDuration = data.duration;

            // 버프 생성 및 등록
            ActiveBuff newBuff = new ActiveBuff(statType, data.modifierType, data.value, sourceName);
            activeBuffs.Add(newBuff);
            currentItemBuffs.Add(newBuff);
            playerStat.RegisterProvider(newBuff);

            Debug.Log($"[Buff] Started: {data.effect} {(data.modifierType == ModifierType.Flat ? "+" : "x")}{data.value} from {sourceName}");
        }

        if (currentItemBuffs.Count > 0 && effectContainer != null && effectPrefab != null)
        {
            GameObject obj = Instantiate(effectPrefab, effectContainer);
            uiItem = obj.GetComponent<ActiveEffectUIItem>();
            if (uiItem != null)
            {
                uiItem.Initialize(icon, maxDuration);
                activeBuffUIs[sourceName] = uiItem;
            }
        }

        yield return new WaitForSeconds(maxDuration);

        // 버프 해제
        foreach (var buff in currentItemBuffs)
        {
            playerStat.UnregisterProvider(buff);
            activeBuffs.Remove(buff);
        }

        // UI 및 데이터 정리
        if (uiItem != null)
        {
            Destroy(uiItem.gameObject);
            if (activeBuffUIs.ContainsKey(sourceName) && activeBuffUIs[sourceName] == uiItem)
                activeBuffUIs.Remove(sourceName);
        }

        if (activeBuffCoroutines.ContainsKey(sourceName))
            activeBuffCoroutines.Remove(sourceName);

        Debug.Log($"[Buff] All effects ended for: {sourceName}");
    }

    private void ApplyUtilityEffects(ItemSO item)
    {
        foreach (var data in item.effects)
        {
            if (data.effect == ItemActiveEffect.ReturnToBase)
            {
                StartCoroutine(ReturnToBaseRoutine(item.castTime));
            }
        }
    }

    private IEnumerator ReturnToBaseRoutine(float castTime)
    {
        if (castTime > 0)
        {
            Debug.Log($"[Utility] Casting ReturnToBase... ({castTime}s)");
            // 여기에 캐스팅 UI 연동 가능
            yield return new WaitForSeconds(castTime);
        }

        Debug.Log("[Utility] Returning to base...");
        SceneLoader.LoadScene(baseSceneName);
    }

    private StatType GetStatTypeFromEffect(ItemActiveEffect effect)
    {
        switch (effect)
        {
            case ItemActiveEffect.StaminaRegenBoost: return StatType.StaminaRegen;
            case ItemActiveEffect.MaxStaminaBoost: return StatType.MaxStamina;
            case ItemActiveEffect.FrostResist: return StatType.HazardFrostResist;
            case ItemActiveEffect.BurnResist: return StatType.HazardBurnResist;
            case ItemActiveEffect.AllEnvResist: return StatType.EnvironmentResistance;
            case ItemActiveEffect.ChargeTimeReduce: return StatType.ToolChargeTimeReduce;
            case ItemActiveEffect.RareMineralDetection: return StatType.RareMineralChance;
            case ItemActiveEffect.ClimbStaminaReduce: return StatType.StaminaCostPerSecond;
            default: return StatType.None;
        }
    }

    /// <summary>
    /// 버프 효과를 제공하는 IStatProvider 구현 클래스
    /// </summary>
    private class ActiveBuff : IStatProvider
    {
        private List<StatModifier> modifiers = new List<StatModifier>();
        public string SourceName { get; }

        // 붙이는 방식은 ItemEffectData.modifierType이 정한다. 기본값이 Flat이라
        // 그 필드를 안 채운 기존 에셋은 예전과 똑같이 합연산으로 붙는다.
        // StaminaCostPerSecond처럼 기준값이 배율/비용인 스탯만 Percent를 쓴다.
        public ActiveBuff(StatType type, ModifierType modifierType, float value, string sourceName)
        {
            SourceName = sourceName;
            modifiers.Add(new StatModifier(type, modifierType, value, ModifierSource.Other));
        }

        public IReadOnlyList<StatModifier> GetModifiers() => modifiers;
    }
}
