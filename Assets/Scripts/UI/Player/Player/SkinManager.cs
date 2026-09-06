using UnityEngine;
using UnityEngine.U2D.Animation;
using System.Collections.Generic;
using System; // ★ 이벤트 사용을 위해 추가됨

[System.Serializable]
public class CostumeData
{
    public string costumeName = "의상 이름";
    [Tooltip("머리, 몸통, 신발이 모두 포함된 통합 에셋")]
    public SpriteLibraryAsset costumeAsset;
}

public class SkinManager : MonoBehaviour
{
    // ★ 껍데기들이 구독할 스킨 변경 알림 이벤트
    public event Action OnSkinChanged;

    [Header("부위별 라이브러리 컴포넌트")]
    public SpriteLibrary hairSpriteLibrary;
    public SpriteLibrary bodySpriteLibrary;
    public SpriteLibrary leftShoeSpriteLibrary;  // 왼쪽 신발 컴포넌트 연결
    public SpriteLibrary rightShoeSpriteLibrary; // 오른쪽 신발 컴포넌트 연결

    [Header("기본 스킨 (장비 미착용)")]
    public SpriteLibraryAsset defaultAsset;

    [Header("보유 중인 의상(에셋) 목록")]
    public List<CostumeData> costumeList = new List<CostumeData>();

    [Header("테스트용: 현재 장착 중인 의상 번호 (-1은 기본스킨)")]
    public int hairIndex = -1;
    public int bodyIndex = -1;
    public int shoesIndex = -1;

    private void Start()
    {
        ApplyAllSkins();
    }

    private void OnValidate()
    {
        ApplyAllSkins();
    }

    // 설정된 인덱스에 맞춰 각 부위별로 스킨을 입혀주는 메서드
    private void ApplyAllSkins()
    {
        // 각 부위별 적용할 에셋 가져오기
        SpriteLibraryAsset targetHair = GetAssetByIndex(hairIndex);
        SpriteLibraryAsset targetBody = GetAssetByIndex(bodyIndex);
        SpriteLibraryAsset targetShoes = GetAssetByIndex(shoesIndex);

        // 컴포넌트가 연결되어 있을 때만 에셋 교체 (하나라도 연결 안 되어 있으면 에러 나는 현상 방지)
        if (hairSpriteLibrary != null)
            hairSpriteLibrary.spriteLibraryAsset = targetHair;

        if (bodySpriteLibrary != null)
            bodySpriteLibrary.spriteLibraryAsset = targetBody;

        // ★ 핵심: 하나의 신발 에셋(targetShoes)을 왼쪽, 오른쪽 라이브러리에 동시에 적용
        if (leftShoeSpriteLibrary != null)
            leftShoeSpriteLibrary.spriteLibraryAsset = targetShoes;

        if (rightShoeSpriteLibrary != null)
            rightShoeSpriteLibrary.spriteLibraryAsset = targetShoes;

        // ★ 옷 교체가 끝난 직후, 연결된 껍데기들에게 알림을 보냄
        OnSkinChanged?.Invoke();
    }

    private SpriteLibraryAsset GetAssetByIndex(int index)
    {
        if (index >= 0 && index < costumeList.Count && costumeList[index].costumeAsset != null)
        {
            return costumeList[index].costumeAsset;
        }
        return defaultAsset;
    }

    // ==========================================
    // 외부(UI 버튼 등)에서 호출할 수 있는 함수들
    // ==========================================

    public void EquipHair(string targetName) { hairIndex = FindCostumeIndex(targetName); ApplyAllSkins(); }
    public void EquipBody(string targetName) { bodyIndex = FindCostumeIndex(targetName); ApplyAllSkins(); }
    public void EquipShoes(string targetName) { shoesIndex = FindCostumeIndex(targetName); ApplyAllSkins(); } // 한 번 호출로 양발 모두 적용됨

    public void EquipFullSet(string targetName)
    {
        int index = FindCostumeIndex(targetName);
        hairIndex = index;
        bodyIndex = index;
        shoesIndex = index;
        ApplyAllSkins();
    }

    public void RemoveHair() { hairIndex = -1; ApplyAllSkins(); }
    public void RemoveBody() { bodyIndex = -1; ApplyAllSkins(); }
    public void RemoveShoes() { shoesIndex = -1; ApplyAllSkins(); }

    public void RemoveAllEquip()
    {
        hairIndex = -1;
        bodyIndex = -1;
        shoesIndex = -1;
        ApplyAllSkins();
    }

    private int FindCostumeIndex(string targetName)
    {
        for (int i = 0; i < costumeList.Count; i++)
        {
            if (costumeList[i].costumeName == targetName)
                return i;
        }
        Debug.LogWarning($"[SkinManager] '{targetName}' 의상을 찾을 수 없습니다.");
        return -1;
    }
}