using System.Collections;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEngine;

public class RobotMovement : MonoBehaviour
{
    [SerializeField] private LidarModule2D module;
    [SerializeField] private Transform targetWaypoint;
    [SerializeField] private float safeDistanceThresh = 1.6f;
    [SerializeField] private float goToFreeSpaceDistThresh = 2.2f;
    [SerializeField] private float maxMoveSpeed = 1;
    [SerializeField] private float maxRotationSpeed = 50;
    [SerializeField] private float avoidanceMoveSpeed = 0.5f;
    [SerializeField] private float avoidanceRotationSpeed = 50;

    float[,] rangeList;

    private bool shouldAvoidObstacle = false;
    private bool shouldMoveTowardsFreeSpace = false;

    private float moveSpeed = 0;
    private float rotationSpeed = 0;

    void Update()
    {
        rangeList = module.GetRanges();

        Vector3 avoidanceVector = Vector3.zero;

        float totalAnglesInf = 0;
        int anglesCountInf = 0;

        shouldAvoidObstacle = false;
        shouldMoveTowardsFreeSpace = false;

        for (int i = 0; i < rangeList.GetLength(1); i++)
        {
            float range = rangeList[1, i];
            float angle = (rangeList[0, i] + 270) % 360;

            if ((angle >= 270 && angle < 360) || (angle >= 180 && angle < 270))
            {
                if (range != Mathf.Infinity)
                {
                    if (range < safeDistanceThresh)
                    {
                        shouldAvoidObstacle = true;

                        float radianAngle = angle * Mathf.Deg2Rad;
                        Vector3 direction = Quaternion.LookRotation(transform.forward, transform.up) * new Vector3(-Mathf.Cos(radianAngle), 0, -Mathf.Sin(radianAngle));
                        Vector3 laserDirection = Vector3.Reflect(direction, transform.right);

                        avoidanceVector += laserDirection;
                    }
                    else if (range < goToFreeSpaceDistThresh)
                    {
                        shouldMoveTowardsFreeSpace = true;
                    }
                }
                else
                {
                    totalAnglesInf += angle;
                    anglesCountInf++;
                }
            }
        } // for

        float anglesInfAvg = anglesCountInf == 0 ? 0 : totalAnglesInf / anglesCountInf;

        float radianAngle2 = anglesInfAvg * Mathf.Deg2Rad;
        Vector3 direction2 = Quaternion.LookRotation(transform.forward, transform.up) * new Vector3(-Mathf.Cos(radianAngle2), 0, -Mathf.Sin(radianAngle2));
        Vector3 laserDirection2 = Vector3.Reflect(direction2, transform.right);

        Vector3 towardsWaypoint = (targetWaypoint.position - transform.position);
        Vector3 towardsWaypointXZ = new Vector3(towardsWaypoint.x, 0, towardsWaypoint.z);

        Quaternion targetRotation;

        moveSpeed = maxMoveSpeed;
        rotationSpeed = maxRotationSpeed;

        if (shouldAvoidObstacle == true)
        {
            shouldAvoidObstacle = false;
            targetRotation = Quaternion.LookRotation(avoidanceVector * -1, transform.up);
            rotationSpeed = avoidanceRotationSpeed;
            moveSpeed = avoidanceMoveSpeed;
        }
        else if (shouldMoveTowardsFreeSpace == true)
        {
            shouldMoveTowardsFreeSpace = false;
            targetRotation = Quaternion.LookRotation(laserDirection2, transform.up);
        }
        else
        {
            targetRotation = Quaternion.LookRotation(towardsWaypointXZ, transform.up);
        }

        Debug.DrawRay(transform.position, targetRotation * Vector3.forward, Color.blue, 0.1f);

        Vector3 targetVelocity = transform.forward * moveSpeed;

        if (towardsWaypointXZ.magnitude > 1)
        {
            transform.position += targetVelocity * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    } // Update()
}
