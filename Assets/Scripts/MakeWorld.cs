using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Jobs;

[RequireComponent(typeof(Camera))]
public class MakeWorld : MonoBehaviour
{

	[SerializeField]
	private int field_size;

	// Use this for initialization
	void Start()
	{
		SetCameraPosition();

		InitWorld();

		CreateCubeforECS();
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

	/* ---------- SetCameraPosition ----------
	 * カメラの位置をいい感じにする関数
	 */
	 void SetCameraPosition()
	 {
		 float half_length = (float)field_size * 0.5f;
		 this.transform.position = new Vector3(half_length, half_length, (float)field_size * -1);
	 }

	/* ---------- InitWorld ----------
	 * EntityのWorldを生成する関数
	 */  
	 void InitWorld()
	{
		_world = new World("MyWorld");
		_world.CreateManager(typeof(EntityManager));
		_world.CreateManager(typeof(EndFrameTransformSystem));
		_world.CreateManager(typeof(EndFrameBarrier));
		_world.CreateManager<MeshInstanceRendererSystem>().ActiveCamera = GetComponent<Camera>();
		_world.CreateManager(typeof(RenderingSystemBootstrap));

		_world.CreateManager(typeof(WavedCube));// 自作のComponentを追加する

		ScriptBehaviourUpdateOrder.UpdatePlayerLoop(_world);
	}

	/* ---------- CreateCube ----------
	 * CubeのTransform値を設定する関数
	 */
	 void CreateCube()
	 {
		 var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
		 cube.transform.position = Vector3.zero;
		 cube.transform.rotation = Quaternion.identity;
		 cube.transform.localScale = Vector3.one;
	 }

	 /* ---------- CreateCubeforECS ---------
	  * ECSの枠組みでCubeを表示する関数
	  */
		void CreateCubeforECS()
		{
			var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			cube.transform.position = Vector3.zero;
			cube.transform.rotation = Quaternion.identity;
			cube.transform.localScale = Vector3.one;

			var manager = _world?.GetExistingManager<EntityManager>();
			if (null != manager)
			{
				var archetype = manager.CreateArchetype(ComponentType.Create<Prefab>(), ComponentType.Create<Position>(),ComponentType.Create<MeshInstanceRenderer>());

				var prefabEntity = manager.CreateEntity(archetype);


				manager.SetComponentData(prefabEntity, new  Position() { Value =  float3.zero });

				manager.SetSharedComponentData(prefabEntity, new MeshInstanceRenderer()
				{
					mesh = cube.GetComponent<MeshFilter>().sharedMesh,
					material = cube.GetComponent<MeshRenderer>().sharedMaterial,
					subMesh = 0,
					castShadows = UnityEngine.Rendering.ShadowCastingMode.Off,
					receiveShadows = false
				});

				// ----- 上記のCubeをprefabとして複数インスタンスする -----
				int length = field_size;
				NativeArray<Entity> entities = new NativeArray<Entity>(length * length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
				try
				{
					manager.Instantiate(prefabEntity, entities);

					for (int x = 0; x < length; x++)
					{
						for (int z = 0; z < length; z++)
						{
							int index = x + (z * length);
							manager.SetComponentData(entities[index], new Position{ Value = new float3(x, 0, z) });
						}
					}
				}
				finally { entities.Dispose(); }

			}

			Destroy(cube);

		}

		private World _world;

}

/* --------------- WavedCube ---------------
 * ECSで出したCubeを動かすクラス
 */
public sealed class WavedCube : JobComponentSystem
{

protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var myCubeJob = new MyCubeJob
        {
            chunks = EntityManager.CreateArchetypeChunkArray(query, Allocator.TempJob),
            positionType = GetArchetypeChunkComponentType<Position>(false),
            time = Time.realtimeSinceStartup
        };
        return myCubeJob.Schedule(myCubeJob.chunks.Length, 16, inputDeps);
    }

    [BurstCompile(Accuracy.Med, Support.Relaxed)]
    struct MyCubeJob : IJobParallelFor
    {
        [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> chunks;
        public ArchetypeChunkComponentType<Position> positionType;
        public float time;

        public unsafe void Execute(int chunkIndex)
        {
            var chunk = chunks[chunkIndex];
            var positions = chunk.GetNativeArray(positionType);
            var positionPtr = (Position*)positions.GetUnsafePtr();
            for (int i = 0, chunkCount = chunk.Count; i < chunkCount; i++, positionPtr++)
                positionPtr->Value.y = math.sin(time + 0.2f * (positionPtr->Value.x + positionPtr->Value.z));
        }
    }

	private readonly EntityArchetypeQuery query = new EntityArchetypeQuery
	{
		Any = System.Array.Empty<ComponentType>(),
		None = System.Array.Empty<ComponentType>(),
		All = new ComponentType[] { ComponentType.Create<Position>(), ComponentType.Create<MeshInstanceRenderer>() }
	};

}
