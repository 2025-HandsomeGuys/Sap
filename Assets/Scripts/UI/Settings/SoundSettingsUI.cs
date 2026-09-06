using UnityEngine;
using UnityEngine.UI;

public class SoundSettingsUI : MonoBehaviour
{
    [Header("Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    private void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (SoundManager.Instance == null) return;

        // 슬라이더 초기 설정 (저장된 볼륨값 불러오기)
        if (masterSlider != null)
        {
            masterSlider.value = SoundManager.Instance.GetMasterVolume();
            masterSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        if (bgmSlider != null)
        {
            bgmSlider.value = SoundManager.Instance.GetBGMVolume();
            bgmSlider.onValueChanged.AddListener(OnBGMVolumeChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.value = SoundManager.Instance.GetSFXVolume();
            sfxSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        }
    }

    private void OnMasterVolumeChanged(float value)
    {
        SoundManager.Instance.SetMasterVolume(value);
    }

    private void OnBGMVolumeChanged(float value)
    {
        SoundManager.Instance.SetBGMVolume(value);
    }

    private void OnSFXVolumeChanged(float value)
    {
        SoundManager.Instance.SetSFXVolume(value);
    }
}
