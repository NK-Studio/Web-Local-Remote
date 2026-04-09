using UnityEngine;

public class Rotate : MonoBehaviour
{
    public float speed;

    private void Update()
    {
        transform.Rotate(0, speed * 10 * Time.deltaTime, 0, Space.World);
    }
}