// @tags: interface, dig, terrain, mining
using UnityEngine;

public interface IDiggable
{
    void Dig(Vector2 worldPos, float radius, int toolIndex);
}
