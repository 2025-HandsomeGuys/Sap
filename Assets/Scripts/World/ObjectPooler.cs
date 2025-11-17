using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            Debug.LogWarning($"Pool for {type} in layer {layerType} is empty.");
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
                // This is not a critical error, some minerals might not be in database.
                Debug.Log($"No MineralSO found for MineralID: {type}. This may be intentional.", objectToSpawn);
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