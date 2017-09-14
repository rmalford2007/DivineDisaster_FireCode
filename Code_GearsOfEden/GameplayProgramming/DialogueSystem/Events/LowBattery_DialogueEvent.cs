using UnityEngine;
using System.Collections;

public class LowBattery_DialogueEvent : DialogueEvent {
    public static new int eventID = 8; //Add your event ID when chosen to the DialogueEvent class, so others can see what is taken
    public static new string eventName = "Low Battery";//Name to show in dialogue editor, keep it short, or go resize the column for event names
    public float lowBatteryPercent = .2f; 
    private float currentLevelPercent = 1f;
    
    private bool postStart = false;

	// Use this for initialization
	void Start () {
        BatteryController.Instance.Broadcast_BatteryLevel += OnBatterLevelChanged;
        
        postStart = true;
    }
	
	// Update is called once per frame
	void Update () {

    }

    private void OnBatterLevelChanged(float batteryLevel, float chargeDeltaTime)
    {
        currentLevelPercent = batteryLevel;
    }

    private void OnEnable()
    {
        if (postStart)
        {
            BatteryController.Instance.Broadcast_BatteryLevel += OnBatterLevelChanged;
        }
    }

    private void OnDisable()
    {
        BatteryController.Instance.Broadcast_BatteryLevel -= OnBatterLevelChanged;
    }

    public override void DoUpdate()
    {
        if (updateReady)
        {
            //set updateReady to false, so when multiple primary speakers are in range, we only update 1 time
            updateReady = false;
            
            if (currentLevelPercent <= lowBatteryPercent)
            {
                //We are idle, lets broadcast and inform subscribers that we are idle until they use us
                OnBroadcastEvent(this);
            }
        }
    }

    //Call this to notify this class when we use it to show dialogue
    public override void ConsumeEvent()
    {
        
    }

    //When making new events, be sure to copy paste these functions in to hide the old ones
    #region REQUIRED_COPY_PASTED_FUNCTIONS
    

    //this is changed, copy a different function from another event
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
