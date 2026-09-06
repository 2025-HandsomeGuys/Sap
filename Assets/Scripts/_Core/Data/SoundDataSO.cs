// @tags: sound, scriptable-object, so, bgm, sfx, audio
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SoundAudioClip
{
    public string soundName;
    public AudioClip audioClip;
}

[CreateAssetMenu(fileName = "SoundData", menuName = "ScriptableObjects/SoundData")]
public class SoundDataSO : ScriptableObject
{
    [Header("BGM Clips")]
    public List<SoundAudioClip> bgmClips = new List<SoundAudioClip>();

    [Header("SFX Clips")]
    public List<SoundAudioClip> sfxClips = new List<SoundAudioClip>();
}
