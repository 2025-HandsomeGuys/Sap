using UnityEngine;
using System.IO;

public class WorldSaveManager : MonoBehaviour
{
    // Using a static instance for easy access from anywhere.
    public static WorldSaveManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    void Update()
    {
        // Trigger save with a key press (e.g., F5)
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SaveWorld();
        }
    }

    public void SaveWorld()
    {
        if (WorldManager.Instance == null)
        {
            Debug.LogError("WorldManager not found. Cannot save world.");
            return;
        }

        string path = Path.Combine(Application.persistentDataPath, "world.json");
        Debug.Log($"Saving world to: {path}");

        try
        {
            WorldManager.Instance.SaveWorld(path);
            Debug.Log("World saved successfully!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save world: {e.Message}\n{e.StackTrace}");
        }
    }
}