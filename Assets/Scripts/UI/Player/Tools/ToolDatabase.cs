using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ToolDatabase", menuName = "Database/Tool Database")]
public class ToolDatabase : ScriptableObject
{
    public static ToolDatabase Instance { get; private set; }

    public List<ToolSO> allTools;

    private Dictionary<ToolID, ToolSO> toolDictionary;

    private void OnEnable()
    {
        Instance = this;

        toolDictionary = new Dictionary<ToolID, ToolSO>();
        if (allTools != null)
        {
            foreach (var tool in allTools)
            {
                if (tool != null && tool.toolID != ToolID.None && !toolDictionary.ContainsKey(tool.toolID))
                {
                    toolDictionary.Add(tool.toolID, tool);
                }
            }
        }
    }

    public ToolSO GetToolByID(ToolID id)
    {
        if (toolDictionary == null)
        {
            OnEnable();
        }

        toolDictionary.TryGetValue(id, out ToolSO tool);
        return tool;
    }
}

