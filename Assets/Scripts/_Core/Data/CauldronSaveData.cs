// @tags: cauldron, save, data-container, dto
using System.Collections.Generic;

[System.Serializable]
public class CauldronSaveData
{
    public List<CauldronEntry> entries = new List<CauldronEntry>();
}

[System.Serializable]
public class CauldronEntry
{
    public int x;
    public int y;
    public int remainingUses;
}
