using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

/*
public class AnimatedECS : MonoBehaviour
{
  // 最大Entity数
  [SerializeField]
  int _maxObjectNum = 10000;

  // 再生するアニメーションデータ
  [SerializeField]
  AnimationMesh[] _animationMeshes = null;

  // 表示領域
  [SerializeField]
  float _randomBoundSize = 64.0f;

  // Start is called before the first frame update
  void Start()
  {
    World.Active = new World("AnimatedECS");
    var entityManager = World.Active.CreateManager<EntityManager>();
    World.Active.CreateManager(typeof(EndFrameTransformSystem));
    

  }

  // Update is called once per frame
  void Update()
  {
    
  }
}

*/
