using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPooler : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public PoolableType type; // Changed from string tag
        public GameObject prefab;
        public int size;
    }

    #region Singleton
    public static ObjectPooler Instance;

    private void Awake()
    {
        Debug.Log("ObjectPooler Awake 실행됨: " + gameObject.name);

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate ObjectPooler found. Destroying this one: " + gameObject.name);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        poolDictionary = new Dictionary<PoolableType, Queue<GameObject>>();

        foreach (Pool pool in pools)
        {
            Queue<GameObject> objectPool = new Queue<GameObject>();

            for (int i = 0; i < pool.size; i++)
            {
                GameObject obj = Instantiate(pool.prefab);
                obj.SetActive(false);
                objectPool.Enqueue(obj);
            }

            poolDictionary.Add(pool.type, objectPool);
            Debug.Log("Pool 등록됨: " + pool.type);
        }
    }
    #endregion

    public List<Pool> pools;
    public Dictionary<PoolableType, Queue<GameObject>> poolDictionary;

    public GameObject SpawnFromPool(PoolableType type, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(type))
        {
            Debug.LogWarning("Pool with type " + type + " doesn't exist.");
            return null;
        }

        if (poolDictionary[type].Count == 0)
        {
            // Debug.LogWarning("Pool with type " + type + " is empty. Consider increasing the pool size.");
            // Optionally, instantiate a new object here if the pool is allowed to grow
            // Pool newPool = pools.Find(p => p.type == type);
            // if (newPool != null) return Instantiate(newPool.prefab);
            return null;
        }

        GameObject objectToSpawn = poolDictionary[type].Dequeue();

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;

        return objectToSpawn;
    }

    public void ReturnToPool(PoolableType type, GameObject objectToReturn)
    {
        if (!poolDictionary.ContainsKey(type))
        {
            Debug.LogError("Pool with type " + type + " doesn't exist.");
            return;
        }
        Debug.Log("!!! SCRIPT IS UPDATED !!! Returning object to pool: " + type);
        objectToReturn.SetActive(false);
        poolDictionary[type].Enqueue(objectToReturn);
    }
}