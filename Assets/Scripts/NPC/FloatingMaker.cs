using UnityEngine;

public class FloatingMaker : MonoBehaviour
{
    [SerializeField] float amplitude = 0.2f;
    [SerializeField] float speed = 2f;
    [SerializeField] float rotateSpeed = 60f;

    Vector3 startPos;

    void Start(){ 
        startPos = transform.localPosition;
    }

    void Update()
    {
        transform.localPosition = startPos + Vector3.up * Mathf.Sin(Time.time * speed) * amplitude;
        transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime, Space.World);
    }
}
