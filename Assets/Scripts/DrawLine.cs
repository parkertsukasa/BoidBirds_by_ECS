using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawLine  : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
        Ray ray = new Ray(transform.position, Vector3.up);
        RaycastHit hit;

        float dist = 10.0f;

        Debug.DrawLine(transform.position, ray.direction * dist);
    }
}
