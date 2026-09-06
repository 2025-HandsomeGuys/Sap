using System.Collections;
using UnityEngine;

namespace Gameplay.Environment
{
    [RequireComponent(typeof(Collider2D))]
    public class IceBreakable : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Delay in seconds before the object breaks after being touched.")]
        [SerializeField] private float delayBeforeBreak = 1.0f;
        
        [Tooltip("Tag to check for collision. Usually 'Player'.")]
        [SerializeField] private string playerTag = "Player";

        [Header("Visuals & Effects")]
        [Tooltip("The prefab containing the fractured/broken 2D pieces of this object.")]
        [SerializeField] private GameObject fracturedPrefab;
        
        [Tooltip("Optional particle system to play when breaking (e.g., ice shards).")]
        [SerializeField] private ParticleSystem breakParticles;
        
        [Tooltip("Optional sound effect to play when breaking.")]
        [SerializeField] private AudioClip breakSound;

        [Header("Physics Settings for Fractured Pieces")]
        [Tooltip("Explosive force applied to the broken pieces to make them shatter outward.")]
        [SerializeField] private float explosionForce = 15f;
        [Tooltip("Radius of the explosion force.")]
        [SerializeField] private float explosionRadius = 3f;
        [Tooltip("Upwards modifier to make pieces fly up slightly.")]
        [SerializeField] private float upwardsModifier = 2f;

        private bool isTriggered = false;

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!isTriggered && collision.collider.CompareTag(playerTag))
            {
                TriggerBreak();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!isTriggered && other.CompareTag(playerTag))
            {
                TriggerBreak();
            }
        }

        private void TriggerBreak()
        {
            isTriggered = true;
            StartCoroutine(BreakRoutine());
        }

        private IEnumerator BreakRoutine()
        {
            yield return new WaitForSeconds(delayBeforeBreak);

            BreakObject();
        }

        private void BreakObject()
        {
            // 1. Play sound — 인스펙터 클립 우선(기존 프리팹 설정 보존), 없으면 SoundManager 키 경로
            if (breakSound != null)
            {
                // PlayClipAtPoint는 믹서 그룹 없는 임시 소스를 만든다 → 효과음 슬라이더가 안 먹는다.
                AudioRouting.PlayClipAt(breakSound, transform.position);
            }
            else if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXAt(SfxKeys.IceBreak, transform.position);
            }

            // 2. Play particles
            if (breakParticles != null)
            {
                ParticleSystem particles = Instantiate(breakParticles, transform.position, transform.rotation);
                particles.Play();
                Destroy(particles.gameObject, particles.main.duration + particles.main.startLifetime.constantMax);
            }

            // 3. Spawn fractured prefab and apply 2D explosive force
            if (fracturedPrefab != null)
            {
                GameObject fracturedObj = Instantiate(fracturedPrefab, transform.position, transform.rotation);
                fracturedObj.transform.localScale = transform.localScale;

                Rigidbody2D[] rbs = fracturedObj.GetComponentsInChildren<Rigidbody2D>();
                Vector2 explosionCenter = transform.position;

                foreach (Rigidbody2D rb in rbs)
                {
                    // 2D Explosion calculation
                    Vector2 piecePosition = rb.transform.position;
                    Vector2 direction = piecePosition - explosionCenter;
                    
                    // Add some random upward direction
                    direction.y += upwardsModifier * Random.Range(0.5f, 1.5f);
                    direction = direction.normalized;

                    float distance = Vector2.Distance(explosionCenter, piecePosition);
                    float forceMultiplier = 1f - Mathf.Clamp01(distance / explosionRadius);
                    
                    float randomForce = explosionForce * Random.Range(0.7f, 1.3f);
                    
                    rb.AddForce(direction * (randomForce * forceMultiplier), ForceMode2D.Impulse);
                    
                    // Optional: Add some random torque for spinning pieces
                    rb.AddTorque(Random.Range(-50f, 50f), ForceMode2D.Impulse);
                }
                
                Destroy(fracturedObj, 5f);
            }
            else
            {
                Debug.LogWarning("IceBreakable: No fractured prefab assigned! Object will just disappear.");
            }

            // 4. Destroy the original object
            Destroy(gameObject);
        }
    }
}
