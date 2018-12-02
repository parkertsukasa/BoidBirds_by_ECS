using System;
using UnityEngine;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;

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
  private static int numberBird = 700;
  
  public static int NumberBird { get => numberBird; set => numberBird = value; }

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
      int length = NumberBird;
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
public sealed class BoidBirds : ComponentSystem
{
  Calc calc = new Calc();

  float number = (float)MakeBirdWorld.NumberBird;

  // --- Archetypeを取得するためのQueryを定義 ---
  private readonly EntityArchetypeQuery query = new EntityArchetypeQuery
  {
    Any = System.Array.Empty<ComponentType>(),
    None = System.Array.Empty<ComponentType>(),
    All = new ComponentType[] { ComponentType.Create<Position>(), 
                                ComponentType.Create<Rotation>(),
                                ComponentType.Create<MeshInstanceRenderer>()}
  };
  protected override void OnUpdate()
  {
    var chunks = EntityManager.CreateArchetypeChunkArray(query, Allocator.TempJob);
    var positionType = GetArchetypeChunkComponentType<Position>(false); 
    var rotationType = GetArchetypeChunkComponentType<Rotation>(false);
    var moveVectorType = GetArchetypeChunkComponentType<MoveVector>(false);

    for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++ )
    {
      var chunk = chunks[chunkIndex];
      // --- 使いたい値をNativeArrayの形式で取得する ---
      // memo : NativeArrayはデータが整列して置いてあるのでアクセスが高速になる
      var positions = chunk.GetNativeArray(positionType);
      var rotations = chunk.GetNativeArray(rotationType);
      var moveVectors = chunk.GetNativeArray(moveVectorType);

      for (int i = 0; i < chunk.Count; i++)
      {
        // --- 各要素のベクトル定義 ---
        float3 cohesionDesire = float3.zero;
        float3 separationDesire = float3.zero;
        float3 alignmentDesire = float3.zero;

        // --- 餌に向かうベクトル ---
        float3 hungryDesire = float3.zero;

        var myPosition = positions[i];

        // --- Boidアルゴリズムを用いて移動欲求を決める ---
        for (int j = 0; j < chunk.Count; j++)
        {          
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
            
            // --- Separation ---
            separationDesire += ((diffPosition.Value / length) * (1 / length) * -1);

            // --- Alignment ---
            var tempMoveVector = moveVectors[j];
            alignmentDesire += tempMoveVector.Value;

            // --- 餌に向かう ---
            float3 toTarget = float3.zero - myPosition.Value;
            hungryDesire += toTarget;
          }
        }

        // --- 係数をかけて調整 ---
        float kCohesion = 3.0f / number;
        cohesionDesire *= kCohesion;

        float kSeparation = 3.0f / number;
        separationDesire *= kSeparation;

        float kAlignment = 7.0f / number;
        alignmentDesire *= kAlignment;

        float kHungry = 0.1f / number;
        hungryDesire *= kHungry;


        // --- 移動欲求を移動ベクトルに反映させる ---
        float3 moveDesire = cohesionDesire + separationDesire + alignmentDesire + hungryDesire;

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
        float yawMove = yawM - yawF;
        float kYaw = 0.02f;
        float yawDiff = yawMove * kYaw;
        float newYaw = yawF + yawDiff;

        // --- Pitchを計算 ---
        float pitchF = math.atan2(myMoveVector.Value.y, forwardLengthXZ);
        float pitchM = math.atan2(moveDesire.y, moveDesireLengthXZ);
        float pitchMove = pitchM - pitchF;
        float kPitch = 0.01f;
        float pitchDiff = pitchMove * kPitch;
        float newPitch = pitchF + pitchDiff;

        // --- 速度の調整 ---
        float maxSpeed = 3.0f;
        float nowVelocityXZ = forwardLengthXZ * math.cos(yawMove);
        float desireVelocityXZ = moveDesireLengthXZ * math.cos(yawMove);
        float thrustXZ = desireVelocityXZ - nowVelocityXZ;
        forwardLengthXZ += thrustXZ * 1.0f * Time.deltaTime;
        
        float nowVelocityYZ = forwardLengthYZ * math.sin(pitchMove);
        float desireVelocityYZ = moveDesireLengthYZ * math.sin(pitchMove);
        float thrustYZ = desireVelocityYZ - nowVelocityYZ;
        forwardLengthYZ += thrustYZ * 1.0f * Time.deltaTime;

        if (maxSpeed < forwardLengthXZ)
          forwardLengthXZ = maxSpeed;

        if (maxSpeed < forwardLengthYZ)
          forwardLengthYZ = maxSpeed;

        // --- 計算した角度を元にベクトルを再構築 ---
        float3 moveDirection;
        moveDirection.x = math.sin(newYaw) * forwardLengthXZ;
        moveDirection.z = math.cos(newYaw) * forwardLengthXZ;
        moveDirection.y = math.sin(newPitch) * forwardLengthYZ;

        // --- 進行方向を向かせる ---
        float rotX = math.atan2(-moveDirection.y, 
              calc.GetVector2Length(moveDirection.x, moveDirection.z));
        float rotY  = math.atan2(moveDirection.x, moveDirection.z);
        float rotZ = yawMove * -0.2f;

        // --- ラジアンからの変換 ---
        if (rotX == 0.0f)
          rotX = 0.01f;
        else
          rotX = calc.Rad2Deg(rotX);
        
        if (rotY == 0.0f)
          rotY = 0.01f;
        else
          rotY = calc.Rad2Deg(rotY);

        if (rotZ == 0.0f)
          rotZ = 0.01f;
        else
          rotZ = calc.Rad2Deg(rotZ);


        // --- 反映 ---
        var rotationValue = rotations[i];
        if (rotX == 0.0f && rotY  == 0.0f)
          rotationValue.Value = Quaternion.identity;
        else
          rotationValue.Value = Quaternion.Euler(rotX, rotY, rotZ);

        rotations[i] = rotationValue;

        myPosition.Value += moveDirection;
        positions[i] = myPosition;
        moveVectors[i] = new MoveVector(moveDirection);
      }
    }
  
    chunks.Dispose();

  }
}

/* --------------------- Calc --------------------
 * 計算関係の関数を置いておく関数
 */
public class Calc
{
  // ----- ベクトルの長さを返す関数 -----
  public float GetVectorLength(float3 v)
  {
    float sqareLength = (v.x * v.x) + (v.y * v.y) + (v.z * v.z);
    float length;
    if (sqareLength > 0)
      length = math.sqrt(sqareLength);
    else
      length = 0.0f;

    return length;
  }

  // ----- 2次元ベクトルの長さを返す関数 -----
  public float GetVector2Length(float x, float y)
  {
    float sqareLength = (x * x) + (y * y);
    float length;
    if (sqareLength > 0)
      length = math.sqrt(sqareLength);
    else
      length = 0.0f;

    return length;
  }

// ----- ラジアンから度数への変換 -----
public float Rad2Deg(float rad) => (rad * 180.0f) / (float)math.PI;
}
