using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ⚠ [사용 중지] 이 풀은 코드에서 아무도 안 쓴다 — SpawnFromPool 호출부가 프로젝트에 0개다.
//    유일한 소비자였던 PickupableItem.RemoveFromWorld는 MineralGenerator 풀로 옮겼다.
//    (광물 풀은 MineralGenerator._mineralPools 하나로 일원화. 여기에 다시 연결하지 말 것 —
//     풀이 둘로 갈리면 같은 프리팹이 두 큐를 오간다.)
//
//    클래스를 아직 안 지운 이유: 컴포넌트가 씬 4곳에 배치돼 있어 지우면 Missing Script가 뜬다.
//      Scenes/Demo/DemoUnderground.unity
//      Scenes/Test_DongJin/CopyDemoUnderground.unity
//      Scenes/Test_Hanbin/khbScene.unity, khbScene 1.unity
//    씬에서 컴포넌트를 먼저 뗀 뒤 이 파일과 Mineable.spawnedFromLayer를 함께 제거하면 된다.
public class ObjectPooler : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public MineralID type;
        public GameObject prefab;
        public int size;
    }

    [System.Serializable]
    public class StratumPool
    {
        public LayerType layerType;
        public List<Pool> pools;
    }

    #region Singleton
    public static ObjectPooler Instance;
    #endregion

    public List<StratumPool> stratumPools;
    public Dictionary<LayerType, Dictionary<MineralID, Queue<GameObject>>> poolDictionary;
    
    // 경고 로그를 제한하기 위한 딕셔너리 (각 풀 타입별로 한 번만 경고)
    private Dictionary<string, bool> emptyPoolWarnings = new Dictionary<string, bool>();
    // ItemSO 로그를 제한하기 위한 딕셔너리 (각 MineralID별로 한 번만 로그)
    private Dictionary<MineralID, bool> itemSOWarnings = new Dictionary<MineralID, bool>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        poolDictionary = new Dictionary<LayerType, Dictionary<MineralID, Queue<GameObject>>>();

        foreach (StratumPool stratumPool in stratumPools)
        {
            var layerType = stratumPool.layerType;
            poolDictionary[layerType] = new Dictionary<MineralID, Queue<GameObject>>();

            GameObject stratumParent = new GameObject(layerType.ToString() + " Pool");
            stratumParent.transform.SetParent(transform);

            foreach (Pool pool in stratumPool.pools)
            {
                Queue<GameObject> objectPool = new Queue<GameObject>();
                GameObject poolParent = new GameObject(pool.type.ToString() + " Pool");
                poolParent.transform.SetParent(stratumParent.transform);

                for (int i = 0; i < pool.size; i++)
                {
                    GameObject obj = Instantiate(pool.prefab);
                    obj.name = $"{layerType}_{pool.type}_{i}";
                    obj.transform.SetParent(poolParent.transform);
                    obj.SetActive(false);
                    objectPool.Enqueue(obj);
                }
                poolDictionary[layerType].Add(pool.type, objectPool);
            }
        }
    }

    public bool HasPool(LayerType layerType, MineralID type)
    {
        return poolDictionary != null && 
               poolDictionary.ContainsKey(layerType) && 
               poolDictionary[layerType].ContainsKey(type);
    }

    public GameObject SpawnFromPool(LayerType layerType, MineralID type, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(layerType) || !poolDictionary[layerType].ContainsKey(type))
        {
            Debug.LogWarning($"Pool with layer {layerType} and type {type} doesn't exist.");
            return null;
        }

        if (poolDictionary[layerType][type].Count == 0)
        {
            // Optionally, you could instantiate a new object here if the pool is empty
            // 경고 로그를 한 번만 출력하도록 제한
            string warningKey = $"{layerType}_{type}";
            if (!emptyPoolWarnings.ContainsKey(warningKey))
            {
                emptyPoolWarnings[warningKey] = true;
                Debug.LogWarning($"Pool for {type} in layer {layerType} is empty. (This warning will only appear once per pool type)");
            }
            return null;
        }

        GameObject objectToSpawn = poolDictionary[layerType][type].Dequeue();

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;

        // Set the spawn context on the Mineable component
        if (objectToSpawn.TryGetComponent<Mineable>(out var mineable))
        {
            mineable.spawnedFromLayer = layerType;
            mineable.mineralID = type; // Set the mineralID

            // Find the corresponding MineralSO and assign it to itemData
            MineralSO mineralData = MineralDatabase.Instance?.GetMineralByID(type);
            if (mineralData != null)
            {
                mineable.itemData = mineralData;
            }
            else
            {
                // This is not a critical error, some minerals might not have items.
                // 각 MineralID별로 한 번만 로그 출력
                if (!itemSOWarnings.ContainsKey(type))
                {
                    itemSOWarnings[type] = true;
                    Debug.Log($"No ItemSO found for MineralID: {type}. This may be intentional. (This message will only appear once per mineral type)", objectToSpawn);
                }
            }
        }
        else
        {
            Debug.LogWarning($"Spawned object {objectToSpawn.name} is missing a Mineable component.", objectToSpawn);
        }

        return objectToSpawn;
    }

    public void ReturnToPool(GameObject objectToReturn)
    {
        if (!objectToReturn.TryGetComponent<Mineable>(out var mineable))
        {
            Debug.LogWarning("Returned object is not a valid mineable. Destroying it.", objectToReturn);
            Destroy(objectToReturn);
            return;
        }

        LayerType layerType = mineable.spawnedFromLayer;
        MineralID poolType = mineable.mineralID; // Use the stored mineralID

        if (!poolDictionary.ContainsKey(layerType) || !poolDictionary[layerType].ContainsKey(poolType))
        {
            Debug.LogWarning($"Pool with layer {layerType} and type {poolType} doesn't exist. Destroying object.", objectToReturn);
            Destroy(objectToReturn);
            return;
        }
        
        objectToReturn.SetActive(false);
        poolDictionary[layerType][poolType].Enqueue(objectToReturn);
    }
}