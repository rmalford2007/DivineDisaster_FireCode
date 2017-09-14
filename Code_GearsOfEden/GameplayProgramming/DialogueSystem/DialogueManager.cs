using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using System.Reflection;

public class HatTicket
{
    public int conversationIndex = -1;
    public int min;
    public int max;
    public HatTicket()
    {
        conversationIndex = -1;
        min = 0;
        max = 0;
    }

    public HatTicket(int _conversationIndex, int _min, int _max)
    {
        conversationIndex = _conversationIndex;
        min = _min;
        max = _max;
    }
}

[System.Serializable]
public class FrequencyConditions
{
    public DialogueEvent.EventID_Int eventEnum = DialogueEvent.EventID_Int.SELECT_AN_EVENT;
    public float cooldownTimer = 0f;
    private float remainingTime = 0f;

    public void Update(float deltaTime)
    {
        if (OnCooldown())
            remainingTime -= deltaTime;
    }

    public bool OnCooldown()
    {
        return remainingTime > 0f;
    }

    public void PutOnCooldown()
    {
        remainingTime = cooldownTimer;
    }
}

public class DialogueManager : MonoBehaviour {

    public static DialogueManager Instance;
    public SpeakerToken[] speakerList;
    public SortedList<int, SortedList<int, List<int>>> occuringEvents;
    public FrequencyConditions[] eventCooldownList;
    public float pollingTime = 0.75f;

    public string narrativeFileName = "narrativeData.json";
    public Transform dialogueParent;
    public DialogueDisplayInfo defaultDisplayInfo;
    public float startTransitionTime = 3f; //Wait for X seconds after scene is loaded to show start dialogue
    public float introTransitionTime = 3f;
    public float playTransitionTime = 3f; //Wait for X seconds after displaying game start to show play dialogue
    public bool useTimeAsSeed = true;
    public int seedInt = 0;
    public bool m_systemEnabled = true;

    private bool readyToShowDialogue = true;
    private string narrativeDataPath = "/StreamingAssets/";
    private string loadedNarrativePath;
    private Dictionary<int, FrequencyConditions> eventCooldownDictionary;

    private ConversationLibrary theLibrary;

    private Dictionary<string, List<ConversationDialogue>> dialogueLibrary; //List of all conversations groups, each group contains all the dialogues that are in the group
    private Dictionary<int, bool> eventBlocks; //true=blocked ... put blocks on certain events happening, to segment into game start, post game start, game play, and game end

    private float elapsedTime = 0f;

    private bool inStateTransition = false;

    private UnityEngine.Random.State dialogueRandomState;
    private UnityEngine.Random.State storeState;

    public enum BlockState
    {
        INIT,
        START, 
        INTRO, 
        PLAY
    }

    private BlockState currentBlockState = BlockState.INIT;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
            Destroy(this);
    }
    // Use this for initialization
    void Start() {
        storeState = UnityEngine.Random.state;

        if(useTimeAsSeed)
            UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
        else
            UnityEngine.Random.InitState(seedInt);

        dialogueRandomState = UnityEngine.Random.state;
        UnityEngine.Random.state = storeState;
        //If there isn't a DialogueManager in play, then set the Instance
        Type[] eventClassTypes = OptionsManager.GetAllDerivedClassesOf<DialogueEvent>();
        eventBlocks = new Dictionary<int, bool>();
        
        for (int i = 0; i < eventClassTypes.Length; i++)
        {
            MethodInfo getEventIDMethod = eventClassTypes[i].GetMethod("GetEventID_Static");
            if (getEventIDMethod != null)
            {
                eventBlocks.Add((int)getEventIDMethod.Invoke(this, null), true); //Start with all events blocked
            }
        }

        EvaluateState_Block(-1, 0f);

        speakerList = FindObjectsOfType(typeof(SpeakerToken)) as SpeakerToken[];
        occuringEvents = new SortedList<int, SortedList<int, List<int>>>();
        eventCooldownDictionary = new Dictionary<int, FrequencyConditions>();
        dialogueLibrary = new Dictionary<string, List<ConversationDialogue>>();
        
        if (File.Exists(Application.dataPath + narrativeDataPath + narrativeFileName))
            LoadNarrative(Application.dataPath + narrativeDataPath + narrativeFileName);
        else
        {
            theLibrary = null;
            Debug.Log("DialogueManager: Unable to load narrative json file > " + (Application.dataPath + narrativeDataPath + narrativeFileName) + "\nMake sure this narrative file exists. Disconnecting dialogue system...");
            m_systemEnabled = false;
            //BuildHardcodedConversationDialogues();
        }

        //Store the eventCooldownList setup in the inspector as a dictionary for faster access by eventID
        for (int i = 0; i < eventCooldownList.Length; i++)
        {
            if (!eventCooldownDictionary.ContainsKey((int)eventCooldownList[i].eventEnum))
            {
                eventCooldownDictionary.Add((int)eventCooldownList[i].eventEnum, eventCooldownList[i]);
            }
        }
    }

    // Update is called once per frame
    void Update() {
        elapsedTime += Time.deltaTime;
        if (m_systemEnabled && elapsedTime >= pollingTime)
        {
            ClearEvents();
            //Update our token list
            if (!inStateTransition)
            {
                speakerList = FindObjectsOfType(typeof(SpeakerToken)) as SpeakerToken[];
                for (int i = 0; i < speakerList.Length; i++)
                {
                    if (speakerList[i].GetType() == typeof(PrimarySpeaker))
                    {
                        speakerList[i].DoUpdate();
                    }
                }

                UpdateCooldownTimers(elapsedTime); //Update the cooldown timers by elapsedTime
                elapsedTime = 0f; //Reset the elapsedTime back to zero
                EvaluateReceivedEvents(); //Evaluate events, to see if we need to show a dialogue
            }
        }
    }
    
    IEnumerator StateTransitionTimer(float waitTime, bool resetFlag)
    {
        inStateTransition = true;
        
        yield return new WaitForSeconds(waitTime);
        
        if (resetFlag)
        {
            inStateTransition = false;
        }
    }
    private void StartScene_Block()
    {
        SceneStart_DialogueEvent.destroyFlag = false;
        Intro_DialogueEvent.destroyFlag = false;
        //Unblock SceneStart_DialogueEvent
        if (eventBlocks.ContainsKey(SceneStart_DialogueEvent.GetEventID_Static()))
        {
            eventBlocks[SceneStart_DialogueEvent.GetEventID_Static()] = false;
        }

        StartCoroutine(StateTransitionTimer(startTransitionTime, true));


        //unlocks player movement
        StartCoroutine(SetUserMovementBlock(0f, true)); 
       
    }

    private void Intro_Block(float displayTime)
    {
        //Mark Scene Start Events for destroy - they must have already shown, and we are moving to the next block - all start events components should get destroyed on next update
        SceneStart_DialogueEvent.destroyFlag = true;
        //Unblock Intro_DialogueEvent
        if (eventBlocks.ContainsKey(Intro_DialogueEvent.GetEventID_Static()))
        {
            eventBlocks[Intro_DialogueEvent.GetEventID_Static()] = false;
        }

        StartCoroutine(StateTransitionTimer(introTransitionTime + displayTime, true));
        //unlock player movement
        //StartCoroutine(SetUserMovementBlock(displayTime, true));
    }

    private void GamePlay_Block(float displayTime)
    {
        SceneStart_DialogueEvent.destroyFlag = true;
        Intro_DialogueEvent.destroyFlag = true;

        //Unblock all events for normal play
        if (eventBlocks != null)
        {
            //Can't modify a dictionary during iteration, copy to a list, to get keys out, loop through keys instead
            List<int> eventBlockCopyList = new List<int>(eventBlocks.Keys);
            for(int i = 0; i < eventBlockCopyList.Count; i++)
            {
                eventBlocks[eventBlockCopyList[i]] = false;
            }
        }
        StartCoroutine(StateTransitionTimer(playTransitionTime + displayTime, true));

        //StartCoroutine(SetUserMovementBlock(playTransitionTime + displayTime, true));
    }

    IEnumerator SetUserMovementBlock(float waitTime, bool setFlag)
    {
        yield return new WaitForSeconds(waitTime);
        InputManager.isRoverControlEnabled = setFlag;
    }

    private void LoadNarrative(string path)
    {
        if (File.Exists(path))
        {
            string dataAsJson = File.ReadAllText(path);
            theLibrary = JsonUtility.FromJson<ConversationLibrary>(dataAsJson);
            if (theLibrary != null)
            {
                //loop through and rebuild key
                for (int i = 0; i < theLibrary.theConversationList.Count; i++)
                {
                    //Rebuild key
                    theLibrary.theConversationList[i].RebuildKey();

                    //add conversation reference to dictionary based on key
                    if (dialogueLibrary.ContainsKey(theLibrary.theConversationList[i].dialogueKey))
                    {
                        dialogueLibrary[theLibrary.theConversationList[i].dialogueKey].Add(theLibrary.theConversationList[i]);
                    }
                    else
                    {
                        dialogueLibrary.Add(theLibrary.theConversationList[i].dialogueKey, new List<ConversationDialogue>());
                        dialogueLibrary[theLibrary.theConversationList[i].dialogueKey].Add(theLibrary.theConversationList[i]);
                    }
                }
                loadedNarrativePath = Application.dataPath + narrativeDataPath + narrativeFileName;
            }
            else
                loadedNarrativePath = "";
        }
    }

    public void OnExitScene()
    {
        if(loadedNarrativePath != "")
        {
            SaveNarrative(loadedNarrativePath);
        }
    }

    private void SaveNarrative(string path)
    {
        if (File.Exists(path))
        {
            string dataAsJson = JsonUtility.ToJson(theLibrary);
            File.WriteAllText(path, dataAsJson);
            Debug.Log("Saved narrative.");
        }
    }

    private void EvaluateState_Block(int usedEventID, float displayTime)
    {
        switch(currentBlockState)
        {
            case BlockState.INIT:
                Evaluate_InitBlock();
                break;
            case BlockState.START:
                Evaluate_StartBlock(usedEventID, displayTime);
                break;
            case BlockState.INTRO:
                Evaluate_IntroBlock(usedEventID, displayTime);
                break;
            case BlockState.PLAY:
                break;
        }
    }

    private void Evaluate_InitBlock()
    {
        currentBlockState = BlockState.START;
        StartScene_Block(); //Unblock the start scene event
    }

    private void Evaluate_StartBlock(int usedEventID, float displayTime)
    {
        if(usedEventID == SceneStart_DialogueEvent.GetEventID_Static())
        {
            //A scene start event is about to be displayed

            //if there are intro events, move to intro state, else move to play state
            
            //FindObjectsOfType<Intro_DialogueEvent>()
            //currentBlockState = BlockState.INTRO;
            //Intro_Block();

            currentBlockState = BlockState.INTRO;
            Intro_Block(displayTime);
        }
    }

    private void Evaluate_IntroBlock(int usedEventID, float displayTime)
    {
        if (usedEventID == Intro_DialogueEvent.GetEventID_Static())
        {
            //A intro event is about to be displayed

            currentBlockState = BlockState.PLAY;
            GamePlay_Block(displayTime);
        }
    }

    //We are using other seed states in the project, when we need to get a random, swap out the state to the dialogue state, and set it back when we are done, to preserve different seed states
    private int GetRandomRangeInt(int min, int max)
    {
        if (min == max)
            return min;
        SetRandomState(false);
        int returnVal = UnityEngine.Random.Range(min, max);
        SetRandomState(true);
        return returnVal;
    }

    //We are using other seed states in the project, when we need to get a random, swap out the state to the dialogue state, and set it back when we are done, to preserve different seed states
    private float GetRandomRangeFloat(float min, float max)
    {
        SetRandomState(false);
        float returnVal = UnityEngine.Random.Range(min, max);
        SetRandomState(true);
        
        return returnVal;
    }

    //We are using other seed states in the project, when we need to get a random, swap out the state to the dialogue state, and set it back when we are done, to preserve different seed states
    private void SetRandomState(bool resetFlag)
    {
        if (!resetFlag)
        {
            storeState = UnityEngine.Random.state;
            UnityEngine.Random.state = dialogueRandomState;
        }
        else
        {
            dialogueRandomState = UnityEngine.Random.state;
            UnityEngine.Random.state = storeState;
        }
    }

    private void EvaluateReceivedEvents()
    {
        if (readyToShowDialogue)
        {
            if (HasEvents())
            {
                //Debug.Log("Has events ===========");
                //Choose an event to use
                List<string> keyList;
                BuildCombinations(out keyList);
                
                //Get the dialogue information
                List<ConversationDialogue> choosableConversations = new List<ConversationDialogue>();
                for (int i = 0; i < keyList.Count; i++)
                {
                    //Debug.Log("Key: " + keyList[i]);
                    AcquireAvailableConversations(ref choosableConversations, keyList[i]);
                }
                //Have the list of keys, now get all dialogues that match these keys
                if (choosableConversations.Count > 0)
                {
                    //Debug.Log("Available conversations: " + choosableConversations.Count.ToString());
                    SortByPriorityID(ref choosableConversations, false);
                    int maxIndex = GetLastPriorityIndexOfFirst(choosableConversations);
                    
                    //Loop through and add tickets into the hat based on each available conversations weight, then draw from the hat
                    
                    int chosenID = ChooseADialogueTicket(choosableConversations, maxIndex);
                    if(chosenID == -1)
                    {
                        Debug.Log("DialogueManager: Chosen Conversation from the ticket hat is -1. Something is wrong.. investigate");
                        return;
                    }

                    //Check if the conversation we are choosing has cooldown conditions for its event, if so, put it on cooldown 
                    if (eventCooldownDictionary.ContainsKey(choosableConversations[chosenID].eventID))
                        eventCooldownDictionary[choosableConversations[chosenID].eventID].PutOnCooldown();

                    //Now that this event is on cooldown, lets check the display chance, and determine if we are actually going to show it
                    if (GetRandomRangeFloat(0f, 1f) < choosableConversations[chosenID].displayChance)
                    {
                        readyToShowDialogue = false;

                        StartCoroutine(DisplayConversation(choosableConversations[chosenID]));
                    }

                    EvaluateState_Block(choosableConversations[chosenID].eventID, choosableConversations[chosenID].displayTime);
                    
                }
            }
        }
    }

    private void PostDisplayChanges(ConversationDialogue theDisplayedDialogue)
    {
        //Increment display count, check if this conversation needs to move to another dictionary item
        theDisplayedDialogue.displayCount++;

        if (theDisplayedDialogue.exclusiveFlag)
        {
            EvaluateExclusiveGroupChanges(theDisplayedDialogue); //Get the group of exlusive items with same priority and move them to archive
        }
        else
            EvaluateChangesToConversation(theDisplayedDialogue); //Display count changed, evaluate the key and rebuild
    }

    private int ChooseADialogueTicket(List<ConversationDialogue> choosableDialogues, int maxPriorityIndex)
    {
        List<HatTicket> hatOfTickets = new List<HatTicket>();

        int trackIndex = 0;
        for(int i = 0; i <= maxPriorityIndex; i++)
        {
            hatOfTickets.Add(new HatTicket(i, trackIndex, trackIndex + choosableDialogues[i].displayWeight));
            trackIndex += choosableDialogues[i].displayWeight;
        }
        if (hatOfTickets.Count == 0)
            return -1;

        int randomInt = GetRandomRangeInt(0, trackIndex);

        for (int i = 0; i < hatOfTickets.Count; i++)
        {
            if(randomInt >= hatOfTickets[i].min && randomInt < hatOfTickets[i].max)
            {
                return hatOfTickets[i].conversationIndex;
            }
        }

        return -1;

        //Ticket method below would be slower if the designer decides to use very large weights, the above method should be cleaner and faster

        ////Loop from 0 to maxPriorityIndex and add index of the conversation into the list - tickets into the hat to draw from
        //List<int> hatOfTickets = new List<int>();
        //for (int i = 0; i <= maxPriorityIndex; i++) //max index contains the last index that has the same priority, so we need to add 1 to this, as max of Range() is exclusive
        //{
        //    for(int j = 0; j < choosableDialogues[i].displayWeight; j++)
        //        hatOfTickets.Add(i);
        //}
        //if (hatOfTickets.Count == 0)
        //    return -1;

        //return hatOfTickets[GetRandomRangeInt(0, hatOfTickets.Count)];
    }

    private void SortByPriorityID(ref List<ConversationDialogue> listToSort, bool lowestFirst)
    {
        if(lowestFirst)
            listToSort.Sort((s1, s2) => s1.priorityIndex.CompareTo(s2.priorityIndex));
        else
            listToSort.Sort((s1, s2) => (s1.priorityIndex.CompareTo(s2.priorityIndex))*-1);
    }

    private int GetLastPriorityIndexOfFirst(List<ConversationDialogue> sortedList)
    {
        //return the last index that holds the same priority value as the first element
        if(sortedList.Count > 0)
        {
            int firstPriorityValue = sortedList[0].priorityIndex;
            int lastIndex = 0;
            //Loop through until priority changes
            for(int i = 0; i < sortedList.Count; i++)
            {
                if (firstPriorityValue != sortedList[i].priorityIndex)
                    break;
                else
                    lastIndex = i;
            }
            return lastIndex;
        }
        return -1;
    }

    private void EvaluateChangesToConversation(ConversationDialogue conversationData)
    {
        if(dialogueLibrary.ContainsKey(conversationData.dialogueKey) && dialogueLibrary[conversationData.dialogueKey].Contains(conversationData))
        {
            //This dictionary item still contains the conversation, this should always be true
            if(dialogueLibrary[conversationData.dialogueKey].Remove(conversationData))
            {
                //Removed the item, now evaluate the key and readd it
                conversationData.RebuildKey();
                if (!dialogueLibrary.ContainsKey(conversationData.dialogueKey))
                    dialogueLibrary.Add(conversationData.dialogueKey, new List<ConversationDialogue>());
                dialogueLibrary[conversationData.dialogueKey].Add(conversationData);
            }
        }
    }

    private void EvaluateExclusiveGroupChanges(ConversationDialogue conversationData)
    {
        if (dialogueLibrary.ContainsKey(conversationData.dialogueKey) && dialogueLibrary[conversationData.dialogueKey].Contains(conversationData))
        {
            //This dictionary item still contains the conversation, this should always be true

            if (conversationData.exclusiveFlag)
            {
                //Loop through and get all other exclusive items with same priority and dictionary key and set them to maxDisplayCount 0 as well. 
                List<ConversationDialogue> listToChange = new List<ConversationDialogue>();
                for(int i = 0; i < dialogueLibrary[conversationData.dialogueKey].Count; i++)
                {
                    if(dialogueLibrary[conversationData.dialogueKey][i].priorityIndex == conversationData.priorityIndex && dialogueLibrary[conversationData.dialogueKey][i].exclusiveFlag)
                    {
                        dialogueLibrary[conversationData.dialogueKey][i].maxDisplayCount = 0; //this is an exclusive item, when used we set the maxDisplayCount to 0, thus moving it to the Archive
                        listToChange.Add(dialogueLibrary[conversationData.dialogueKey][i]);
                    }
                }
                
                for(int i = 0; i < listToChange.Count; i++)
                {
                    EvaluateChangesToConversation(listToChange[i]); //Evaluate changes and move conversations to different keys for archiving
                }
            }
        }
    }

    private IEnumerator DisplayConversation(ConversationDialogue theConversation)
    {

        float lastDialogueTimestamp = Time.realtimeSinceStartup;

        float conversationElapsedTime = 0f;
        float lastDialogueElapsedTime = 0f;

        float lastDeltaTime = 0f;
        int currentDialogueIndex = -1;
        while(conversationElapsedTime < theConversation.displayTime)
        {
            //Adjust delta tiem by the current timeScale, for pausing the game
            lastDeltaTime = (Time.realtimeSinceStartup - lastDialogueTimestamp) * Time.timeScale;
            lastDialogueTimestamp = Time.realtimeSinceStartup;

            conversationElapsedTime += lastDeltaTime;
            lastDialogueElapsedTime += lastDeltaTime;
            if (theConversation.involvedDialogues.Count > 0)
            {
                try
                {                   
                    if (currentDialogueIndex == -1 || (currentDialogueIndex < theConversation.involvedDialogues.Count && lastDialogueElapsedTime >= theConversation.involvedDialogues[currentDialogueIndex].displayTime))
                    {

                        //dialogue has displayed full duration, display the next one
                        currentDialogueIndex++;
                        if (currentDialogueIndex < theConversation.involvedDialogues.Count)
                        {
                            int theOwnerID = theConversation.involvedDialogues[currentDialogueIndex].ownerID;
                            int theEventID = theConversation.eventID;

                            //Find an object to track to
                            Transform speakerTarget = FindSpeakerTransform(theOwnerID, theEventID);
                            if (speakerTarget != null)
                            {
                                DialogueDisplayInfo spawnDisplayInfo = new DialogueDisplayInfo(defaultDisplayInfo);

                                SpeakerToken speakerClass = speakerTarget.GetComponent<SpeakerToken>();
                                if (speakerClass != null && speakerClass.displayInfo != null)
                                {
                                    //have a dialogue prefab, don't use the default info
                                    if (speakerClass.displayInfo.dialoguePrefab != null)
                                        spawnDisplayInfo.dialoguePrefab = speakerClass.displayInfo.dialoguePrefab;
                                    if (speakerClass.displayInfo.primaryTextColor != null)
                                        spawnDisplayInfo.primaryTextColor = speakerClass.displayInfo.primaryTextColor;

                                    spawnDisplayInfo.trackPosition = speakerClass.displayInfo.trackPosition;
                                }

                                GameObject dialogueInst = Instantiate(spawnDisplayInfo.dialoguePrefab, dialogueParent, false) as GameObject;
                                if (dialogueInst)
                                {
                                    dialogueInst.name = "Conversation" + theConversation.conversationId.ToString() + "_Dialogue" + theConversation.involvedDialogues[currentDialogueIndex].dialogueID.ToString();
                                    DialogueDriver driverInst = dialogueInst.GetComponent<DialogueDriver>();
                                    if (driverInst)
                                    {
                                        //Find the closest speaker to the camera
                                        driverInst.Setup(speakerTarget, theConversation.involvedDialogues[currentDialogueIndex], spawnDisplayInfo.primaryTextColor, spawnDisplayInfo.trackPosition);
                                        Destroy(dialogueInst, theConversation.involvedDialogues[currentDialogueIndex].displayTime);
                                        lastDialogueElapsedTime = 0f;
                                    }
                                }
                                //play audio blip
                                if (RoverControl.Instance != null && RoverControl.Instance.roverAudioPlayer != null && RoverControl.Instance.roverAudioPlayer.GetAudioSource("DialogueBleep") != null)
                                {
                                    RoverControl.Instance.roverAudioPlayer.GetAudioSource("DialogueBleep").Play(); //play audio
                                }
                            }

                            //Lets hope it doesn't return early, as the if condition for the timer to the next dialogue is based on this, if for some reason it returns early, there will be another set of displayTime to wait, then we are gonna miss a dialogue
                            //yield return new WaitForSecondsRealtime(theConversation.involvedDialogues[currentDialogueIndex].displayTime); //Wait for the duration of the dialogue to pass, then return to this function and continue
                        }
                        else
                            break;

                    }

                }
                catch (Exception)
                {
                    Debug.Log("Error with conversation ID: " + theConversation.conversationId + " with event ID: " + theConversation.eventID);
                }
            }
            yield return null; //Wait for the next frame
        }

        PostDisplayChanges(theConversation);
        readyToShowDialogue = true;
        yield return null;
    }

    //Get the speaker object that matches conditions for speakerID (actorID) and eventID
    //  if the scene has multiple speakers with the same ID, get the speaker with the most recent broadcast time for this event
    private Transform FindSpeakerTransform(int speakerID, int eventID)
    {
        Transform bestSpeaker = null;
        float bestTime = 0f;
        for(int i = 0; i < speakerList.Length; i++)
        {
            if(speakerList[i].actorID == speakerID)
            {
                //Found a valid speaker, check if its the most recent speaker that has broadcasted
                float checkTime = speakerList[i].GetLastBroadcastTime();
                if (bestSpeaker == null || checkTime > bestTime)
                {
                    //Found a more valid speaker
                    //Debug.Log("Found speaker");
                    bestSpeaker = speakerList[i].transform;
                    bestTime = checkTime;
                }
            }
        }

        return bestSpeaker;
    }

    private void AcquireAvailableConversations(ref List<ConversationDialogue> addToList, string dialogueKey)
    {
        //Add all conversation dialogues to addToList, where dialogueKey is the key to the dialogueLibrary 
        if(dialogueLibrary.ContainsKey(dialogueKey))
        {
            //Debug.Log("Found conversations with key: " + dialogueKey);
            addToList.AddRange(dialogueLibrary[dialogueKey]);
        }
    }

    private void BuildCombinations(out List<string> keyList)
    {
        keyList = new List<string>();
        for (int i = 0; i < occuringEvents.Keys.Count; i++)
        {
            int primarySpeakerID = occuringEvents.Keys[i];
            //For each primary key
            for (int j = 0; j < occuringEvents[primarySpeakerID].Keys.Count; j++)
            {
                //For each event on this primary key, check if there are actorID's stored in the list in its value pair
                int eventID = occuringEvents[primarySpeakerID].Keys[j];
                if (occuringEvents[primarySpeakerID][eventID].Count > 0)
                {
                    //Check if there are cooldown restrictions on this type of event
                    if (eventCooldownDictionary.ContainsKey(eventID))
                    {
                        //there are cooldown restrictions, check if the event is on cooldown still
                        if (!eventCooldownDictionary[eventID].OnCooldown())
                            AddPowerSetToList(ref keyList, occuringEvents[primarySpeakerID][eventID], eventID, primarySpeakerID);
                    }
                    else
                        AddPowerSetToList(ref keyList, occuringEvents[primarySpeakerID][eventID], eventID, primarySpeakerID);
                }
            }
        }
    }

    //Add all items in the powerSet of mainActorList to keyList, requiring primaryActorID to be in all items
    private void AddPowerSetToList(ref List<string> keyList, List<int> mainActorList, int eventID, int primaryActorID)
    {
        
        if (mainActorList.Contains(primaryActorID))
        {
            //Remove our primaryActorID from the list, because we want the powerset of the sequence without the primaryActorID, then we apply the primaryActorID to each sequence returned
            mainActorList.Remove(primaryActorID);
        }

        mainActorList.Sort();

        if (mainActorList.Count == 0)
        {
            keyList.Add(primaryActorID.ToString() + " : " + eventID.ToString());
        }
        else
        {
            int[][] powerSet = FastPowerSet<int>(mainActorList.ToArray()); //get the powerset of mainActorList
            
            for (int i = 0; i < powerSet.Length; i++)
            {
                List<int> powerSetItem = new List<int>(powerSet[i]);
                powerSetItem.Add(primaryActorID);
                keyList.Add(BuildEventKey(powerSetItem, eventID));
            }
        }
    }

    //FastPowerSet takes an array of elements, then returns a 2D array of powerset items, where each row contains an array of set items for the specific powerset item
    //The following function was made by SergeyS on stackoverflow.com
    //    link to the page: http://stackoverflow.com/questions/19890781/creating-a-power-set-of-a-sequence
    //    See section with Another approach (twice faster) and generic implementation
    public static T[][] FastPowerSet<T>(T[] seq)
    {
        var powerSet = new T[1 << seq.Length][];
        powerSet[0] = new T[0]; // starting only with empty set
        for (int i = 0; i < seq.Length; i++)
        {
            var cur = seq[i];
            int count = 1 << i; // doubling list each time
            for (int j = 0; j < count; j++)
            {
                var source = powerSet[j];
                var destination = powerSet[count + j] = new T[source.Length + 1];
                for (int q = 0; q < source.Length; q++)
                    destination[q] = source[q];
                destination[source.Length] = cur;
            }
        }
        return powerSet;
    }

    public string BuildEventKey(List<int> actorList, int eventID)
    {
        //Sort the actorList by id, then build the EventKey
        actorList.Sort();
        
        StringBuilder buildSet = new StringBuilder();
        buildSet.Length = 0;
        for (int i = 0; i < actorList.Count; i++)
        {
            if (i != 0)
                buildSet .Append(" : ");
            buildSet.Append(actorList[i].ToString());

            if (i == actorList.Count - 1)
            {
                buildSet.Append(" : ");
                buildSet.Append(eventID.ToString());
            }
        }

        return buildSet.ToString();
    }

    private void UpdateCooldownTimers(float timeDelta)
    {
        //Loop through the dictionary updating each cooldown
        foreach (var item in eventCooldownDictionary.Values)
        {
            item.Update(timeDelta); 
        }
    }

    private bool HasEvents()
    {
        for (int i = 0; i < occuringEvents.Keys.Count; i++)
        {
            int primarySpeakerID = occuringEvents.Keys[i];
            //For each primary key
            for (int j = 0; j < occuringEvents[primarySpeakerID].Keys.Count; j++)
            {
                //For each event on this primary key, check if there are actorID's stored in the list in its value pair
                int eventID = occuringEvents[primarySpeakerID].Keys[j];
                if (occuringEvents[primarySpeakerID][eventID].Count > 0)
                {
                    //Check if there are cooldown restrictions on this type of event
                    if (eventCooldownDictionary.ContainsKey(eventID))
                    {
                        //there are cooldown restrictions, check if the event is on cooldown still
                        if (!eventCooldownDictionary[eventID].OnCooldown())
                            return true;
                    }
                    else
                        return true;
                } 
            }
        }
        return false;
    }

    public void CallUpdateOnEventsInRangeOf(Transform checkPosition, int primarySpeakerID)
    {
        for (int i = 0; i < speakerList.Length; i++)
        {
            speakerList[i].CallUpdateOnEventsInRangeOf(checkPosition, primarySpeakerID);
        }
    }

    public void EventOcurring(int activatingActorID, int eventID, int eventActorID)
    {
        //Check if the occuring event is not blocked
        if (!eventBlocks[eventID])
        {
            //Not blocked, do additional checks

            //Check if this is the first event instance of this type
            if (!occuringEvents.ContainsKey(activatingActorID))
                occuringEvents.Add(activatingActorID, new SortedList<int, List<int>>());
            if (!occuringEvents[activatingActorID].ContainsKey(eventID))
                occuringEvents[activatingActorID].Add(eventID, new List<int>());
            if (!occuringEvents[activatingActorID][eventID].Contains(eventActorID))
            {
                //Debug.Log("Adding event for: " + activatingActorID + " with event: " + eventID);
                occuringEvents[activatingActorID][eventID].Add(eventActorID);
            }
        }
    }

    public void ClearEvents()
    {
        int clearedCount = 0;
        for(int i = 0; i < occuringEvents.Keys.Count; i++)
        {
            int primarySpeakerID = occuringEvents.Keys[i];
            //For each primary key
            for(int j = 0; j < occuringEvents[primarySpeakerID].Keys.Count; j++)
            {
                //For each event on this primary key, clear the list of ids from it's stored List<int> of actorID's
                int eventID = occuringEvents[primarySpeakerID].Keys[j];
                clearedCount += occuringEvents[primarySpeakerID][eventID].Count;
                occuringEvents[primarySpeakerID][eventID].Clear();
            }
        }
    }
}
