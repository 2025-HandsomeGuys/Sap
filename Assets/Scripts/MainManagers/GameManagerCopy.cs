using UnityEngine;

public class GameManagerCopy : MonoBehaviour
{
    public PlayerStat playerStats;
    public SaveManagerCopy saveManagerCopy;

    void Start()
    {
        
        saveManagerCopy.Load(playerStats);
    }

    void OnApplicationQuit()
    {
        
        saveManagerCopy.Save(playerStats);
    }

    
    public void OnNewGameButton()
    {
        saveManagerCopy.NewGame(playerStats);
    }
}
