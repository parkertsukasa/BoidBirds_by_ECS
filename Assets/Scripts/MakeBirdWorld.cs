using System;
using UnityEngine;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

/* ---------- MoveVector ----------
 * 現在進んでいる方向を各エージェントごとに保持するためのData
 */
public struct MoveVector : IComponentData
{
  public float3 Value;
  public MoveVector(float3 value) => Value = value;

}

[RequireComponent(typeof(Camera))]
public class MakeBirdWorld : MonoBehaviour
{

  [SerializeField]
  private int numberBird;

  [SerializeField]
  private Mesh birdMesh;
  [SerializeField]
  private Material birdMaterial;

  private System.Random random = new System.Random();

  // Start is called before the first frame update
  void Start()
  {
    InitWorld();

    DrawBirdModel();
  }

  // Update is called once per frame
  void Update()
  {
      
  }

  private void OnDisable()
  {
    ScriptBehaviourUpdateOrder.UpdatePlayerLoop(null);
    _world?.Dispose();
  }

  private World _world;

  /* ---------- InitWorld ----------
   * Entityの初期設定を行う関数
   */
  void InitWorld()
  {
    _world = new World("BirdsWorld");
    _world.CreateManager(typeof(EntityManager));
    _world.CreateManager(typeof(EndFrameTransformSystem));
    _world.CreateManager(typeof(EndFrameBarrier));
    _world.CreateManager<MeshInstanceRendererSystem>().ActiveCamera = GetComponent<Camera>();
    _world.CreateManager(typeof(RenderingSystemBootstrap));

    // --- 鳥を制御するComponentを追加 ---
    _world.CreateManager(typeof(BoidBirds));

    ScriptBehaviourUpdateOrder.UpdatePlayerLoop(_world);
  }


  /* ---------- DrawBirdModel ----------
   * ECSの枠組みで鳥のモデルを描画する
   */
  void DrawBirdModel()
  {
    // --- 必要なTransform情報を初期化 ---
    var bird = new GameObject();
    bird.transform.position = Vector3.zero;
    bird.transform.rotation = Quaternion.identity;
    bird.transform.localScale = Vector3.one;

    var manager = _world?.GetOrCreateManager<EntityManager>();
    if (null != manager)
    {
      // --- ECSのデータ配列<Archetype>を作成する ---
    var archetype = manager.CreateArchetype(
        ComponentType.Create<Prefab>(), 
        ComponentType.Create<Position>(), 
        ComponentType.Create<Rotation>(),
        ComponentType.Create<MoveVector>(),
        ComponentType.Create<MeshInstanceRenderer>()
        );

      // --- 作成したArchetypeを元にPrefabを作成 ---
      var prefabEntity = manager.CreateEntity(archetype);
      
      // --- インスタンスするに当たって必要な情報を設定 ---
      /* memo : 
       * PositionやRotationなど，個々に値が変わってくるものは`SetComponentData`
       * MeshやMaterialなどのPrefabで共通するものは`SetSharedComponentData`
       */
      manager.SetComponentData(prefabEntity, new Position() { Value = float3.zero });
      manager.SetComponentData(prefabEntity, new Rotation() { Value = Quaternion.identity });
      manager.SetComponentData(prefabEntity, new MoveVector(new  float3(0.0f, 0.0f, 1.0f * Time.deltaTime)));

      manager.SetSharedComponentData(prefabEntity, new MeshInstanceRenderer()
      {
        mesh = birdMesh,
        material = birdMaterial,
        subMesh = 0,
        castShadows = UnityEngine.Rendering.ShadowCastingMode.Off,
        receiveShadows = false
      });

      // --- 設定したPrefabをインスタンスしていく ---
      int length = numberBird;
      //float halfLength = (float)length * 0.5f;
      NativeArray<Entity> entities = new NativeArray<Entity>(length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
      try
      {
        manager.Instantiate(prefabEntity, entities);
        for (int i = 0; i < length; i++)
        {
          manager.SetComponentData(entities[i], 
          //new Position{ Value = new float3(UnityEngine.Random.Range(-50, 50), UnityEngine.Random.Range(-50, 50), UnityEngine.Random.Range(-50, 50)) });
          new Position{ Value = new float3(random.Next(-50, 50), random.Next(-50, 50), random.Next(50)) });
        }
      }
      finally { entities.Dispose(); }

    }

    Destroy(bird);
  }

}


/* -------------------- BoidBirds --------------------
 * Boidアルゴリズムを用いて鳥を制御するComponent
 */
public sealed class BoidBirds : JobComponentSystem
{

  protected override JobHandle OnUpdate(JobHandle inputDeps)
  {
    var boidJob = new BoidJob
    {
      chunks = EntityManager.CreateArchetypeChunkArray(query, Allocator.TempJob),
      positionType = EntityManager.GetArchetypeChunkComponentType<Position>(false),
      rotationType = EntityManager.GetArchetypeChunkComponentType<Rotation>(false),
      moveVectorType = EntityManager.GetArchetypeChunkComponentType<MoveVector>(false),
      time = Time.realtimeSinceStartup
    };

    boidJob.chunks.Dispose();

    return boidJob.Schedule(boidJob.chunks.Length, 16, inputDeps);
  }

//  [BurstCompile(Accuracy.Med, Support.Relaxed)]
  struct BoidJob : IJobParallelFor
  {
    [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> chunks;
    public ArchetypeChunkComponentType<Position> positionType;
    public ArchetypeChunkComponentType<Rotation> rotationType;
    public ArchetypeChunkComponentType<MoveVector> moveVectorType;
    public float time;

    public unsafe void Execute(int chunkIndex)
    {
      var chunk = chunks[chunkIndex];
      // --- 使いたい値をNativeArrayの形式で取得する ---
      // memo : NativeArrayはデータが整列して置いてあるのでアクセスが高速になる
      var positions = chunk.GetNativeArray(positionType);
      var positionPtr = (Position*)positions.GetUnsafePtr();
      var rotations = chunk.GetNativeArray(rotationType);
      var moveVectors = chunk.GetNativeArray(moveVectorType);


      for (int c = 0; c < chunk.Count; c++ ,positionPtr++)
      {
        Calc calc = new Calc();

        for (int i = 0; i < chunk.Count; i++)
        {
          // --- 各要素のベクトル定義 ---
          float3 cohesionDesire = float3.zero;
          float3 separationDesire = float3.zero;
          float3 alignmentDesire = float3.zero;

          // --- Boidアルゴリズムを用いて移動欲求を決める ---
          for (int j = 0; j < chunk.Count; j++)
          {          
            var myPosition = positions[i];

            if (i != j)
            {
              var tempPosition = positions[j];
              // --- 差分ベクトルを求める ---
              var diffPosition = new Position();
              diffPosition.Value = tempPosition.Value - myPosition.Value;

              // --- ベクトルの長さを求める ---
              float length = calc.GetVectorLength(diffPosition.Value);

              // --- Cohesion ---
              cohesionDesire += diffPosition.Value;
              float kCohesion = 0.01f * Time.deltaTime;
              cohesionDesire *= kCohesion;

              // --- Separation ---
              separationDesire += ((diffPosition.Value / length) * -1);
              float kSeparation = 0.1f * Time.deltaTime;
              separationDesire *= kSeparation;

              // --- Alignment ---
              var tempMoveVector = moveVectors[j];
              alignmentDesire += tempMoveVector.Value;
              float kAlignment = (1.0f * Time.deltaTime);
              alignmentDesire *= kAlignment;
            }

            // --- 移動欲求を移動ベクトルに反映させる ---
            float3 moveDesire = cohesionDesire + separationDesire + alignmentDesire;

            // ----- 基礎情報の計算 -----
            var myMoveVector =  moveVectors[i];

            // 鳥の正面ベクトルのXZ平面における長さ
            float forwardLengthXZ = calc.GetVector2Length(myMoveVector.Value.x, myMoveVector.Value.z);
            // 鳥の正面ベクトルのYZ平面における長さ
            float forwardLengthYZ = calc.GetVector2Length(myMoveVector.Value.y, myMoveVector.Value.z);

            // 鳥の移動欲求のXZ平面における長さ
            float moveDesireLengthXZ = calc.GetVector2Length(moveDesire.x, moveDesire.z);
            // 鳥の移動欲求のYZ平面における長さ
            float moveDesireLengthYZ = calc.GetVector2Length(moveDesire.y, moveDesire.z);

            // --- Yawを計算 ---
            float yawF = math.atan2(myMoveVector.Value.x, myMoveVector.Value.z);
            float yawM = math.atan2(moveDesire.x, moveDesire.z);
            float yawDiff = yawM - yawF;
            float kYaw = 0.1f;
            yawDiff *= kYaw;
            float newYaw = yawF + yawDiff;

            // --- Pitchを計算 ---
            float pitchF = math.atan2(myMoveVector.Value.y, forwardLengthXZ);
            float pitchM = math.atan2(moveDesire.y, moveDesireLengthXZ);
            float pitchDiff = pitchM - pitchF;
            float kPitch = 0.1f;
            pitchDiff *= kPitch;
            float newPitch = pitchF + pitchDiff;

            // --- 計算した角度を元にベクトルを再構築 ---
            float3 moveDirection;
            moveDirection.x = math.sin(newYaw) * forwardLengthXZ;
            moveDirection.z = math.cos(newYaw) * forwardLengthXZ;
            moveDirection.y = math.sin(newPitch) * forwardLengthYZ;


            // --- 進行方向を向かせる ---
            float rotX = math.atan2(moveVectors[i].Value.y, 
                  calc.GetVector2Length(moveVectors[i].Value.x, moveVectors[i].Value.z));
            float rotY  = math.atan2(moveVectors[i].Value.x, moveVectors[i].Value.z);

            // --- ラジアンからの変換 ---
            rotX = calc.Rad2Deg(rotX);
            rotY = calc.Rad2Deg(rotY);

            // --- 反映 ---
            var rotationValue = rotations[i];
            rotationValue.Value = Quaternion.Euler(rotX, rotY, 0.0f);
            rotations[i] = rotationValue;

            myPosition.Value += moveDirection;
            positions[i] = myPosition;
            moveVectors[i] = new MoveVector(moveDirection);
          }
        }
      }
    }
  }

  private readonly EntityArchetypeQuery query = new EntityArchetypeQuery
  {
    Any = System.Array.Empty<ComponentType>(),
    None = System.Array.Empty<ComponentType>(),
    All = new ComponentType[] { ComponentType.Create<Position>(),
                                ComponentType.Create<Rotation>(),
                                ComponentType.Create<MoveVector>(),
                                ComponentType.Create<MeshInstanceRenderer>() }
  };
}

/* --------------------- Calc --------------------
 * 計算関係の関数を置いておく関数
 */
public class Calc
{
  // ----- ベクトルの長さを返す関数 -----
    public float GetVectorLength(float3 v)
    {
      float length = math.sqrt( (v.x * v.x) + (v.y * v.y) + (v.z * v.z));

      return length;
    }

    // ----- 2次元ベクトルの長さを返す関数 -----
    public float GetVector2Length(float x, float y)
    {
      float length = math.sqrt( (x * x) + (y * y) );

      return length;
    }

  // ----- ラジアンから度数への変換 -----
  public float Rad2Deg(float rad) => (rad * 180.0f) / (float)math.PI;
}
