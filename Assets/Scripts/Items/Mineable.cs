using UnityEngine;

public class Mineable : MonoBehaviour
{
    public MineralID mineralID; // The unique ID of this mineral type
    public InterfaceInventoryItem itemData; // PlayerController가 접근해야 하는 데이터 (ItemSO, MineralSO, ToolSO)
    public LayerType spawnedFromLayer; // ObjectPooler가 스폰 시 설정해주는 필드
}