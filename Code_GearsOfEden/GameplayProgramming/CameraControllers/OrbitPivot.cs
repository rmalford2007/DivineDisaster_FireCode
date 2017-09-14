using UnityEngine;
using System.Collections;

public class OrbitPivot : MonoBehaviour
{
    [Tooltip("This is the object we want the camera to follow. The pivot axis position will be synced with this transform position.")]
    public Transform snapToTransform;
    public Transform gravityCenter;

    [Tooltip("This holds up vector that we calculated. Setting this value in inspector will do nothing, will get recalculated.")]
    public Vector3 pivotUp;

    [Tooltip("The distance to the terrain. If the object isn't on the terrain, then we won't hit anything. How far to shoot the ray for getting the terrain normal.")]
    public float rayDistance = 5.0f;
    [Tooltip("These are the layers we want to collide with for raycasting to get the object normal to use. Terrain normal can be the terrain, rocks, structures, etc.")]
    public LayerMask terrainLayerMask;
    [Tooltip("Speed at which the up vector is smoothed when updating.")]
    public float smoothSpeed = 1.0f;
    [Tooltip("The terrain normal has to update more than this value to report the changes to the camera. When over this value, we begin to animate smooth to the new value.")]
    public float angleBuffer = 5.0f; //In degrees, the amount of up vector change, to slerp to the new up
    [Tooltip("This is the weight at which we want the gravity vector to influence the pivot axis, when mixed with the terrain. This should simulate going up and down hills depending on how the camera is sitting.")]
    public float gravityUpWeight = 1.2f;

    public bool followForward = false;
    private Vector3 targetUp;

    private float elapsedTime = 0.0f;
    private Vector3 fromUp;
    private Vector3 toUp;

    // Use this for initialization
    void Start()
    {
        pivotUp = targetUp = transform.up;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (snapToTransform != null)
            transform.position = snapToTransform.position;

        GetPivotUp();

        CheckBufferAngle();

        if (elapsedTime < 1.0f)
        {
            elapsedTime += Time.fixedDeltaTime;

            pivotUp = Vector3.Slerp(fromUp, toUp, elapsedTime * smoothSpeed);
        }
        Quaternion rotation = Quaternion.LookRotation(GetForward(), pivotUp);
        transform.rotation = rotation;
    }

    public void SetSnapToForward(bool val)
    {
        followForward = val;
    }

    //Sets the forward direction. This is needed to prevent floating around when setting the terrain normal. If follow mode is active, set the forward vector based on where the rover forward vector is pointing
    Vector3 GetForward()
    {
        Vector3 nextForward = Vector3.Cross(pivotUp, Vector3.Cross((followForward ? snapToTransform.forward : transform.forward), transform.up));
        //Debug.DrawRay(transform.position, nextForward.normalized * 9.0f, Color.red);
        return nextForward;
    }

    //This set the animation up to update the terrain up. 5 degrees is good I think. This prevent the camera from bobbing up and down as the rover drives over low poly terrain with hard edges.
    void CheckBufferAngle()
    {
        float theta = Mathf.Acos(Vector3.Dot(targetUp, pivotUp) / (targetUp.magnitude * pivotUp.magnitude));
        if (theta > angleBuffer * Mathf.Deg2Rad)
        {
            //over the buffer angle, lets setup the slerp
            fromUp = pivotUp;
            toUp = targetUp;
            elapsedTime = 0.0f;
        }
        else
            elapsedTime = 1.0f;
    }

    //This averages the gravity vector with the terrain normal to get the Pivot axis
    void GetPivotUp()
    {
        Vector3 rayDirection = (gravityCenter.position - transform.position).normalized;
        //hit the terrain, get the normal information from the hit info
        targetUp = RoverControl.Instance.GetTerrainNormal();

        //average the terrain normal with the gravity normal
        targetUp += -rayDirection.normalized * gravityUpWeight;
        targetUp = targetUp.normalized;

    }
}
