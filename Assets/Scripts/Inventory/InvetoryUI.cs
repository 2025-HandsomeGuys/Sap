using JetBrains.Annotations;
using UnityEngine;

public class InvetoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    bool activeInventory=false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {

        inventoryPanel.SetActive(activeInventory);
    }

    // Update is called once per frame
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            activeInventory = !activeInventory;
            inventoryPanel.SetActive(activeInventory);         
        }
    }
}
