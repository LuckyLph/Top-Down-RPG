using UnityEngine;

public interface IRandom
{
    float Range(float minInclusive, float maxInclusive);
    Vector2 InsideUnitCircle();
}
