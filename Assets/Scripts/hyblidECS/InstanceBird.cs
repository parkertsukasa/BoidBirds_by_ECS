using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public struct birdData
{
  public float3 position;
  public Quaternion rotation;
  public float3 moveVector;
  public float3 moveDesire;
}

public class InstanceBird : MonoBehaviour
{
  [SerializeField]
  private GameObject birdPrefab;

  [SerializeField]
  private float dist;

  [SerializeField]
  public static int number = 100;

  public static birdData[] birds = new birdData[number];

  // Start is called before the first frame update
  void Start()
  {
    for (int i = 0; i < number; i++)
    {
      var go = Instantiate(birdPrefab) as GameObject;

      BoidMove boidMove = go.GetComponent<BoidMove>();
      boidMove.Id = i;
      go.transform.position = UnityEngine.Random.insideUnitSphere * dist;
      go.transform.rotation = UnityEngine.Random.rotation;
      go.GetComponent<Animator>().speed = UnityEngine.Random.Range(0.5f, 1.5f);

      birds[i].position = go.transform.position;
      birds[i].rotation = go.transform.rotation;
      birds[i].moveVector = float3.zero;
      birds[i].moveDesire = float3.zero;
    }
  }

  // Update is called once per frame
  void Update()
  {
        
  }
}
