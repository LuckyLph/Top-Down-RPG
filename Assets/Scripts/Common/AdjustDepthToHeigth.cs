using UnityEngine;

public class AdjustDepthToHeigth : MonoBehaviour
{
    void FixedUpdate()
    {
        Vector3 newPos = new Vector3(gameObject.transform.position.x, gameObject.transform.position.y, gameObject.transform.position.y);
        gameObject.transform.SetPositionAndRotation(newPos, Quaternion.identity);   
    }
}
