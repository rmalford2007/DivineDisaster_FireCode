using UnityEngine;
using System.Collections;

public class SceneCameraController : CameraBase {

    [Tooltip("Drag and drop the main camera object here. This is the camera that follows the rover around.")]
    public Camera mainFollowCamera;

    [Tooltip("Drag and drop the scene camera object here. This is the camera that should be attached to the same game object as this script.")]
    private Camera sceneCamera;

    [Tooltip("The curve will control the movement speed of the camera for moving forward, backwards, strafe left, strafe right, up, and down based on duration of movement. \n\n" + 
        "Vertical axis is the movement speed.\n" + 
        "Horizontal axis is the elapsed time. The time it takes to get to the maximum speed.\n\n" + 
        "To change a key to an exact value right click the key and choose Edit Key.\n\n" + 
        "Scroll Wheel changes overall zoom.\n" + 
        "Shift + Scroll Wheel will zoom the vertical axis only.\n" + 
        "Ctrl + Scroll Wheel will zoom the horizontal axis only.")]
    public AnimationCurve moveSpeed;
    [Tooltip("The maximum values the player can zoom to using the scroll wheel.")]
    public Vector2 moveSpeedBounds = new Vector2(0.1f, 100.0f);

    [Tooltip("The curve will control the roll rotation speed of the camera. \n\n" +
        "Vertical axis is the roll speed.\n" +
        "Horizontal axis is the elapsed time. The time it takes to get to the maximum speed.\n\n" +
        "To change a key to an exact value right click the key and choose Edit Key.\n\n" +
        "Scroll Wheel changes overall zoom.\n" +
        "Shift + Scroll Wheel will zoom the vertical axis only.\n" +
        "Ctrl + Scroll Wheel will zoom the horizontal axis only.")]
    public AnimationCurve rollSpeed;
    [Tooltip("The maximum values the player can zoom to using the scroll wheel.")]
    public Vector2 rollSpeedBounds = new Vector2(0.1f, 200.0f); //roll speed should be about 3 to 4 times more than the move speed? 

    [Tooltip("The multiplier for adjusting movement speed and roll speed when holding down the boost key.")]
    public float boostMultiplier = 3.0f;

    [Tooltip("These are the layers the camera will collide with.")]
    public LayerMask collisionLayers;

    [Tooltip("The distance the camera can get to an obstacle before stopping. Note the distance starts at the collider box. As the number gets smaller you may go through objects if moving too fast.")]
    public float collisionDistance = 1.0f;

    private BoxCollider raycastOrigin;
     
    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private float roll = 0.0f;

    private float scrollAxis = 0.0f;

    private float elapsedMoveTime = 0.0f; //current duration of moving, since we started pressing a movement key. Should reset each time we stop moving.
    private float elapsedRollTime = 0.0f; //current duration of rolling, since we started pressing a roll key. Should reset each time we stop rolling.

    private Vector3 moveDirection = Vector3.zero;
    private bool rotationLock = false; //this is true when the user wants to lock their camera rotation so they can use the mouse and click UI buttons.
    private bool firstActivation = true; //this is true until the first time the scene camera is toggled to. This is to sync the scene camera to the follow camera at first activation.
    private Vector3[] verts;

    // Use this for initialization
    void Start () {
        if (sceneCamera == null)
            sceneCamera = gameObject.GetComponent<Camera>();
        
        InitBoxVertices();
    }

    //Store the corner vertices of the collider box for reuse later. NOTE this will not update with rotation of the collider, only position is updated later
    void InitBoxVertices()
    {
        raycastOrigin = GetComponent<BoxCollider>();
        if (raycastOrigin != null)
        {
            verts = new Vector3[8];        // Array that will contain the collider vertices
            
            verts[0] = raycastOrigin.center + new Vector3(raycastOrigin.size.x, -raycastOrigin.size.y, raycastOrigin.size.z) * 0.5f;
            verts[1] = raycastOrigin.center + new Vector3(-raycastOrigin.size.x, -raycastOrigin.size.y, raycastOrigin.size.z) * 0.5f;
            verts[2] = raycastOrigin.center + new Vector3(-raycastOrigin.size.x, -raycastOrigin.size.y, -raycastOrigin.size.z) * 0.5f;
            verts[3] = raycastOrigin.center + new Vector3(raycastOrigin.size.x, -raycastOrigin.size.y, -raycastOrigin.size.z) * 0.5f;
            verts[4] = raycastOrigin.center + new Vector3(raycastOrigin.size.x, raycastOrigin.size.y, raycastOrigin.size.z) * 0.5f;
            verts[5] = raycastOrigin.center + new Vector3(-raycastOrigin.size.x, raycastOrigin.size.y, raycastOrigin.size.z) * 0.5f;
            verts[6] = raycastOrigin.center + new Vector3(-raycastOrigin.size.x, raycastOrigin.size.y, -raycastOrigin.size.z) * 0.5f;
            verts[7] = raycastOrigin.center + new Vector3(raycastOrigin.size.x, raycastOrigin.size.y, -raycastOrigin.size.z) * 0.5f;
        }
    }
	
	// Update is called once per frame
	void Update () {
        if (InputManager.isRoverControlEnabled)
        {
            if (sceneCamera.enabled && firstActivation)
            {
                firstActivation = false;
                sceneCamera.gameObject.transform.position = mainFollowCamera.gameObject.transform.position;
                sceneCamera.gameObject.transform.rotation = mainFollowCamera.gameObject.transform.rotation;
            }
            
            scrollAxis = Input.GetAxis("Change Camera Speed");

            //Listen for control keys
            if (Input.GetButtonDown("Reset Scene Camera"))
            {
                //Reset sceneCamera to mainCamera position / orientation
                sceneCamera.gameObject.transform.position = mainFollowCamera.gameObject.transform.position;
                sceneCamera.gameObject.transform.rotation = mainFollowCamera.gameObject.transform.rotation;
                yaw = 0;
                pitch = 0;
            }

            if (scrollAxis != 0.0f)
            {
                if (Input.GetButton("Roll Modifier"))
                {
                    //adjust maximum roll speed
                    Keyframe modifyKey = rollSpeed.keys[rollSpeed.keys.Length - 1];
                    modifyKey.value = Mathf.Clamp(modifyKey.value + (scrollAxis * 3), rollSpeedBounds.x, rollSpeedBounds.y);
                    rollSpeed.MoveKey(rollSpeed.keys.Length - 1, modifyKey);
                }
                else
                {
                    //adjust maximum movement speed
                    Keyframe modifyKey = moveSpeed.keys[moveSpeed.keys.Length - 1];
                    modifyKey.value = Mathf.Clamp(modifyKey.value + scrollAxis, moveSpeedBounds.x, moveSpeedBounds.y);
                    moveSpeed.MoveKey(moveSpeed.keys.Length - 1, modifyKey);
                }
            }
            
            rotationLock = Cursor.visible;

            //Look Rotations
            if (!rotationLock)
            {
                RotateCamera_MouseYawPitch();
            }

            if (Input.GetButton("Roll Modifier") && Input.GetButton("Elevate or Roll Camera")) //Left Click or Right Click
                RotateCamera_Roll((Input.GetAxis("Elevate or Roll Camera") < 0.0f), Input.GetButton("Boost Camera"));
            else
                elapsedRollTime = 0.0f;

            MoveCamera();
            
        }
	}

    void MoveCamera()
    {
        //Move Translations
        moveDirection = Vector3.zero;
        
        if (Input.GetButton("Vertical Camera"))
        {
            //move forward or backward
            moveDirection += Input.GetAxis("Vertical Camera") > 0.0f ? transform.forward : -transform.forward;
        }
        if (Input.GetButton("Horizontal Camera"))
        {
            //strafe right or left
            moveDirection += Input.GetAxis("Horizontal Camera") > 0.0f ? transform.right : -transform.right;
        }
        if (!rotationLock && !Input.GetButton("Roll Modifier") && Input.GetButton("Elevate or Roll Camera")) //Left Click or Right Click
        {
            //Move camera up or down
            moveDirection += Input.GetAxis("Elevate or Roll Camera") > 0.0f ? transform.up : -transform.up;
        }

        //If we are moving, normalize the direction and adjust it for frame time and movespeed
        if (moveDirection != Vector3.zero)
        {
            elapsedMoveTime += Time.deltaTime;
            Vector3 checkDirection = moveDirection.normalized * Time.deltaTime * GetMoveSpeedMultiplier(elapsedMoveTime, Input.GetButton("Boost Camera"));
            if (checkDirection != Vector3.zero && !CheckDirectionCollisions(checkDirection))
                transform.position += checkDirection;
        }
        else
            elapsedMoveTime = 0.0f;
    }

    //Check for collision with obstacles using raycasting
    bool CheckDirectionCollisions(Vector3 checkDirection)
    {
        //Cast the ray checkDirection from each corner of the cube, scale the raycast with the speed we are moving
        //   NOTE: Rotation of cube is not updated, only original orientation is considered, additional programming will need to be done if you want the points to rotate as the camera rotates. Didn't seem needed at the time.
        for(int i = 0; i < 8; i++)
        {
            //Debug.DrawRay(transform.position + verts[i], checkDirection.normalized * (checkDirection.magnitude + collisionDistance), Color.red);
            if (Physics.Raycast(transform.position + verts[i], checkDirection.normalized, checkDirection.magnitude + collisionDistance, collisionLayers))
            {
                elapsedMoveTime = 0.0f; //Collision occured, reset acceleration time to 0
                return true;
            }
        }

        return false;
    }

    //Move Speed is determined by amount of time we are holding down a movement key. 
    //    Uses the moveSpeed animation curve. 
    //    Set the curve values to adjust maximum speed / animation time.
    float GetMoveSpeedMultiplier(float elapsedTime, bool boosted)
    {
        return moveSpeed.Evaluate(elapsedTime) * (boosted ? boostMultiplier : 1.0f);
    }

    //Roll Speed is determined by amount of time we are holding down a roll command key. 
    //    Uses the rollSpeed animation curve. 
    //    Set the curve values to adjust maximum speed / animation time.
    float GetRollSpeedMultiplier(float elapsedTime, bool boosted)
    {
        return rollSpeed.Evaluate(elapsedTime) * (boosted ? boostMultiplier : 1.0f);
    }

    void RotateCamera_MouseYawPitch()
    {
        yaw = Input.GetAxis("Yaw Camera");
        pitch = Input.GetAxis("Pitch Camera");
       
        //Rotate pitch and yaw
        transform.Rotate(pitch, 0f, 0f, Space.Self);
        transform.Rotate(0f, yaw, 0f, Space.Self);
    }

    void RotateCamera_Roll(bool reverseDirection, bool increasedSpeed)
    {
        elapsedRollTime += Time.deltaTime;
        roll = (reverseDirection ? -1.0f : 1.0f) * GetRollSpeedMultiplier(elapsedRollTime, increasedSpeed) * Time.deltaTime;

        //Rotate roll
        transform.Rotate(0f, 0f, roll, Space.Self);
    }
}
