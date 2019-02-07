using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class BoidMove : MonoBehaviour
{
  private int id;
  public int Id { set => id = value; }


  // Start is called before the first frame update
  void Start()
  { 

  }

  // Update is called once per frame
  void Update()
  {
    Boid();
  }

  /* ---------- Boid ----------
   * Boidアルゴリズムを用いて自身を移動させる関数
   */
  void Boid()
  {
    // --- Boid各要素の移動欲求 ---
    float3 cohesionDesire = float3.zero;
    float3 separationDesire = float3.zero;
    float3 alignmentDesire = float3.zero;

    float3 hungryDesire = float3.zero;

    Calc calc = new Calc();

    int visivbleBirdsNumber = 0;

    // --- Managerの持つListを参照して他の個体の位置を把握，移動欲求を計算する ---
    for (int i = 0; i < InstanceBird.number; i++)
    {
      if (i != id)
      {
        float3 myPosition = InstanceBird.birds[id].position;
        float3 tempPosition = InstanceBird.birds[i].position;

        // --- 自分の位置と対象個体の差分ベクトルを求める ---
        float3 diffVector = tempPosition - myPosition;

        // --- 差分ベクトルの長さを求める ---
        float diffLength = calc.GetVectorLength(diffVector);

        if (diffLength < 18.0f)
        {
          //  --- それぞれの欲求に合算する ---
          cohesionDesire += diffVector;
          separationDesire += ((diffVector / diffLength) * (1 / diffLength) * -1);
          alignmentDesire += InstanceBird.birds[i].moveVector;
          visivbleBirdsNumber += 1;
        }

      }
    }
    // --- 餌に向かう ---
    hungryDesire = InstanceBird.birds[id].position;

    float kHungry = 0.0f / InstanceBird.number;
      hungryDesire *= kHungry;

    float3 moveDesire = hungryDesire;

    if (visivbleBirdsNumber > 0) {
      // --- 係数をかけてバランスを調整する ---
      float kCohesion = 3.0f / visivbleBirdsNumber;
      cohesionDesire *= kCohesion;

      float kSeparation = 4.5f / visivbleBirdsNumber;
      separationDesire *= kSeparation;

      float kAlignment = 5.0f / visivbleBirdsNumber;
      alignmentDesire *= kAlignment;

      // ----- 移動欲求を反映して移動する -----
      moveDesire += cohesionDesire + separationDesire + alignmentDesire;
    }

    // --- 基礎情報の計算 ---
    float3 myMoveVector = InstanceBird.birds[id].moveVector;
    float3 myMoveDesire = InstanceBird.birds[id].moveDesire;

    // 鳥の正面ベクトルのXZ平面における長さ
    float forwardLengthXZ = calc.GetVector2Length(myMoveVector.x, myMoveVector.z);
    // 鳥の正面ベクトルのYZ平面における長さ
    float forwardLengthYZ = calc.GetVector2Length(myMoveVector.y, myMoveVector.z);

    // 鳥の移動欲求のXZ平面における長さ
    float moveDesireLengthXZ = calc.GetVector2Length(moveDesire.x, moveDesire.z);
    // 鳥の移動欲求のYZ平面における長さ
    float moveDesireLengthYZ = calc.GetVector2Length(moveDesire.y, moveDesire.z);

    // --- Yawを計算 ---
    float yawF = math.atan2(myMoveVector.x, myMoveVector.z);
    float yawM = math.atan2(moveDesire.x, moveDesire.z);
    float yawMove = yawM - yawF;
    float kYaw = 0.02f;
    float yawDiff = yawMove * kYaw;
    float newYaw = yawF + yawDiff;
    
    // --- Pitchを計算 ---
    float pitchF = math.atan2(myMoveVector.y, forwardLengthXZ);
    float pitchM = math.atan2(moveDesire.y, moveDesireLengthXZ);
    float pitchMove = pitchM - pitchF;
    float kPitch = 0.01f;
    float pitchDiff = pitchMove * kPitch;
    float newPitch = pitchF + pitchDiff; 

    // --- 速度の調整 ---
    // 前のフレームの移動欲求との差分から推進力を求める
    float maxSpeed = 1.0f;

    float kThrust = -1.0f * Time.deltaTime;
    float preDesireLengthXZ = calc.GetVector2Length(myMoveDesire.x, myMoveDesire.z);
    float nowVelocityXZ = preDesireLengthXZ * math.cos(yawMove);
    float desireVelocityXZ = moveDesireLengthXZ * math.cos(yawMove);
    float thrustXZ = desireVelocityXZ - nowVelocityXZ;
    forwardLengthXZ += thrustXZ * kThrust;
    
    float preDesireLengthYZ = calc.GetVector2Length(myMoveDesire.y, myMoveDesire.z);
    float nowVelocityYZ = preDesireLengthYZ * math.sin(pitchMove);
    float desireVelocityYZ = moveDesireLengthYZ * math.sin(pitchMove);
    float thrustYZ = desireVelocityYZ - nowVelocityYZ;
    forwardLengthYZ += thrustYZ * kThrust;

    if (maxSpeed < forwardLengthXZ)
      forwardLengthXZ = maxSpeed;
    if (maxSpeed < forwardLengthYZ)
      forwardLengthYZ = maxSpeed;

    // forwardLengthXZ = maxSpeed;
    // forwardLengthYZ = maxSpeed;

    // --- 計算した角度を元にベクトルを再構築 ---
    float3 moveDirection;
    moveDirection.x = math.sin(newYaw) * forwardLengthXZ;
    moveDirection.z = math.cos(newYaw) * forwardLengthXZ;
    moveDirection.y = math.sin(newPitch) * forwardLengthYZ;

    // --- 進行方向を向かせる ---
    float rotX = math.atan2(-moveDirection.y, calc.GetVector2Length(moveDirection.x, moveDirection.z));
    float rotY  = math.atan2(moveDirection.x, moveDirection.z);
    float rotZ = 0.0f; // yawMove * -0.1f;

    // --- ラジアンからの変換 ---
    rotX = calc.Rad2Deg(rotX);
    rotY = calc.Rad2Deg(rotY);
    rotZ = calc.Rad2Deg(rotZ);

    InstanceBird.birds[id].rotation = Quaternion.Euler(rotX, rotY, rotZ);
    InstanceBird.birds[id].position += moveDirection;
    InstanceBird.birds[id].moveVector = moveDirection;
    InstanceBird.birds[id].moveDesire = moveDesire;

    transform.position = InstanceBird.birds[id].position;
    transform.rotation = InstanceBird.birds[id].rotation;
  }


}
