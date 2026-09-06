// @tags: input, save, world-save, keyboard, disk
using UnityEngine;

public class WorldInputController : MonoBehaviour
{
    [Header("Input Settings")]
    [Tooltip("Key to save world data manually")]
    public KeyCode saveKey = KeyCode.F5;
    
    [Tooltip("Key to toggle auto-save on exit")]
    public KeyCode toggleSaveKey = KeyCode.F6;

    private void Update()
    {
        if (Input.GetKeyDown(saveKey))
        {
            if (InfinityMapManager.Instance != null)
            {
                InfinityMapManager.Instance.SaveAllData();
                Debug.Log($"[WorldInputController] Manual Save ({saveKey})");
            }
        }

        if (Input.GetKeyDown(toggleSaveKey))
        {
            if (InfinityMapManager.Instance != null)
            {
                InfinityMapManager.Instance.enableDiskSave = !InfinityMapManager.Instance.enableDiskSave;
                string status = InfinityMapManager.Instance.enableDiskSave ? "ENABLED" : "DISABLED";
                Debug.Log($"[WorldInputController] Disk Save Feature {status}");
            }
        }
    }
}
