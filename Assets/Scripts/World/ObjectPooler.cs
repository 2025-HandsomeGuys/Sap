
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
        if (Instance != null && Instance != this)
        {
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
        }
    }
    #endregion

    public List<Pool> pools;
    public Dictionary<PoolableType, Queue<GameObject>> poolDictionary;

    public GameObject SpawnFromPool(PoolableType type, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(type))
        {
            return null;
        }

        if (poolDictionary[type].Count == 0)
        {
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
            return;
        }
        objectToReturn.SetActive(false);
        poolDictionary[type].Enqueue(objectToReturn);
    }
}
