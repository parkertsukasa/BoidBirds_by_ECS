using UnityEngine;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;

[RequireComponent(typeof(Camera))]
public class MakeBirdWorld_Backup: MonoBehaviour
{

  [SerializeField]
  private Mesh birdMesh;
  [SerializeField]
  private Material birdMaterial;

  // Start is called before the first frame update
  void Start()
  {
    InitWorld();

    SetBirdArcheType();

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

    ScriptBehaviourUpdateOrder.UpdatePlayerLoop(_world);
  }

  private EntityManager entityManager;
  private EntityArchetype birdArchetype;
  /* ---------- SetBirdArcheType----------
   * 鳥のECSデータを作成
   */
  void SetBirdArcheType()
  {

    entityManager = _world.GetExistingManager<EntityManager>();
    birdArchetype = entityManager.CreateArchetype(
      typeof(MeshInstanceRenderer),
      typeof(Position),
      typeof(Rotation)
    );
  }

  /* ---------- DrawBirdModel ----------
   * ECSの枠組みで鳥のモデルをひとつ描画する
   */
   void DrawBirdModel()
   {
     var bird = entityManager.CreateEntity(birdArchetype);

     entityManager.SetSharedComponentData(bird, new MeshInstanceRenderer
     {
      mesh = birdMesh,
      material = birdMaterial
     }); 

     entityManager.SetComponentData(bird, new Position
     {
       Value = new float3(0.0f, 0.0f, 0.0f)
     });

     entityManager.SetComponentData(bird, new Rotation
     {
       Value = Quaternion.identity
     });
   }

}
