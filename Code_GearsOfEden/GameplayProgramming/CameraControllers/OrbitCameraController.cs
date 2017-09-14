using UnityEngine;
using System.Collections;

public class OrbitCameraController : CameraBase
{
    [Tooltip("This should be the transform for the pivot axis this camera follows, the pivot axis transform should belong to the object with an OrbitPivot script. This pivot axis should be a mixture of the terrain normal and gravity vector. See the OrbitPivot script for more pivot details.")]
    public Transform pivotAxisTransform;
    [Tooltip("This should be the transform for gravity center object of the asteroid.")]
    public Transform gravityCenter;

    private float minFollowDistance = 2f; //NOTE: if you change this value, change the range value on followDistance below
    private float maxFollowDistance = 30f;//NOTE: if you change this value, change the range value on followDistance below

    [Tooltip("The distance the camera will attempt to remain from the pivot position.")]
    [Range(2f, 30f)]
    public float followDistance = 14.0f;

    [Tooltip("This angle describes the vertical angle that the camera follows at. This is not clamped, you can roll right over the top of the target, but the cameras up direction is tied to the up direction of the pivot.")]
    public float verticalAngle = 30.0f; //the angle from horizontal to the up vector, where the up vector is a mixture of the up of the rover, and the gravity vector
    [Tooltip("This angle describes the horizontal rotation about the pivot axis. When follow rotation mode is active, 180 degrees should be behind the rover.")]
    public float horizontalAngle = 180.0f; //the angle from the forward of the rover on the horizontal plane, forward and right vectors
    [Tooltip("This is the speed at which the camera rotates horizontally as you drag the mouse left and right.")]
    public float horizontalSensitivity = 1.0f;
    [Tooltip("This is the speed at which the camera rotates vertically as you drag the mouse up and down.")]
    public float verticalSensitivity = 1.0f;

    [Tooltip("This controls the speed of the camera movement with regards to distance from where the camera should be. Should simulate fast snapping to location if distance increases. You should start the value at 1.0 at distance (time) zero.")]
    public AnimationCurve distanceSnapCurve;

    [Tooltip("This multiplier is combined with the distanceSnapCurve value, to control the camera movement speed.")]
    public float moveSpeedMultiplier = 3.0f;
    [Tooltip("This multiplier is combined with the distanceSnapCurve value, to control the camera look speed. How fast the camera rotates around to look at the target.")]
    public float lookSpeedMultiplier = 7.0f;
    [Tooltip("This multiplier is used to control the camera roll speed. Specifically for adjusting the up vector of the camera as rotation changes.")]
    public float rollSpeedMultiplier = 3.0f;

    [Tooltip("This how close the camera distances need to be before going idle. This also applies to a few other variables for testing idleness.")]
    public float idleCheckDistance = .04f;
    [Tooltip("This is the space around the camera that we raycast to, for collision detection. This value prevents the camera from going halfway into objects. Think of this value as a radius of the circle around the camera.")]
    public float colliderDistance = 0.5f;

    [Tooltip("These are the layers that the collision detection raycast will attempt to hit.")]
    public LayerMask collisionLayers;
    [Tooltip("Controls the follow rotation state of the camera rig. This bool is synced to the OrbitPivot script we are following. Rotates the pivot axis to match the forward direction of the rover, while maintaining the up value of the terrain / gravity mix.")]
    public bool followRotation = false;
    [Tooltip("Set this to true, if you want to see debug raycasting in scene view of unity.")]
    public bool drawDebugLines = false;
    [Tooltip("Controls how fast the rotation to behind the rover occurs when the follow command, or camera reset is activated. ")]
    public float followActivationSpeed = 75.0f;

    //Last values help track the idle state of the camera.
    private float lastFollowDistance;
    private float lastVerticalAngle;
    private float lastHorizontalAngle;
    private Vector3 lastFollowTargetPosition;
    private Vector3 lastFollowTargetUp;
    private Vector3 lastFollowTargetForward;

    private bool isUpdating = false; //tracker bool for idle state of camera

    //Final values for camera is what the camera is always trying to set itself to. Smoothly over time it will reach these values, when not colliding.
    private Vector3 finalCameraPosition = Vector3.zero;
    private Vector3 finalCameraLook = Vector3.zero;
    private Vector3 finalCameraWorldUp = Vector3.zero;

    //These are the current values for the camera
    private Vector3 currentCameraLook = Vector3.zero;
    private Vector3 currentCameraWorldUp = Vector3.zero;

    private float cosVal, sinVal; //temp variable for updating camera position, save on garbage collection
    private Vector3 rotatedPoint; //temp variable for updating camera position, save on garbage collection

    private OrbitPivot snapScript;

    private Vector3 currentCameraPosition = Vector3.zero;
    private Vector3 collisionCameraPosition = Vector3.zero;

    private float verticalAngle_Delta = 0f; //When collision occurs, we change this delta value, to find a space where there is no collision, always moves back towards 0 when no collision is occuring
    private float horizontalAngle_Delta = 0f;//When collision occurs, we change this delta value, to find a space where there is no collision, always moves back towards 0 when no collision is occuring

    private bool recentCollision = false; //Tracks if there was a recent collision, this tells us when to move the angle delta values back towards 0
    private bool movingToFollow = false; //when follow command is pressed, or if camera reset is pressed. This is used to track the animation state of rotating to the 180 degree follow position

    private float scrollAxis = 0f; //store the follow distance scroll wheel float, save on garbage collection

    private enum CameraSides
    {
        CAM_CENTER,
        CAM_BOTTOM,
        CAM_TOP,
        CAM_RIGHT,
        CAM_LEFT
            //If you add more items in here, update cameraSidesEnumLength
    };
    
    private int cameraSidesEnumLength = 5;

    // Use this for initialization
    void Start()
    {
        lastFollowDistance = followDistance;
        lastVerticalAngle = verticalAngle;
        lastHorizontalAngle = horizontalAngle;
    }

    void Awake()
    {
        snapScript = pivotAxisTransform.gameObject.GetComponent<OrbitPivot>();
        if (snapScript != null)
        {
            snapScript.SetSnapToForward(followRotation);
            transform.position = snapScript.snapToTransform.position;
        }
        else
            transform.position = pivotAxisTransform.position;
        currentCameraPosition = transform.position;
        collisionCameraPosition = transform.position;
        finalCameraPosition = transform.position;
        lastFollowTargetPosition = finalCameraLook = currentCameraLook = pivotAxisTransform.position;
        lastFollowTargetUp = finalCameraWorldUp = currentCameraWorldUp = pivotAxisTransform.up;
        lastFollowTargetForward = pivotAxisTransform.forward;
    }

    void Update()
    {

        if (InputManager.isRoverControlEnabled)
        {
            scrollAxis = Input.GetAxis("Change Camera Speed");
            followDistance -= scrollAxis;
            followDistance = Mathf.Clamp(followDistance, minFollowDistance, maxFollowDistance);

            if (Input.GetButton("Drag Camera"))
            {
                UpdateRotationValues();

                //if we are currently smoothing to follow 180, stop, cause the user is already dragging the angle
                if (movingToFollow)
                    movingToFollow = false;
            }

            if(followRotation && Input.GetButtonDown("Reset Scene Camera"))
            {
                movingToFollow = true;
            }
            
            if(movingToFollow)
            {
                MoveToFollowRotation();
            }
        }
    }

    void FixedUpdate()
    {
        //Check if anything is changing, pivot is moving (follow target), camera is rotating / zooming by dragging
        if (CheckForChanges())
        {
            isUpdating = true;
            finalCameraPosition = GetCalculatedPosition(horizontalAngle, verticalAngle);
            finalCameraLook = pivotAxisTransform.position;
            finalCameraWorldUp = pivotAxisTransform.up;
        }

        if (isUpdating)
        {
            //Stuff is moving, lets lerp towards the next camera position, and check the raycasting for collisions
            currentCameraPosition = Vector3.Lerp(currentCameraPosition, finalCameraPosition, Time.fixedDeltaTime * distanceSnapCurve.Evaluate(Vector3.Distance(currentCameraPosition, finalCameraPosition)) * moveSpeedMultiplier);
            
            RaycastHit hitInfo;
            CameraSides collisionSide;

            //Collision check
            if (CollisionCheck_LineOfSight(currentCameraPosition, out hitInfo, Color.blue, out collisionSide))
            {
                //Some collision occured, hand info off to CameraCollisionMove for evaluation
                CameraCollisionMove(hitInfo);
                recentCollision = true;
            }
            else
            {
                //Not colliding with something, lerp towards where the camera should be
                //if we recently were colliding lerp from collided position to current camera position
                if (recentCollision)
                {
                    //If we were colliding and the camera was pushed out of position, slowly lerp back towards where the camera should be, where verticalAngle_Delta is zero
                    if (verticalAngle_Delta > 1f)
                        verticalAngle_Delta -= (Time.fixedDeltaTime * moveSpeedMultiplier * 4);
                    else if (verticalAngle_Delta < -1f)
                        verticalAngle_Delta += (Time.fixedDeltaTime * moveSpeedMultiplier * 4);

                    //Lerp the horizontal angle delta as well back to 0
                    if (horizontalAngle_Delta > 1f)
                        horizontalAngle_Delta -= (Time.fixedDeltaTime * moveSpeedMultiplier * 4);
                    else if (horizontalAngle_Delta < -1f)
                        horizontalAngle_Delta += (Time.fixedDeltaTime * moveSpeedMultiplier * 4);

                    collisionCameraPosition = GetCalculatedPosition(horizontalAngle + horizontalAngle_Delta, verticalAngle + verticalAngle_Delta);
                    transform.position = Vector3.Lerp(transform.position, collisionCameraPosition, Time.fixedDeltaTime * distanceSnapCurve.Evaluate(Vector3.Distance(transform.position, collisionCameraPosition)) * moveSpeedMultiplier);
                    if (verticalAngle_Delta >= -1f && verticalAngle_Delta <= 1f)
                    {
                        verticalAngle_Delta = 0f;
                    }
                    if (horizontalAngle_Delta >= -1f && horizontalAngle_Delta <= 1f)
                    {
                        horizontalAngle_Delta = 0f;
                    }
                    if (horizontalAngle_Delta == 0f && verticalAngle_Delta == 0f)
                        recentCollision = false;
                }
                else //Nothing special is happening, lets lerp towards current calculated position
                    transform.position = Vector3.Lerp(transform.position, currentCameraPosition, Time.fixedDeltaTime * distanceSnapCurve.Evaluate(Vector3.Distance(currentCameraPosition, finalCameraPosition)) * moveSpeedMultiplier);
            }

            //Rotate to the camera toward the object
            currentCameraLook = Vector3.Lerp(currentCameraLook, finalCameraLook, Time.fixedDeltaTime * lookSpeedMultiplier); //Smoothly look towards the target
            currentCameraWorldUp = Vector3.Lerp(currentCameraWorldUp, finalCameraWorldUp, Time.fixedDeltaTime * rollSpeedMultiplier); //smoothly roll the camera to the up vector
            transform.LookAt(currentCameraLook, currentCameraWorldUp);
        }

        //check if the camera / follow target is idle, maybe saves some calculations? or adds more?
        if (isUpdating && Vector3.Distance(finalCameraPosition, currentCameraPosition) < idleCheckDistance / 2.0f && Vector3.Distance(finalCameraLook, currentCameraLook) < idleCheckDistance / 2.0f && Vector3.Distance(finalCameraWorldUp, currentCameraWorldUp) < idleCheckDistance / 2.0f)
        {
            isUpdating = false;
        }
    }

    public void SetFollowMode(bool setVal)
    {
        followRotation = setVal;
        snapScript.SetSnapToForward(followRotation);

        if (followRotation)
        {
            //activated follow, lets set the bool to smooth rotate towards 180 degree follow
            movingToFollow = true;
        }
    }

    //Upon activating follow or resetting camera during follow mode, smoothly reset the horizontal angle to 180 degrees (behind the rover), do nothing with vertical?
    void MoveToFollowRotation()
    {
        //the angle could be from multiple full 360 rotations, lets reduce this to 0 - 360 range, negative or positive
        while(horizontalAngle > 360.0f)
        {
            horizontalAngle -= 360.0f;
        }
        while(horizontalAngle < -360.0f)
        {
            horizontalAngle += 360.0f;
        }

        //if we are already at 180, lets stop and return
        if(horizontalAngle == 180f)
        {
            //we are done here, set bool then leave
            movingToFollow = false;
            return;
        }

        //Move closer to 180
        if(horizontalAngle > 180.0f)
        {
            horizontalAngle -= Time.deltaTime * followActivationSpeed;
            if(horizontalAngle < 180.0f)
            {
                //if we updated past 180, set to 180 and stop the animation
                horizontalAngle = 180.0f;
                movingToFollow = false;
            }
        }
        else
        {
            horizontalAngle += Time.deltaTime * followActivationSpeed;
            if (horizontalAngle > 180.0f)
            {
                horizontalAngle = 180.0f;
                movingToFollow = false;
            }
        }
    }

    //If the camera line of sight collisions are occuring, lets move the camera differently based on what is colliding.
    //   If the camera hits the terrain, lets float the camera upwards, so it can see the rover as it drives over a hill. (most common case)
    //   If the camera hits a structure (Default layer) zoom the camera in until no objects are between the camera and rover.
    void CameraCollisionMove(RaycastHit hitInfo)
    {
        if (hitInfo.collider.gameObject.layer == LayerMask.NameToLayer("Asteroid"))
        {
            Vector3 offsetDirection = Vector3.zero;

            //camera is colliding with terrain, then increase or decrease vertical angle, instead of sliding towards the collision point
            for (int i = 0; i < 100; i++)
            {
                collisionCameraPosition = GetCalculatedPosition(horizontalAngle + horizontalAngle_Delta, verticalAngle + verticalAngle_Delta);
                if (CollisionCheck_Terrain(collisionCameraPosition, out offsetDirection, Color.green))
                {
                    horizontalAngle_Delta += Time.fixedDeltaTime * lookSpeedMultiplier * -offsetDirection.x;


                    verticalAngle_Delta += Time.fixedDeltaTime * lookSpeedMultiplier * offsetDirection.y * ((verticalAngle > 90f && verticalAngle < 270f) ? -1f : 1f);
                }
                else
                {
                    collisionCameraPosition = GetCalculatedPosition(horizontalAngle + horizontalAngle_Delta, verticalAngle + verticalAngle_Delta); 
                    if (verticalAngle_Delta != 0f)
                        verticalAngle_Delta += Time.fixedDeltaTime * lookSpeedMultiplier * (verticalAngle_Delta > 0f ? -1.0f : 1.0f);
                    if (horizontalAngle_Delta != 0f)
                        horizontalAngle_Delta += Time.fixedDeltaTime * lookSpeedMultiplier * (horizontalAngle_Delta > 0f ? -1.0f : 1.0f);
                    
                    break; //break out of the for loop
                }
                if(i == 99)
                {
                    Debug.Log("Unable to move collision angle to satisfactory value. Weird stuff might happen? This occurs in tunnels, terrain collisions try to maintain the camera distance during collision. Turning camera side ways in tunnels causes this issue.");
                }
            }
        }
        else
        {
            verticalAngle_Delta = 0f;
            horizontalAngle_Delta = 0f;
            //Colliding with a structure, lerp the camera from its current position towards the collisionPosition
            collisionCameraPosition = GetCollisionOffset(currentCameraPosition);
        }

        //Lerp towards the collisionPosition
        transform.position = Vector3.Lerp(transform.position, collisionCameraPosition, Time.fixedDeltaTime * moveSpeedMultiplier);

    }

    //Pass a point to raycast to, and based on raycast collisions, provide an offset of the camera and return the modified position to place the camera. 
    //   The raycasts that collide closer than the center raycast distance, will influence the direction of the final camera position. 
    //     Example: if raycasts hit objects in the center and the bottom of the camera then the camera will be pushed forward, by center raycast, and then along the up vector by the bottom collision, to push the camera away from the floor or object
    Vector3 GetCollisionOffset(Vector3 collisionPoint)
    {
        Vector3 offset = collisionPoint;
        RaycastHit hitInfo;
        Vector3 cameraSidePosition = Vector3.zero;
        float centerDistance = Vector3.Distance(pivotAxisTransform.position, offset);
        for (int i = 0; i < cameraSidesEnumLength; i++)
        {
            CameraSides nextSide = (CameraSides)i;
            cameraSidePosition = GetCameraSidePosition(collisionPoint, nextSide);

            if (Physics.Raycast(pivotAxisTransform.position, cameraSidePosition - pivotAxisTransform.position, out hitInfo, Vector3.Distance(pivotAxisTransform.position, cameraSidePosition), collisionLayers))
            {

                if (drawDebugLines)
                    Debug.DrawRay(pivotAxisTransform.position, (cameraSidePosition - pivotAxisTransform.position).normalized * Vector3.Distance(pivotAxisTransform.position, cameraSidePosition), Color.red);
               
                switch (nextSide)
                {
                    case CameraSides.CAM_CENTER:
                        //if center collides update centerDistance 
                        //center collision pushs camera up to the collision point + colliderDistance
                        offset += transform.forward * (colliderDistance + Vector3.Distance(collisionPoint, hitInfo.point));
                        
                        centerDistance = hitInfo.distance;
                        break;
                    case CameraSides.CAM_TOP:
                        if(hitInfo.distance < centerDistance)
                        {
                            offset += -transform.up * colliderDistance; //Raycast collision is closer than the center collision point, push camera down
                        }
                        
                        break;
                    case CameraSides.CAM_RIGHT:
                        if (hitInfo.distance < centerDistance)
                        {
                            offset += -transform.right * colliderDistance;//Raycast collision is closer than the center collision point, push camera left
                        }
                        break;
                    case CameraSides.CAM_BOTTOM:
                        if (hitInfo.distance < centerDistance)
                        {
                            offset += transform.up * colliderDistance;//Raycast collision is closer than the center collision point, push camera up
                        }
                        
                        break;
                    case CameraSides.CAM_LEFT:
                        if (hitInfo.distance < centerDistance)
                        {
                            offset += transform.right * colliderDistance;//Raycast collision is closer than the center collision point, push camera right
                        }
                        
                        break;
                    default:
                        Debug.Log("Invalid camera side. Please use Center, Top, Right, Bottom, or Left.");
                        break;
                }
            }
            else
            {
                if (drawDebugLines)
                    Debug.DrawRay(pivotAxisTransform.position, (cameraSidePosition - pivotAxisTransform.position).normalized * Vector3.Distance(pivotAxisTransform.position, cameraSidePosition), Color.blue);
            }
        }
        return offset;
    }

    //User is holding down the mouse drag button, lets capture the mouse changes and update the orbital angles
    void UpdateRotationValues()
    {
        // Read the user input
        var x = Input.GetAxis("Mouse X");
        var y = Input.GetAxis("Mouse Y");

        horizontalAngle += x * horizontalSensitivity;
        verticalAngle += -y * verticalSensitivity;

        if (verticalAngle > 360f)
            verticalAngle -= 360f;
        if (verticalAngle < 0f)
            verticalAngle += 360f;

        if(verticalAngle_Delta != 0f)
        {
            //If the user is dragging the camera angle, and we are currently colliding with the terrain, verticalAngle_Delta is the
            //     offset where the camera is actually being drawn, snap the angle to where the collision is occuring so we can start moving the camera immediately
            verticalAngle += verticalAngle_Delta;
            verticalAngle_Delta = 0f;
        }
    }

    //Based on what rays are colliding with the terrain, return x,y direction vector to specify which direction we need to rotate the camera
    //Note: this returns direction based on world direction coordinates, not the local camera directions
    //      x value will move the camera's horizontalAngle_Delta positive or negative, to move the camera
    //      y value will move the camera's verticalAngle_Delta
    bool CollisionCheck_Terrain(Vector3 cameraPositionToCheck, out Vector3 offsetDirection, Color rayColor)
    {
        offsetDirection = Vector3.zero;
        //Raycast from the target we are following to the camera, if the ray hits anything, then we need to move the camera so it can see the target
        Vector3 cameraSidePosition = Vector3.zero;
        CameraSides collisionSide;
        RaycastHit hitInfo;
        float rayDistance = 0.0f;
        //Raycast to all edges of the camera, raycast to each edge contained in CameraSides enum.
        for (int i = 0; i < cameraSidesEnumLength; i++)
        {
            cameraSidePosition = GetCameraSidePosition(cameraPositionToCheck, (CameraSides)i);
            rayDistance = Vector3.Distance(pivotAxisTransform.position, cameraSidePosition);
            if (Physics.Raycast(pivotAxisTransform.position, cameraSidePosition - pivotAxisTransform.position, out hitInfo, rayDistance, collisionLayers))
            {
                if (drawDebugLines)
                    Debug.DrawRay(pivotAxisTransform.position, (cameraSidePosition - pivotAxisTransform.position).normalized * rayDistance, rayColor);
                //hit something
                collisionSide = (CameraSides)i;
                switch (collisionSide)
                {
                    case CameraSides.CAM_CENTER: //Do nothing with center raycast for now, eventually we may want to change terrain collision to zoom into center collision point distance
                        break;
                    case CameraSides.CAM_TOP:
                        offsetDirection += -Vector3.up * (rayDistance - hitInfo.distance); //push camera down
                        break;
                    case CameraSides.CAM_RIGHT:
                        offsetDirection += -Vector3.right * (rayDistance - hitInfo.distance);//push camera left
                        break;
                    case CameraSides.CAM_BOTTOM:
                        offsetDirection += Vector3.up * (rayDistance - hitInfo.distance);//push camera up
                        break;
                    case CameraSides.CAM_LEFT:
                        offsetDirection += Vector3.right * (rayDistance - hitInfo.distance);//push camera right
                        break;
                    default:
                        Debug.Log("Invalid camera side. Please use Center, Top, Right, Bottom, or Left.");
                        break;
                }
            }
        }

        if (offsetDirection == Vector3.zero)
            return false;
        else
        {
            offsetDirection = offsetDirection.normalized;
            return true;
        }
    }

    //This function returns true for the first raycast that collides with an object in collisionLayers. Raycasting from the target position to all edges of the camera stored in 
    //   the CameraSides enum. This returns both raycast hit info and the first side of the camera that is colliding
    bool CollisionCheck_LineOfSight(Vector3 cameraPositionToCheck, out RaycastHit hitInfo, Color rayColor, out CameraSides collisionSide)
    {
        //Raycast from the target we are following to the camera, if the ray hits anything, then we need to move the camera so it can see the target
        Vector3 cameraSidePosition = Vector3.zero;

        //Raycast to all edges of the camera, raycast to each edge contained in CameraSides enum. This should simulate a sorta of collision buffer around the camera?
        for (int i = 0; i < cameraSidesEnumLength; i++)
        {
            cameraSidePosition = GetCameraSidePosition(cameraPositionToCheck, (CameraSides)i);
            if (drawDebugLines)
                Debug.DrawRay(pivotAxisTransform.position, (cameraSidePosition - pivotAxisTransform.position).normalized * Vector3.Distance(pivotAxisTransform.position, cameraSidePosition), rayColor);
            if (Physics.Raycast(pivotAxisTransform.position, cameraSidePosition - pivotAxisTransform.position, out hitInfo, Vector3.Distance(pivotAxisTransform.position, cameraSidePosition), collisionLayers))
            {
                //hit something
                //Debug.Log("hit something: " + hitInfo.collider.gameObject.name);
                collisionSide = (CameraSides)i;
                return true;
            }
        }
        hitInfo = new RaycastHit();
        collisionSide = (CameraSides)0;
        return false;
    }

    //Check if something is changing to maybe save some time
    bool CheckForChanges()
    {
        bool retVal = false;

        if (lastFollowDistance != followDistance)
        {
            lastFollowDistance = followDistance;
            retVal = true;
        }
        if (lastVerticalAngle != verticalAngle)
        {
            lastVerticalAngle = verticalAngle;
            retVal = true;
        } 
        if (lastHorizontalAngle != horizontalAngle)
        {
            lastHorizontalAngle = horizontalAngle;
            retVal = true;
        }
        if (Vector3.Distance(lastFollowTargetPosition, pivotAxisTransform.position) > idleCheckDistance)
        {
            lastFollowTargetPosition = pivotAxisTransform.position;
            retVal = true;
        }
        if (Vector3.Distance(lastFollowTargetUp, pivotAxisTransform.up) > idleCheckDistance)
        {
            lastFollowTargetUp = pivotAxisTransform.up;
            retVal = true;
        }
        if (Vector3.Distance(lastFollowTargetForward, pivotAxisTransform.forward) > idleCheckDistance)
        {
            lastFollowTargetUp = pivotAxisTransform.forward;
            retVal = true;
        }
        return retVal;
    }

    //Based on the passed in horizontal angle, and vertical angle. Calculate a position rotated about the pivot axis transform vectors
    Vector3 GetCalculatedPosition(float horizontalTheta, float verticalTheta)
    {
        Vector3 nextCameraPos = pivotAxisTransform.position + (pivotAxisTransform.forward * followDistance);

        //Rotate the point on the vertical plane, about the follow targets left vector
        nextCameraPos = RotatePointAboutLine(nextCameraPos, pivotAxisTransform.position, -pivotAxisTransform.right, verticalTheta);
        //Rotate the point on the horizontal plane, about the pivotAxis
        return RotatePointAboutLine(nextCameraPos, pivotAxisTransform.position, pivotAxisTransform.up, horizontalTheta);
    }

    //The math for this function can be seen at http://inside.mines.edu/fs_home/gmurray/ArbitraryAxisRotation/
    //   Look at section 6 Rotation about an arbitrary line. The simplied version is at section 6.2
    // this is sorta similar to transform.RotateAround, except we pass in a position that rotates around an arbitrary axis instead of the transform 
    Vector3 RotatePointAboutLine(Vector3 point, Vector3 origin, Vector3 originDirection, float thetaRotation_Deg)
    {
        thetaRotation_Deg *= Mathf.Deg2Rad;

        originDirection = originDirection.normalized; //direction vector must be normalized, (u*u + v*v + w*w = 1) is true
       
        rotatedPoint = Vector3.zero;
        cosVal = Mathf.Cos(thetaRotation_Deg);
        sinVal = Mathf.Sin(thetaRotation_Deg);

        rotatedPoint.x = (origin.x * (originDirection.y * originDirection.y + originDirection.z * originDirection.z) - originDirection.x * (origin.y * originDirection.y + origin.z * originDirection.z - originDirection.x * point.x - originDirection.y * point.y - originDirection.z * point.z)) * (1 - cosVal) + point.x * cosVal + (-origin.z * originDirection.y + origin.y * originDirection.z - originDirection.z * point.y + originDirection.y * point.z) * sinVal;
        rotatedPoint.y = (origin.y * (originDirection.x * originDirection.x + originDirection.z * originDirection.z) - originDirection.y * (origin.x * originDirection.x + origin.z * originDirection.z - originDirection.x * point.x - originDirection.y * point.y - originDirection.z * point.z)) * (1 - cosVal) + point.y * cosVal + (origin.z * originDirection.x - origin.x * originDirection.z + originDirection.z * point.x - originDirection.x * point.z) * sinVal;
        rotatedPoint.z = (origin.z * (originDirection.x * originDirection.x + originDirection.y * originDirection.y) - originDirection.z * (origin.x * originDirection.x + origin.y * originDirection.y - originDirection.x * point.x - originDirection.y * point.y - originDirection.z * point.z)) * (1 - cosVal) + point.z * cosVal + (-origin.y * originDirection.x + origin.x * originDirection.y - originDirection.y * point.x + originDirection.x * point.y) * sinVal;

        //To make it a little faster, don't assign to another variable, just use the real variable its already stored in, this will be hard to read, the following may be easier to read?
        //x = point.x;
        //y = point.y;
        //z = point.z;

        //a = origin.x;
        //b = origin.y;
        //c = origin.z;

        //u = originDirection.x;
        //v = originDirection.y;
        //w = originDirection.z;

        //Debug.Log("(x,y,z):(" + x + "," + y + "," + z + ") \n(a,b,c):(" + a + "," + b + "," + c + ") \n(u,v,w):(" + u + "," + v + "," + w + ")\nTheta: " + thetaRotation_Deg*Mathf.Rad2Deg);

        //rotatedPoint.x = (a * (v * v + w * w) - u * (b * v + c * w - u * x - v * y - w * z)) * (1 - cosVal) + x * cosVal + (-c * v + b * w - w * y + v * z) * sinVal;
        //rotatedPoint.y = (b * (u * u + w * w) - v * (a * u + c * w - u * x - v * y - w * z)) * (1 - cosVal) + y * cosVal + (c * u - a * w + w * x - u * z) * sinVal;
        //rotatedPoint.z = (c * (u * u + v * v) - w * (a * u + b * v - u * x - v * y - w * z)) * (1 - cosVal) + z * cosVal + (-b * u + a * v - v * x + u * y) * sinVal;
        
        //Debug.Log("RotatedPoint: " + rotatedPoint.ToString());
        return rotatedPoint;
    }

    //Based on the passed in cameraPosition and side the user wants, return the offset position
    Vector3 GetCameraSidePosition(Vector3 camPosition, CameraSides whatSide)
    {
        switch(whatSide)
        {
            case CameraSides.CAM_CENTER:
                return camPosition;
            case CameraSides.CAM_TOP:
                return camPosition + transform.up * colliderDistance;
            case CameraSides.CAM_RIGHT:
                return camPosition + transform.right * colliderDistance;
            case CameraSides.CAM_BOTTOM:
                return camPosition + -transform.up * colliderDistance;
            case CameraSides.CAM_LEFT:
                return camPosition + -transform.right * colliderDistance;
            default:
                Debug.Log("Invalid camera side. Please use Center, Top, Right, Bottom, or Left.");
                break;
        }
        return camPosition;
    }

    public override void OnActivate()
    {
        base.OnActivate();
        //Each time we activate, lets swap the follow bool. This simulates two camera modes one for following and the other for not. This is sort of a hack until later, should probably just use two separate camera scripts for this
        SetFollowMode(!followRotation);
    }

    public override void OnDeactivate()
    {
        base.OnDeactivate();
    }

}
