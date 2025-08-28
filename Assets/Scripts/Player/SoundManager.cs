
using UnityEngine;
using System.Collections.Generic;

// This struct allows you to associate a name with an AudioClip in the Inspector.
[System.Serializable]
public class SoundAudioClip
{
    public string soundName;
    public AudioClip audioClip;
}

public class SoundManager : MonoBehaviour
{
    #region Singleton
    public static SoundManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Ensure SoundManager persists between scenes
        }
    }
    #endregion

    [Header("Audio Clips")]
    // You can drag and drop your audio clips here in the Unity Inspector.
    public SoundAudioClip[] audioClips;

    private AudioSource audioSource;
    private Dictionary<string, AudioClip> audioClipDict;

    void Start()
    {
        // Add an AudioSource component to this GameObject to play sounds.
        audioSource = gameObject.AddComponent<AudioSource>();

        // Populate the dictionary for quick lookups.
        audioClipDict = new Dictionary<string, AudioClip>();
        foreach (var soundClip in audioClips)
        {
            if (!audioClipDict.ContainsKey(soundClip.soundName))
            {
                audioClipDict.Add(soundClip.soundName, soundClip.audioClip);
            }
        }
    }

    /// <summary>
    /// Plays a sound effect by its name.
    /// </summary>
    /// <param name="soundName">The name of the sound to play (as defined in the Inspector).</param>
    public void PlaySound(string soundName)
    {
        if (audioClipDict.TryGetValue(soundName, out AudioClip clip))
        {
            // PlayOneShot allows multiple sounds to overlap, which is good for sound effects.
            audioSource.PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning("SoundManager: Sound not found: " + soundName);
        }
    }
}
