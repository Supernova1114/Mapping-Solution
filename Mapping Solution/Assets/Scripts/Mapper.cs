using AStar.Options;
using AStar;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor.Search;

public class Mapper : MonoBehaviour
{
    [SerializeField] private LidarModule2D lidarModule;
    [SerializeField] private float clearMapInterval;
    [SerializeField] private int mapResolutionFactor = 10;
    [SerializeField] private Transform robotTransform;
    [SerializeField] private Transform robotBaseTransform;
    [SerializeField] private Transform targetPosition;
    [SerializeField] private float robotSpeed;
    [SerializeField] private float rotationSpeed;
    [SerializeField] private float plannerInterval;

    private Quaternion targetRotation;

    private float robotToTargetRotationDelta = 0;

    private Vector2 mapTargetPosCeil;

    public short[,] grid;
    public List<List<short>> map2D;
    private float[,] rangeList; // Row 0: angles, Row 1: ranges.

    private Vector2 mapCenter; // Center for n x n map

    private float lastMapClearTime = 0;

    private int maxRaySizeCeil;

    PathFinderOptions pathfinderOptions = new PathFinderOptions
    {
        PunishChangeDirection = false,
        UseDiagonals = true,
    };

    private Vector2 previousPositionXZ;

    List<short> mapRowTemp;

    Vector2[] points = new Vector2[200];

    private float lastPlanTime = 0;

    private int successfulPoints = 0;

    private bool needToReplan = true;

    private Vector3 subpointPos;
    private Vector3 targetVelocity;
    private bool recalculateSubpoint;

    // Start is called before the first frame update
    void Start()
    {
        //previousPositionXZ = new Vector2(robotTransform.position.x, robotTransform.position.z);

        maxRaySizeCeil = Mathf.CeilToInt(lidarModule.GetMaxRayDistance());

        mapCenter = mapResolutionFactor * maxRaySizeCeil * Vector2.one;

        int mapSize = (maxRaySizeCeil * mapResolutionFactor * 2) + 1; // +1 to add a center

        grid = new short[mapSize, mapSize];
        map2D = new List<List<short>>();


        // Assign all map spaces to open
        for (int i = 0; i < mapSize; i++)
        {
            map2D.Add(new List<short>());

            for (int j = 0; j < mapSize; j++)
            {
                map2D[i].Add(1); // Open cell
            }
        }


    }

    // Update is called once per frame
    void Update()
    {
        //Vector2 currentPositionXZ = new Vector2(robotTransform.position.x, robotTransform.position.z);

        //ShiftMap(currentPositionXZ);

        rangeList = lidarModule.GetRanges();

        for (int i = 0; i < rangeList.GetLength(1); i++)
        {
            float range = rangeList[1, i];
            float angle = (rangeList[0, i] + 270) % 360 * Mathf.Deg2Rad;

            if (range == Mathf.Infinity)
            {
                // Raytrace clear cells

                range = maxRaySizeCeil * mapResolutionFactor;

                RayTraceClearCells(angle, range);
            }
            else
            {
                // Add blocked cell (and extra padding)
                range *= mapResolutionFactor;

                RayTraceClearCells(angle, maxRaySizeCeil * mapResolutionFactor);

                int y = Mathf.CeilToInt(Mathf.Sin(angle) * range) + (int)mapCenter.y - 1;
                int x = Mathf.CeilToInt(Mathf.Cos(angle) * range) + (int)mapCenter.x - 1;

                for (int r = y - 5; r <= y + 5; r++)
                {
                    for (int c = x - 5; c <= x + 5; c++)
                    {
                        if (r < map2D.Count && r >= 0 && c < map2D[0].Count && c >= 0)
                        {
                            map2D[r][c] = 0; // Add Blocked Cell
                        }
                    }
                }
            }


        }


        // Check if there are more rays to one side

        


        //previousPositionXZ = currentPositionXZ; // TODO - Put this at end of update loop.



        // Debug map print output
        /*string output = "";

        for (int i = 0; i < map2D.GetLength(0); i++)
        {
            for (int j = 0; j < map2D.GetLength(1); j++)
            {
                output += map2D[i, j] + " ";
            }
            output += "\n";
        }*/

        //print(output);
        //Debug.Log(output);
        // Clear map
        //PlanPath1();
        PathPlan2();

        if (Time.time - lastMapClearTime > clearMapInterval)
        {

            for (int i = 0; i < map2D.Count; i++)
            {
                for (int j = 0; j < map2D[0].Count; j++)
                {
                    map2D[i][j] = 1; // Open Cell
                }
            }

            lastMapClearTime = Time.time;
        }
    }

    public Vector2 GetMapCenter()
    {
        return mapCenter;
    }

    public int GetMapResolutionFactor()
    {
        return mapResolutionFactor;
    }

    public Vector2 GetMapTargetPos()
    {
        return mapTargetPosCeil;
    }

    private void PathPlan2()
    {
        Vector2 currentPos = new Vector2(robotTransform.position.x, robotTransform.position.z);
        Vector2 targetPos = new Vector2(targetPosition.position.x, targetPosition.position.z);

        Vector2 mapTargetPos = ((targetPos - currentPos) * mapResolutionFactor) + new Vector2(mapCenter.x - 1, mapCenter.y - 1);
        mapTargetPos = new Vector2(Mathf.CeilToInt(mapTargetPos.x), Mathf.CeilToInt(mapTargetPos.y));

        //Note: Up is forward on map.

        // Draw circle a bit larger than robot. Traverse the graph using that circle.

        int currX = Mathf.CeilToInt(mapCenter.x);
        int currY = Mathf.CeilToInt(mapCenter.y);
        int robotRadiusMap = Mathf.CeilToInt(1.12f * mapResolutionFactor);


        if (needToReplan == true)
        {
            for (int step = 0; step < points.Length; step++)
            {
                if (CircleCollisionCheck(currX, currY - 1, robotRadiusMap) == false)
                {
                    currY--;

                    points[step].Set(currX, currY);
                    //PrintPath(currX, currY, 1);
                }
                else
                {
                    float rightMag = 0;
                    float leftMag = 0;

                    int rightCount = 0;
                    int leftCount = 0;

                    for (int i = 0; i < rangeList.GetLength(1); i++)
                    {
                        float range = rangeList[1, i];
                        float angle = (rangeList[0, i] + 270) % 360;

                        if (range != Mathf.Infinity)
                        {
                            if (angle >= 270 && angle < 360)
                            {
                                rightMag += range;
                                rightCount++;
                            }
                            else if (angle >= 180 && angle < 270)
                            {
                                leftMag += range;
                                leftCount++;
                            }
                        }
                    }

                    float leftAvg;
                    float rightAvg;

                    print("L: " + leftCount + " | R: " + rightCount);

                    leftAvg = leftCount == 0 ? 0 : leftMag / leftCount;
                    rightAvg = rightCount == 0 ? 0 : rightMag / rightCount;

                    int dir = leftAvg < rightAvg ? 1 : -1;

                    if (CircleCollisionCheck(currX + dir, currY, robotRadiusMap) == false)
                    {
                        currX += dir;

                        points[step].Set(currX, currY);
                        //PrintPath(currX, currY, 1);
                    }
                    else
                    {
                        currX -= dir;
                        currY += 1;
                    }
                }


            }
        }
        

        if ((targetPosition.position - robotTransform.position).magnitude > 1) // Check if at goal
        {
            Vector3 robotPosXZ = new Vector3(robotTransform.position.x, 0, robotTransform.position.z);

            if (recalculateSubpoint)
            {
                recalculateSubpoint = false;

                // Convert waypoint to worldspace, then relative to robot.

                Vector2 waypoint = points[5];
                waypoint.Set(waypoint.x, waypoint.y);

                Vector2 worldWaypoint = (waypoint - mapCenter) / mapResolutionFactor;
                Vector3 worldWaypointXZ = new Vector3(worldWaypoint.x, 0, worldWaypoint.y);


                Vector3 towardsGoal = targetPosition.position - robotPosXZ;
                Vector3 towardsGoalXZ = new Vector3(towardsGoal.x, 0, towardsGoal.z);

                Vector3 rotatedVector = Quaternion.LookRotation(-1 * towardsGoalXZ, Vector3.up) * worldWaypointXZ;
                Vector3 rotatedVectorXZ = new Vector3(rotatedVector.x, 0, rotatedVector.z);

                Vector3 reflectedVector = Vector3.Reflect(rotatedVectorXZ, towardsGoalXZ.normalized);
                // XZ this?

                subpointPos = -1 * (reflectedVector - robotPosXZ);
            }

            // Set target velocity and target rotation

            Vector3 targetVelocity = (subpointPos - robotPosXZ).normalized;

            targetRotation = Quaternion.LookRotation(targetVelocity, robotTransform.up);
            robotTransform.rotation = Quaternion.RotateTowards(robotTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            float angleDelta = Mathf.Abs(targetRotation.eulerAngles.y - robotTransform.eulerAngles.y);
            float distToSubpoint = (subpointPos - robotPosXZ).magnitude;

            if (distToSubpoint < 0.1f) // Made it to subpoint
            {
                recalculateSubpoint = true;
            }
            else if (angleDelta < 5)
            {
                robotTransform.position += targetVelocity * robotSpeed * Time.deltaTime;
            }

            Debug.DrawRay(subpointPos, Vector3.right * 0.5f, Color.green, 0.1f);
            Debug.DrawRay(robotTransform.position, targetVelocity * 2, Color.blue, 0.1f);
        }

    }

    private void PrintPath(int x, int y, int radius)
    {
        for (int r = y - radius; r <= y + radius; r++)
        {
            for (int c = x - radius; c <= x + radius; c++)
            {
                if (r < map2D.Count && r >= 0 && c < map2D[0].Count && c >= 0)
                {
                    map2D[r][c] = 2; // Add path cell for debug visual
                }
            }
        }
    }

    private bool CircleCollisionCheck(int mapX, int mapY, int mapRadius)
    {
        for (int angle = 0; angle < 360; angle++)
        {
            int x = Mathf.CeilToInt(mapRadius * Mathf.Cos(Mathf.Deg2Rad * angle)) + mapX;
            int y = Mathf.CeilToInt(mapRadius * Mathf.Sin(Mathf.Deg2Rad * angle)) + mapY;

            if (IsWithinMapBounds(x, y))
            {
                if (map2D[y][x] == 0) // blocked cell
                {
                    return true;
                }
                else
                {
                    //map2D[y][x] = 2; // Debug to show collision checks
                }
            }


        }

        return false;
    }

    private bool IsWithinMapBounds(int mapX, int mapY)
    {
        return mapX >= 0 && mapX < map2D[0].Count && mapY >= 0 && mapY < map2D.Count;
    }


    private void PlanPath1()
    {
        // Path plan using map

        Vector2 currentPos = new Vector2(robotTransform.position.x, robotTransform.position.z);
        Vector2 targetPos = new Vector2(targetPosition.position.x, targetPosition.position.z);

        //Debug.DrawRay(new Vector3(targetPos.x, 1, targetPos.y), Vector3.right * 0.1f, Color.green, 0.01f);

        Vector2 mapTargetPos = ((targetPos - currentPos) * mapResolutionFactor) + new Vector2(mapCenter.x - 1, mapCenter.y - 1);
        mapTargetPosCeil = new Vector2(Mathf.CeilToInt(mapTargetPos.x), Mathf.CeilToInt(mapTargetPos.y));

        //print("Current Pos: " + mapCenter + ", Target Pos:" + mapTargetPosCeil);

        // Convert List<List<short>> to short[,] 2D array.
        for (int i = 0; i < map2D.Count; i++)
        {
            for (int j = 0; j < map2D[0].Count; j++)
            {
                grid[i, j] = map2D[i][j];
            }
        }

        var worldGrid = new WorldGrid(grid);
        var pathfinder = new PathFinder(worldGrid, pathfinderOptions);

        Position[] path = pathfinder.FindPath(new Position((int)mapCenter.x, (int)mapCenter.y), new Position((int)mapTargetPosCeil.x, (int)mapTargetPosCeil.y));

        if (path.Length > 0)
        {
            // Convert path to worldspace
            Vector2[] worldPath = new Vector2[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                float y = (path[i].Row - mapCenter.y) / mapResolutionFactor;
                float x = (path[i].Column - mapCenter.x) / mapResolutionFactor;

                Vector2 temp = new Vector2(x, y) + new Vector2(currentPos.y, currentPos.x);
                worldPath[i] = new Vector2(temp.y, temp.x);

                // DEBUG
                Debug.DrawRay(new Vector3(worldPath[i].x, 1, worldPath[i].y), Vector3.right * 0.1f, Color.blue, 0.01f);
            }

            if (worldPath.Length >= 2)
            {
                Vector3 targetVelocity = (worldPath[1] - worldPath[0]).normalized;
                // Temp
                //robotTransform.position += new Vector3(targetVelocity.x, 0, targetVelocity.y) * robotSpeed * Time.deltaTime;
                robotTransform.position += new Vector3(targetVelocity.x, 0, targetVelocity.y) * robotSpeed * Time.deltaTime;
                targetRotation = Quaternion.LookRotation(new Vector3(targetVelocity.x, 0, targetVelocity.y), robotBaseTransform.up);
            }

            robotBaseTransform.rotation = Quaternion.Slerp(robotBaseTransform.rotation, targetRotation, 0.05f);


        }
    }

    private void ShiftMap(Vector2 currentPositionXZ)
    {
        // Shift map when robot moves.

        // Find position difference vector

        Vector2 positionDeltaXZ = currentPositionXZ - previousPositionXZ;

        // Convert delta to map-space

        Vector2 posDeltaXZMap = positionDeltaXZ * mapResolutionFactor;
        posDeltaXZMap = new Vector2(Mathf.CeilToInt(posDeltaXZMap.y), Mathf.CeilToInt(posDeltaXZMap.x));

        // Shift map according to delta.

        if (posDeltaXZMap.x > 0)
        {
            foreach (List<short> row in map2D)
            {
                row.RemoveRange(0, (int)posDeltaXZMap.x);
                for (int i = 0; i < (int)posDeltaXZMap.x; i++)
                {
                    row.Add(1); // Add clear cell
                }
            }
        }
        else if (posDeltaXZMap.x < 0)
        {
            int posDeltaX = (int)Mathf.Abs(posDeltaXZMap.x);

            print(posDeltaX);

            foreach (List<short> row in map2D)
            {
                row.RemoveRange(row.Count - posDeltaX, posDeltaX);
                for (int i = 0; i < posDeltaX; i++)
                {
                    row.Insert(0, 1); // Add clear cell
                }
            }
        }

        /*if (posDeltaXZMap.y > 0)
        {
            // Add to top, remove from bottom

            // Remove from bottom
            map2D.RemoveRange(map2D.Count - (int)posDeltaXZMap.y, (int)posDeltaXZMap.y);

            // Add to top
            for (int i = 0; i < (int)posDeltaXZMap.y; i++)
            {
                mapRowTemp = new List<short>(map2D.Count);

                for (int j = 0; j < map2D.Count; j++)
                {
                    mapRowTemp.Add(1); // Open cell
                }

                map2D.Insert(0, mapRowTemp);
            }
        }
        else if (posDeltaXZMap.y < 0)
        {
            int posDeltaY = (int)Mathf.Abs(posDeltaXZMap.y);

            // Remove from top, add from bottom

            // Remove from top
            map2D.RemoveRange(0, posDeltaY);

            // Add from bottom
            for (int i = 0; i < posDeltaY; i++)
            {

                mapRowTemp = new List<short>(map2D.Count);

                for (int j = 0; j < map2D.Count; j++)
                {
                    mapRowTemp.Add(1); // Open cell
                }

                map2D.Add(mapRowTemp);
            }
        }*/

    }

    private void RayTraceClearCells(float angle, float range)
    {
        float slope = Mathf.Tan(angle);
        //print(slope); 

        //float slope = (Mathf.Sin(angle) * range) / (Mathf.Cos(angle) * range);


        float linePercent = 0;

        while (linePercent < 1)
        {
            int sideSwitch;
            int bruh;

            if (angle >= 0 && angle < Mathf.PI / 2)
            {
                sideSwitch = -1;
                bruh = 1;
            }
            else if (angle >= Mathf.PI / 2 && angle < Mathf.PI)
            {
                sideSwitch = 1;
                bruh = 1;
            }
            else if (angle >= Mathf.PI && angle < Mathf.PI * 3 / 2)
            {
                sideSwitch = 1;
                bruh = -1;
            }
            else
            {
                sideSwitch = -1;
                bruh = -1;
            }

            // Create parametric equation of line
            int x = Mathf.CeilToInt(linePercent * range * -sideSwitch * 1) + (int)mapCenter.x - 1;
            int y = Mathf.CeilToInt(linePercent * Mathf.Abs(slope) * range * bruh * 1) + (int)mapCenter.y - 1;



            for (int r = y - 5; r <= y + 5; r++)
            {
                for (int c = x - 5; c <= x + 5; c++)
                {
                    if (r < map2D.Count && r >= 0 && c < map2D[0].Count && c >= 0)
                    {
                        map2D[r][c] = 1; // Clear cell
                    }
                }
            }

            linePercent += 0.05f;
        }
    }

}
