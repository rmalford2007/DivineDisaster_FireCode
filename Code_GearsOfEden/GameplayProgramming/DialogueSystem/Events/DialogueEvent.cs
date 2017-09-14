using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public delegate void DialogueEventHandler(DialogueEvent e);

public abstract class DialogueEvent : MonoBehaviour {

    public static int eventID = -1;
    /********************************
     * 1 - Idle Event
     * 2 - Depart Event
     * 3 - Arrive Event
     * 4 - Scene Start Event
     * 5 - Intro Event
     * 6 - Rover Idle Event
     * 7 - Drilling Event
     * 8 - Low Battery Event
     * 9 - Driving Event
     * 10 - Arrive Close Event
     * 11 - Arrive Far Event
     * ******************************/
    public enum EventID_Int
    {
        SELECT_AN_EVENT = -1,
        IDLE = 1,
        DEPART = 2,
        ARRIVE = 3, 
        SCENE_START = 4,
        INTRO = 5,
        ROVER_IDLE = 6,
        DRILLING = 7,
        LOW_BATTERY = 8,
        DRIVING = 9,
        ARRIVE_CLOSE = 10,
        ARRIVE_FAR = 11,
    }
    
    public static string eventName = "Base";//Name to show in dialogue editor, keep it short, or go resize the column for event names
    public event DialogueEventHandler BroadcastEvent;
    public event DialogueEventHandler BroadcastEventDestroyed;
    public float enableRange = 10f;
    internal bool updateReady = true;
    public bool requireLOS = true;
    internal bool isCulled = true;
    public bool useOcclusion = true;
    internal int activatorID = -1;
    internal float lastBroadcastTime = 0f;
    public LayerMask hitLayersMask = (int)LayerMaskValues.Default | (int)LayerMaskValues.Asteroid | (int)LayerMaskValues.ChargeStations | (int)LayerMaskValues.Camera;
    private List<Collider> colliderList;

    // Use this for initialization
    void Start () {
        
            
	}
	
	// Update is called once per frame
	void Update () {
	
	}

    //Require events update when manually called
    public abstract void DoUpdate();

    //Require events to have a consumeEvent to notify the class when the event is actually used
    public abstract void ConsumeEvent();

    public virtual void OnBroadcastEvent(DialogueEvent e)
    {
        if (BroadcastEvent != null)
        {
            lastBroadcastTime = Time.realtimeSinceStartup;
            //Debug.Log("Starting Broadcast");
            BroadcastEvent(e);
            //Debug.Log("Ending Broadcast");
        }
    }

    

    public virtual void OnBroadcastEventDestroyed(DialogueEvent e)
    {
        if(BroadcastEventDestroyed != null)
        {
            BroadcastEventDestroyed(e);
        }
    }

    public virtual float GetRangeTo(Transform other)
    {
        return Vector3.Distance(transform.position, other.position);
    }

    public virtual bool IsInRange(Transform other, int activatingActorID)
    {
        activatorID = activatingActorID;
        return (GetRangeTo(other) <= enableRange);
    }

    void LateUpdate()
    {
        //Reset our ready flag for next frame
        updateReady = true;
        isCulled = true;
    }

    private bool IsOwnCollider(Collider checkCollider)
    {
        if (colliderList == null)
            colliderList = new List<Collider>(transform.GetComponentsInChildren<Collider>(true));
        for(int i = 0; i < colliderList.Count; i++)
        {
            if (colliderList[i] == checkCollider)
                return true;
        }
        return false;
    }

    public virtual bool IsInLineOfSightOfMainCamera()
    {
        if (requireLOS)
        {
            Vector3 viewPoint = Camera.main.WorldToViewportPoint(transform.position);
            if (viewPoint.x > 0f && viewPoint.x < 1f && viewPoint.y > 0f && viewPoint.y < 1f && viewPoint.z > 0f)
            {
                //If cullCheck is true, then we care if the object is blocked by another object, behind a wall etc. 
                if (useOcclusion)
                {
                    float closestObject = Vector3.Distance(Camera.main.transform.position, transform.position);
                    float cameraColliderDistance = -1f;
                    Debug.DrawRay(transform.position, (Camera.main.transform.position - transform.position).normalized * closestObject, Color.red, 2f);
                    RaycastHit[] hitInfoList = Physics.RaycastAll(transform.position, (Camera.main.transform.position - transform.position).normalized, closestObject, hitLayersMask, QueryTriggerInteraction.Collide);
                    for(int i = 0; i < hitInfoList.Length; i++)
                    {
                        if(hitInfoList[i].collider.CompareTag("MainCamera"))
                        {
                            cameraColliderDistance = hitInfoList[i].distance;
                        }
                        else if(!hitInfoList[i].collider.isTrigger)
                        {
                            if (!IsOwnCollider(hitInfoList[i].collider))
                            {
                                //if this isn't a trigger, then set the closest object
                                if (hitInfoList[i].distance < cameraColliderDistance)
                                    return false;
                                else if (closestObject > hitInfoList[i].distance)
                                    closestObject = hitInfoList[i].distance;
                            }
                        }
                        
                    }
                    if (closestObject < cameraColliderDistance)
                        return false;
                    //Check if blocked
                    //if(GetComponent<Renderer>().isVisible)
                    //{
                    //    return true;
                    //}
                    //else
                    //{
                    //    //Blocked by another object
                    //    return false;
                    //}
                }

                //don't care if its blocked by another object
                return true;
            }
            return false;
        }
        //if we don't require line of sight, always return true
        return true;
    }

    public float GetLastBroadcastTime()
    {
        return lastBroadcastTime;
    }

    //When making new events, be sure to copy paste these functions in to hide the old ones
    #region REQUIRED_COPY_PASTED_FUNCTIONS

    private void OnDestroy()
    {
        OnBroadcastEventDestroyed(this);
    }

    //Get this event name, without a class reference, used in the editor
    //  Note when copy pasting from this base class remove the comment block around the "new" keyword, as hiding is required for derived classes
    public static /** new **/ string GetEventName_Static()
    {
        return eventName;
    }

    //Get this classes eventID and return it, each event should be hiding the base class eventID (through new hiding)
    //  Note when copy pasting from this base class remove the comment block around the "new" keyword, as hiding is required for derived classes
    public static /** new **/ int GetEventID_Static()
    {
        return eventID;
    }

    //Used to get the event name to display in combo boxes, without a class reference, and the game not running
    //  Note when copy pasting from this base class remove the comment block around the "new" keyword, as hiding is required for derived classes
    public static /** new **/ string GetComboBoxName_Static()
    {
        return eventID + " - " + eventName;
    }

    //Used to get access to the lowest non hidden eventID using a class reference
    //   Note when copied from the base class, you need to replace virtual with override
    public virtual int GetEventID()
    {
        return eventID;
    }

    #endregion
}
