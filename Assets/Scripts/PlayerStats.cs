using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public float originalMaxStamina = 500f;
    public float maxStamina;
    public float currentStamina;

    void Awake()
    {
        maxStamina = originalMaxStamina;
        currentStamina = maxStamina;
    }

    public void UseStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
    }

    public void RecoverStamina(float amount)
    {
        currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
    }

    public void ReduceMaxStamina(float amount)
    {
        maxStamina = Mathf.Max(0f, maxStamina - amount);
        if (currentStamina > maxStamina)
            currentStamina = maxStamina;
    }

}
