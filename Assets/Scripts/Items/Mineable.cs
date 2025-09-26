using UnityEngine;

public class Mineable : MonoBehaviour
{
    public ItemSO itemData; // PlayerController가 접근해야 하는 데이터
    public LayerType spawnedFromLayer; // ObjectPooler가 스폰 시 설정해주는 필드
}