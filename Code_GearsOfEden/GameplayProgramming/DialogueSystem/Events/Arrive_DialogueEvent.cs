using UnityEngine;
using System.Collections;

public class Arrive_DialogueEvent : DialogueEvent {
    public static new int eventID = 3;//Add your event ID when chosen to the DialogueEvent class, so others can see what is taken
    public static new string eventName = "Arrive";//Name to show in dialogue editor, keep it short, or go resize the column for event names
    private bool canArrive = true;
    
	// Use this for initialization
	void Start () {
        canArrive = true;
	}
	
	// Update is called once per frame
	void Update () {
	
	}

    public override void DoUpdate()
    {
        if (updateReady)
        {
            //set updateReady to false, so when multiple primary speakers are in range, we only update 1 time
            updateReady = false;

            if (canArrive && IsInLineOfSightOfMainCamera())
            {
                //Set to false, so we only can arrive again, after departing see IsInRange()
                canArrive = false;
                //We are idle, lets broadcast and inform subscribers that we are idle until they use us
                //Debug.Log("Broadcast Arrive");
                OnBroadcastEvent(this);
            }
        }
    }


    //Call this to notify this class when we use it to show dialogue
    public override void ConsumeEvent()
    {
        canArrive = false;
        //consumedEvent = true;
    }

    public override bool IsInRange(Transform other, int activatingActorID)
    {
        if(GetRangeTo(other) <= enableRange)
        {
            activatorID = activatingActorID;
            return true;
        }
        else
        {
            //If not in range, reset the arrive flag
            canArrive = true;
            return false;
        }
    }

    //When making new events, be sure to copy paste these functions in to hide the old ones
    #region REQUIRED_COPY_PASTED_FUNCTIONS

    private void OnDestroy()
    {
        OnBroadcastEventDestroyed(this);
    }

    //Get this event name, without a class reference, used in the editor
    public static new string GetEventName_Static()
    {
        return eventName;
    }

    //Get this classes eventID and return it, each event should be hiding the base class eventID (through new hiding)
    public static new int GetEventID_Static()
    {
        return eventID;
    }

    //Used to get the event name to display in combo boxes, without a class reference, and the game not running
    public static new string GetComboBoxName_Static()
    {
        return eventName;
    }

    //Used to get access to the lowest non hidden eventID using a class reference
    public override int GetEventID()
    {
        return eventID;
    }

    #endregion
}
