using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using UnityEditor.AnimatedValues;

[System.Serializable]
public class DialogueNarratorWindow : EditorWindow
{
    private static bool isReloaded = false;
    private float COLUMN_WIDTH = 60f;
    public delegate void ColumnFunction(int index=-1, float columnWidth=60f);

    public class ColumnEntry
    {
        public string columnTitle;
        public ColumnFunction displayFunc; //The function to display data when NOT in edit mode
        public ColumnFunction editFunc;    //The function to display data when in edit mode
        public float columnWidth;
        public ColumnEntry(string _columnTitle, ColumnFunction _displayFunc, ColumnFunction _editFunc, float _width)
        {
            columnTitle = _columnTitle;
            displayFunc = _displayFunc;
            editFunc = _editFunc;
            columnWidth = _width;
        }
    }

    private ConversationLibrary currentLibrary;

    private string narrativeDataPath = "/StreamingAssets/narrativeData.json";
    private string currentFilePath = "";
    private string currentFileName = "";
    private List<PopupSelectionItem> eventList_Data;
    private List<ColumnEntry> conversationColumns;
    private List<ColumnEntry> dialogueColumns;

    GUIStyle theStyle;
    GUIStyle theFieldStyle;

    //Row coloring
    Texture2D rowColorMain;
    Texture2D rowColorAlt;

    TextAnchor lastAnchor;

    private int editIndex = -1;
    private int dialogueEditConversationIndex = -1;
    private int dialogueEditIndex = -1;
    private bool expandAll = false;
    private Rect ownerActorRect;
    private Rect involvedActorsRect;
    private Rect editEventRect;
    private int currentConversationIndex = -1;
    private Vector2 currentScrollPosition;
    private Vector2 currentEditActorPosition;
    private int postProcessingDeleteIndex = -1;

    public enum NavigationState
    {
        Main, 
        NewDialogue, 
        NewConversation,
        ViewConversation, 
        ActorUpdate,
    }
    
    private NavigationState currentNavigationState = NavigationState.ViewConversation;

    // Add menu item named "My Window" to the Window menu
    [MenuItem("Window/Dialogue Narrator")]
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        DialogueNarratorWindow win = (DialogueNarratorWindow)EditorWindow.GetWindow(typeof(DialogueNarratorWindow));
        
        win.Show();
    }
    private void OnEnable()
    {
        EditorApplication.playmodeStateChanged += PlayStatChanged;

        if (conversationColumns == null)
            InitConversationColumns();
        if (dialogueColumns == null)
            InitDialogueColumns();
        if (eventList_Data == null)
            InitEventClassTypes();
        currentNavigationState = NavigationState.Main;

        InitColors();
    }

    private void OnDisable()
    {
        EditorApplication.playmodeStateChanged -= PlayStatChanged;
    }

    void PlayStatChanged()
    {
        InitColors();
    }

    void RecolorRow()
    {
        if(currentConversationIndex % 2 == 0)
        {
            
            theStyle.normal.background = rowColorMain;
        }
        else
        {
            theStyle.normal.background = rowColorAlt; 
        }
    }

    void InitColors()
    {
        //Make a 1 by 1 texture of the colors we need, for use during display
        rowColorMain = MakeTex(1, 1, new Color(.86f, .86f, .86f));
        rowColorAlt = MakeTex(1, 1, new Color(.69f, .69f, .69f));
    }

    void OnGUI()
    {

        //When this script is reloaded after compiling or running / closing the game, remove listeners and re add them. This fixes the sliding animation not working after running and stopping the game.
        if (isReloaded)
        {
            isReloaded = false;
            RemoveFadeGroupListeners();
            AddFadeGroupListeners();

            InitColors();
        }

        if (theStyle == null)
        {
            theStyle = new GUIStyle(GUI.skin.label);
            
        }

        if (theFieldStyle == null)
            theFieldStyle = new GUIStyle(GUI.skin.textField);

        GUI.skin.textField.alignment = TextAnchor.MiddleCenter;
        theStyle.wordWrap = true;
        theStyle.alignment = TextAnchor.MiddleCenter;

        theStyle.stretchHeight = true;
        theStyle.contentOffset = new Vector2(0f, 3f);
        theStyle.normal.background = GUI.skin.label.normal.background;

        if (Event.current.type == EventType.Repaint && postProcessingDeleteIndex != -1)
        {
            currentLibrary.DeleteConversationIndex(postProcessingDeleteIndex);
            currentLibrary.UpdatedUnusedConversationID();
            postProcessingDeleteIndex = -1;
        }

        //Move to the proper state we are in, and calculate
        switch (currentNavigationState)
        {
            case NavigationState.Main:
                editIndex = -1;
                dialogueEditIndex = -1;
                State_Main();
                break;
            case NavigationState.ViewConversation:
                State_View();
                break;
            case NavigationState.ActorUpdate:
                State_ActorUpdate();
                break;
            default:
                break;
        }
        GUILayout.FlexibleSpace();

        GUI.skin.textField.alignment = TextAnchor.MiddleLeft;
    }

    void InitEventClassTypes()
    {

        eventList_Data = new List<PopupSelectionItem>();

        Type[] eventClassTypes = OptionsManager.GetAllDerivedClassesOf<DialogueEvent>();
        if (eventClassTypes != null)
        {
            for (int i = 0; i < eventClassTypes.Length; i++)
            {
                string optionName = "";
                int optionInt = -1;
                bool optionFlag = false;

                MethodInfo callbackMethod = eventClassTypes[i].GetMethod("GetComboBoxName_Static");
                if (callbackMethod != null)
                    optionName = (string)callbackMethod.Invoke(this, null);

                callbackMethod = eventClassTypes[i].GetMethod("GetEventID_Static");
                if (callbackMethod != null)
                    optionInt = (int)callbackMethod.Invoke(this, null);

                eventList_Data.Add(new PopupSelectionItem(optionInt, optionName, optionFlag));
            }
        }
    }

    #region SETUP_COLUMN_FUNCTIONS
    
    //Define each column for the conversation rows, header text, display function callback, edit function callback, and column widths
    void InitConversationColumns()
    {
        conversationColumns = new List<ColumnEntry>();
                                                    //add \n to second word so they each take up 2 lines?
        conversationColumns.Add(new ColumnEntry("Conv \nID", DisplayConversationID, EditConversationID, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Actor \nList", DisplayInvolvedActors, EditInvolvedActors, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Event \nID", DisplayEventID, EditEventID, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Display \nLimit", DisplayLimit, EditDisplayLimit, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Display \nCount", DisplayCount, EditDisplayCount, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Priority \nID", DisplayPriorityID, EditPriorityID, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Exclusive \nFlag", DisplayExclusiveFlag, EditExclusiveFlag, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Display \nWeight", DisplayWeight, EditDisplayWeight, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("Display \nChance", DisplayChance, EditDisplayChance, COLUMN_WIDTH));
        conversationColumns.Add(new ColumnEntry("", DoEditButtons, null, COLUMN_WIDTH));
    }

    //Define each column for the individual dialogue rows that are contained in a conversation, header text, display function callback, edit function callback, and column widths
    void InitDialogueColumns()
    {
        dialogueColumns = new List<ColumnEntry>();
        dialogueColumns.Add(new ColumnEntry("Diag \nID", DisplayDialogueID, EditDialogueID, COLUMN_WIDTH));
        dialogueColumns.Add(new ColumnEntry("Owner \nID", DisplayOwnerID, EditOwnerID, COLUMN_WIDTH * 1.5f));
        dialogueColumns.Add(new ColumnEntry("Display \nTime", DisplayShowTime, EditShowTime, COLUMN_WIDTH));
        dialogueColumns.Add(new ColumnEntry("Text \nFlag", DisplayShowText, EditShowText, COLUMN_WIDTH));
        dialogueColumns.Add(new ColumnEntry("Voice \nFlag", DisplayPlayVoice, EditPlayVoice, COLUMN_WIDTH));
        dialogueColumns.Add(new ColumnEntry("Voice \nPath", DisplayVoicePath, EditVoicePath, COLUMN_WIDTH*2));
        dialogueColumns.Add(new ColumnEntry("Dialogue\nText", DisplayDialogueText, EditDialogueText, COLUMN_WIDTH*5));
        dialogueColumns.Add(new ColumnEntry("", DoDialogueEditButtons, null, COLUMN_WIDTH));
    }

    #endregion

    //Display a button that sole purpose to navigate between panels in the editor
    void DisplayStateChangeButton(string title, NavigationState stateChange)
    {
        if (GUILayout.Button(title))
        {
            currentNavigationState = stateChange;
        }
    }

    

    #region STATE_DISPLAYS

    void State_Main()
    {
        theStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField("Main Menu", theStyle);
        //Top buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Narrative File"))
        {
            LoadNarrativeData();
        }
        if (GUILayout.Button("New Narrative File"))
        {
            NewNarrativeData();
        }

        EditorGUILayout.EndHorizontal();
        //end top

        if (currentLibrary != null)
        {
            theStyle.alignment = TextAnchor.MiddleCenter;
            EditorGUILayout.LabelField(currentFilePath, theStyle);

            EditorGUILayout.BeginHorizontal();
            theStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("Conversations: " + currentLibrary.theConversationList.Count, theStyle);

            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
            DisplayStateChangeButton("View Narrative", NavigationState.ViewConversation);
        }
        
        GUI.enabled = true;
    }

    void State_View()
    {
        theStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField("Viewing Narrative : " + currentFileName);//, theStyle);
        theStyle.alignment = TextAnchor.MiddleCenter;
        //Top buttons

        EditorGUILayout.BeginHorizontal();
        if(GUILayout.Button("Edit Actors"))
        {
            currentNavigationState = NavigationState.ActorUpdate;
        }
        //DisplayStateChangeButton("Update Actors", NavigationState.ActorUpdate);
        if(GUILayout.Button("Add Conversation"))
        {
            currentLibrary.AddConversation();
            currentLibrary.theConversationList[currentLibrary.theConversationList.Count - 1].expandedDialogues = new AnimBool(false);
            currentLibrary.theConversationList[currentLibrary.theConversationList.Count - 1].expandedDialogues.valueChanged.AddListener(Repaint);

            currentLibrary.theConversationList[currentLibrary.theConversationList.Count - 1].expandedPreview = new AnimBool(true);
            currentLibrary.theConversationList[currentLibrary.theConversationList.Count - 1].expandedPreview.valueChanged.AddListener(Repaint);
        }
        EditorGUILayout.EndHorizontal();
        //end top
        
        ConversationRows();

        if (editIndex != -1)
        {
            GUI.enabled = false;
        }
        else
            GUI.enabled = true;
        if (currentLibrary != null)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                SaveNarrativeData();
            }
            EditorGUILayout.Separator();
            if (GUILayout.Button("Save As"))
            {
                SaveAsNarrativeData();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
        GUI.enabled = true;
        DisplayStateChangeButton("Back to Menu", NavigationState.Main);
        EditorGUILayout.Space();
    }
    
    void State_ActorUpdate()
    {
        
        //Title of window
        theFieldStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField("Editting Actors : " + currentFileName);
        theFieldStyle.alignment = TextAnchor.MiddleCenter;

        //Contents of window
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        theStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label("Actor Name", theStyle, GUILayout.Width(COLUMN_WIDTH * 3));
        theStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label("Actor ID", theStyle, GUILayout.Width(COLUMN_WIDTH * 1));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();


        currentEditActorPosition = GUILayout.BeginScrollView(currentEditActorPosition);

        for (int i = 0; i < currentLibrary.actorList.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            theFieldStyle.alignment = TextAnchor.MiddleRight;
            currentLibrary.actorList[i].actorName = EditorGUILayout.TextField(currentLibrary.actorList[i].actorName, theFieldStyle, GUILayout.Width(COLUMN_WIDTH * 3));
            theFieldStyle.alignment = TextAnchor.MiddleLeft;
            currentLibrary.actorList[i].actorID = EditorGUILayout.IntField(currentLibrary.actorList[i].actorID, theFieldStyle, GUILayout.Width(COLUMN_WIDTH * 1));

            if (GUILayout.Button("Remove"))
            {
                currentLibrary.actorList.RemoveAt(i);
                break;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if(GUILayout.Button("Add Actor"))
        {
            currentLibrary.actorList.Add(new DialogueActor("New Actor Name", -1));
        }
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        //Navigation
        DisplayStateChangeButton("Back to Narrative", NavigationState.ViewConversation);
    }

    #endregion

    void ConversationRows()
    {
        currentConversationIndex = 0;
        RecolorRow();
        //Do header
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < conversationColumns.Count; i++)
        {
            theStyle.wordWrap = true;
            //Headers for conversation columns
            DisplayConversationHeader(conversationColumns[i].columnTitle, conversationColumns[i].columnWidth);
        }
        EditorGUILayout.EndHorizontal();

        currentScrollPosition = GUILayout.BeginScrollView(currentScrollPosition);
        
        for (int j = 0; j < currentLibrary.theConversationList.Count; j++)
        {
            
            currentConversationIndex = j;
            RecolorRow();
            Rect lastVertRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
                theStyle.Draw(lastVertRect, false, false, false, false);
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < conversationColumns.Count; i++)
            {
                theStyle.wordWrap = false;

                //Display either the edit data, or the visual data based on if we are actively in row edit mode or not
                if (editIndex == j && conversationColumns[i].editFunc != null)
                    conversationColumns[i].editFunc(j, conversationColumns[i].columnWidth);
                else
                    conversationColumns[i].displayFunc(j, conversationColumns[i].columnWidth);
            }
            EditorGUILayout.EndHorizontal();

            //Display detailed children, if they are visible
            if (currentLibrary.theConversationList[j].expandedDialogues.faded > 0.1f)
            {
                EditorGUILayout.BeginFadeGroup(currentLibrary.theConversationList[j].expandedDialogues.faded);
                if (currentLibrary.theConversationList[j].involvedDialogues == null)
                    currentLibrary.theConversationList[j].involvedDialogues = new List<SingleDialogue>();
                if (currentLibrary.theConversationList[j].involvedDialogues != null)
                {
                    EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_WIDTH * 7f), GUILayout.Height(5));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    for (int m = -1; m < currentLibrary.theConversationList[j].involvedDialogues.Count; m++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        for (int n = 0; n < dialogueColumns.Count; n++)
                        {
                            if (n == 0)
                            {
                                //Add COLUMN_WIDTH * 2.5f as an empty left column
                                EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_WIDTH * 1.5f));
                                EditorGUILayout.LabelField("", GUILayout.Width(0f));
                                EditorGUILayout.EndHorizontal();
                            }
                            if (m == -1)
                            {
                                //Headers for conversation columns
                                currentConversationIndex = j;
                                DisplayDialogueHeader(dialogueColumns[n].columnTitle, dialogueColumns[n].columnWidth);
                            }
                            else
                            {
                                currentConversationIndex = j;
                                if (dialogueEditConversationIndex == currentConversationIndex && dialogueEditIndex == m && dialogueColumns[n].editFunc != null)
                                    dialogueColumns[n].editFunc(m, dialogueColumns[n].columnWidth);
                                else
                                    dialogueColumns[n].displayFunc(m, dialogueColumns[n].columnWidth);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                        if(m==-1)
                        {
                            EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_WIDTH * 7f), GUILayout.Height(5));
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    if (editIndex != -1 || dialogueEditIndex != -1)
                        GUI.enabled = false;
                    else
                        GUI.enabled = true;
                    if (GUILayout.Button("Add Dialogue", GUILayout.Width(COLUMN_WIDTH * 4)))
                    {
                        currentLibrary.theConversationList[j].AddNewDialogue();
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    
                }
                EditorGUILayout.EndFadeGroup();
            }

            //Display non detailed children, not expanded, this should be a summary of the children contents
            else
            {
                if (currentLibrary.theConversationList[j].expandedPreview.target == currentLibrary.theConversationList[j].expandedDialogues.target)
                {
                    currentLibrary.theConversationList[j].expandedPreview.target = !currentLibrary.theConversationList[j].expandedPreview.target;
                }
                if (currentLibrary.theConversationList[j].expandedPreview.faded > 0.05f)
                {
                    EditorGUILayout.BeginFadeGroup(currentLibrary.theConversationList[j].expandedPreview.faded);
                    for (int z = 0; z < currentLibrary.theConversationList[j].involvedDialogues.Count; z++)
                    {
                        EditorGUILayout.BeginHorizontal();

                        //Add COLUMN_WIDTH * 2.5f as an empty left column
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_WIDTH * 1.5f));
                        EditorGUILayout.LabelField("", GUILayout.Width(0f));
                        EditorGUILayout.EndHorizontal();
                        
                        if (editIndex != -1 || dialogueEditIndex != -1)
                            GUI.enabled = false;
                        else
                            GUI.enabled = true;

                        EditorGUILayout.LabelField(currentLibrary.GetActorNameByID(currentLibrary.theConversationList[j].involvedDialogues[z].ownerID) + " - " + currentLibrary.theConversationList[j].involvedDialogues[z].dialogueText, GUILayout.ExpandWidth(true));

                        EditorGUILayout.EndHorizontal();
                    }
                    if(currentLibrary.theConversationList[j].involvedDialogues.Count == 0)
                    {
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_WIDTH * 7f), GUILayout.Height(5));
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndFadeGroup();
                }
            }
            EditorGUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
    }

    void DisplayConversationHeader(string title, float columnWidth)
    {
        
        GUI.enabled = true;
        if (title == "")
        {
            DoExpandButtons(-1, columnWidth);
        }
        else
        {
            GUILayout.Label(title, theStyle, GUILayout.Width(columnWidth), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
        }
    }

    void DisplayDialogueHeader(string title, float columnWidth)
    {
        if (editIndex != -1)
            GUI.enabled = false;
        else if (dialogueEditIndex != -1)
        {
            if (dialogueEditConversationIndex == currentConversationIndex)
                GUI.enabled = true;
            else
                GUI.enabled = false;
        }
        else
            GUI.enabled = true;
        if (title != "")
            GUILayout.Label(title, theStyle, GUILayout.Width(columnWidth));
    }

    #region CONVERSATION_FUNCTIONS
    void DisplayConversationID(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(currentLibrary.theConversationList[index].conversationId.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditConversationID(int index, float columnWidth)
    {
        GUI.enabled = true;
        currentLibrary.theConversationList[index].conversationId = EditorGUILayout.IntField(currentLibrary.theConversationList[index].conversationId, GUILayout.Width(columnWidth));

    }

    void DisplayInvolvedActors(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        string actorStringList = "";
        for (int j = 0; j < currentLibrary.theConversationList[index].actorIDList.Count; j++)
        {
            if (j != 0)
                actorStringList += ", ";
            actorStringList += currentLibrary.theConversationList[index].actorIDList[j].ToString();
        }
        GUILayout.Label(actorStringList , theStyle, GUILayout.Width(columnWidth));
    }

    void EditInvolvedActors(int index, float columnWidth)
    {
        GUI.enabled = true;

        List<PopupSelectionItem> optionsList = new List<PopupSelectionItem>();
        for(int i = 0; i < currentLibrary.actorList.Count; i++)
        {
            PopupSelectionItem nextItem = new PopupSelectionItem(currentLibrary.actorList[i].actorID, currentLibrary.actorList[i].ToString(), currentLibrary.theConversationList[index].actorIDList.Contains(currentLibrary.actorList[i].actorID));
            optionsList.Add(nextItem);
        }
        
        if (GUILayout.Button(currentLibrary.theConversationList[index].GetActorString(), EditorStyles.layerMaskField, GUILayout.Width(columnWidth)))
        {
            
            PopupWindow.Show(involvedActorsRect, new MultiSelectionPopup(optionsList, currentLibrary.theConversationList[index].SetActorList, true, this));
        }
        if (Event.current.type == EventType.Repaint)
            involvedActorsRect = GUILayoutUtility.GetLastRect();
    }

    void DisplayEventID(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        string displayString = currentLibrary.theConversationList[index].eventID.ToString();
        for (int i = 0; i < eventList_Data.Count; i++)
        {
            if(eventList_Data[i].optionInt == currentLibrary.theConversationList[index].eventID)
            {
                displayString = eventList_Data[i].optionName;
            }
        }
        GUILayout.Label(displayString, theStyle, GUILayout.Width(columnWidth));
    }

    void EditEventID(int index, float columnWidth)
    {
        GUI.enabled = true;
        string displayString = currentLibrary.theConversationList[index].eventID.ToString();
        for (int i = 0; i < eventList_Data.Count; i++)
        {
            eventList_Data[i].selectedFlag = (currentLibrary.theConversationList[index].eventID == eventList_Data[i].optionInt);
            if (eventList_Data[i].optionInt == currentLibrary.theConversationList[index].eventID)
            {
                displayString = eventList_Data[i].optionName;
            }
        }

        if (GUILayout.Button(displayString, EditorStyles.layerMaskField, GUILayout.Width(columnWidth)))
        {

            PopupWindow.Show(editEventRect, new MultiSelectionPopup(eventList_Data, currentLibrary.theConversationList[index].SetEventID, false, this));
        }
        if (Event.current.type == EventType.Repaint)
            editEventRect = GUILayoutUtility.GetLastRect();
    }

    void DisplayLimit(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        string displayCount = "";
        if (currentLibrary.theConversationList[index].maxDisplayCount == -1)
            displayCount = "\u221E";
        else
            displayCount = currentLibrary.theConversationList[index].maxDisplayCount.ToString();
        GUILayout.Label(displayCount, theStyle, GUILayout.Width(columnWidth));
    }

    void EditDisplayLimit(int index, float columnWidth)
    {
        GUI.enabled = true;
        int newCount = EditorGUILayout.IntField(currentLibrary.theConversationList[index].maxDisplayCount, GUILayout.Width(columnWidth));
        if (newCount < -1)
            currentLibrary.theConversationList[index].maxDisplayCount = -1;
        else
        {
            currentLibrary.theConversationList[index].maxDisplayCount = newCount;
        }
    }

    void DisplayCount(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(currentLibrary.theConversationList[index].displayCount.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditDisplayCount(int index, float columnWidth)
    {
        GUI.enabled = true;
        int newCount = EditorGUILayout.IntField(currentLibrary.theConversationList[index].displayCount, GUILayout.Width(columnWidth));
        if (newCount < 0)
            currentLibrary.theConversationList[index].displayCount = 0;
        else
        {
            currentLibrary.theConversationList[index].displayCount = newCount;
        }
    }

    void DisplayPriorityID(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(currentLibrary.theConversationList[index].priorityIndex.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditPriorityID(int index, float columnWidth)
    {
        GUI.enabled = true;
        currentLibrary.theConversationList[index].priorityIndex = EditorGUILayout.IntField(currentLibrary.theConversationList[index].priorityIndex, GUILayout.Width(columnWidth)); 
    }

    void DisplayExclusiveFlag(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(currentLibrary.theConversationList[index].exclusiveFlag.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditExclusiveFlag(int index, float columnWidth)
    {
        GUI.enabled = true;
        EditorGUILayout.BeginHorizontal(GUILayout.Width(columnWidth));
        EditorGUILayout.LabelField("", GUILayout.Width(columnWidth/2f - 10f)); //fake the space so it looks centered?
        currentLibrary.theConversationList[index].exclusiveFlag = EditorGUILayout.Toggle(currentLibrary.theConversationList[index].exclusiveFlag, GUILayout.ExpandWidth(false));
        
        EditorGUILayout.EndHorizontal();
    }

    void DisplayWeight(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(currentLibrary.theConversationList[index].displayWeight.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditDisplayWeight(int index, float columnWidth)
    {
        GUI.enabled = true;
        currentLibrary.theConversationList[index].displayWeight = EditorGUILayout.IntField(currentLibrary.theConversationList[index].displayWeight, GUILayout.Width(columnWidth));
    }

    void DisplayChance(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);
        GUILayout.Label(((int)(currentLibrary.theConversationList[index].displayChance * 100)).ToString() + "%", theStyle, GUILayout.Width(columnWidth));
    }

    void EditDisplayChance(int index, float columnWidth)
    {
        GUI.enabled = true;
        currentLibrary.theConversationList[index].displayChance = Mathf.Clamp01(EditorGUILayout.FloatField(currentLibrary.theConversationList[index].displayChance, GUILayout.Width(columnWidth)));
    }

    #endregion

    #region DIALOGUE_FUNCTIONS
    void DisplayDialogueID(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueID.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditDialogueID(int index, float columnWidth)
    {
        GUI.enabled = true;
        int lastID = currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueID;
        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueID = EditorGUILayout.IntField(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueID, GUILayout.Width(columnWidth));
        if (lastID != currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueID)
            currentLibrary.theConversationList[currentConversationIndex].ChangedDialogueID();
    }


    void DisplayDialogueText(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        theStyle.wordWrap = true;
       
        int lineHeight = Mathf.CeilToInt(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText.Length / 52f);
        if (lineHeight == 0)
            lineHeight = 1;
        //Debug.Log("Chars: " + currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText.Length + " LineHeight: " + lineHeight);
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText, theStyle, GUILayout.MinWidth(columnWidth), GUILayout.Width(columnWidth), GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight * lineHeight));
        theStyle.wordWrap = false;
    }

    void EditDialogueText(int index, float columnWidth)
    {
        GUI.enabled = true;
        int lineHeight = Mathf.CeilToInt(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText.Length / 52f);
        if (lineHeight == 0)
            lineHeight = 1;
        GUI.skin.textArea.wordWrap = true;

        string lastString = currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText;
        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText = EditorGUILayout.TextArea(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText, GUILayout.Width(columnWidth), GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight * lineHeight));

        if (lastString != currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText)
            currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].DialogueTextChanged();
    }

    void DisplayOwnerID(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        theStyle.wordWrap = true;
        GUILayout.Label(currentLibrary.GetActorNameByID(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].ownerID), theStyle,  GUILayout.Width(columnWidth));
        theStyle.wordWrap = false;
    }

    void EditOwnerID(int index, float columnWidth)
    {
        GUI.enabled = true;
        int ownerID = currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].ownerID;
        

        if (GUILayout.Button(currentLibrary.GetActorNameByID(ownerID), EditorStyles.layerMaskField, GUILayout.Width(columnWidth)))
        {
            List<PopupSelectionItem> optionsList = new List<PopupSelectionItem>();
            for (int i = 0; i < currentLibrary.theConversationList[currentConversationIndex].actorIDList.Count; i++)
            {
                PopupSelectionItem nextItem = new PopupSelectionItem(currentLibrary.theConversationList[currentConversationIndex].actorIDList[i], currentLibrary.GetActorNameByID(currentLibrary.theConversationList[currentConversationIndex].actorIDList[i]), ownerID == currentLibrary.theConversationList[currentConversationIndex].actorIDList[i]);
                optionsList.Add(nextItem);
            }

            PopupWindow.Show(ownerActorRect, new MultiSelectionPopup(optionsList, currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].SetOwnerID, false, this));
        }
        if (Event.current.type == EventType.Repaint)
            ownerActorRect = GUILayoutUtility.GetLastRect();
    }

    void DisplayShowTime(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].displayTime.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditShowTime(int index, float columnWidth)
    {
        GUI.enabled = true;

        float lastVal = currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].displayTime;

        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].displayTime = EditorGUILayout.FloatField(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].displayTime, GUILayout.Width(columnWidth));

        if (lastVal != currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].displayTime)
            currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].DisplayTimeChanged();
    }

    void DisplayShowText(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].showText.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditShowText(int index, float columnWidth)
    {
        GUI.enabled = true;
        EditorGUILayout.BeginHorizontal(GUILayout.Width(columnWidth));
        EditorGUILayout.LabelField("", GUILayout.Width(columnWidth / 2f - 10f)); //fake the space so it looks centered?
        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].showText = EditorGUILayout.Toggle(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].showText, GUILayout.ExpandWidth(false));

        EditorGUILayout.EndHorizontal();
    }

    void DisplayPlayVoice(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].playVoice.ToString(), theStyle, GUILayout.Width(columnWidth));
    }

    void EditPlayVoice(int index, float columnWidth)
    {
        GUI.enabled = true;
        EditorGUILayout.BeginHorizontal(GUILayout.Width(columnWidth));
        EditorGUILayout.LabelField("", GUILayout.Width(columnWidth / 2f - 10f)); //fake the space so it looks centered?
        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].playVoice = EditorGUILayout.Toggle(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].playVoice, GUILayout.ExpandWidth(false));

        EditorGUILayout.EndHorizontal();
    }

    void DisplayVoicePath(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, false);
        theStyle.alignment = TextAnchor.MiddleRight;
        GUILayout.Label(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].voiceFilePath, theStyle, GUILayout.Width(columnWidth));
        theStyle.alignment = TextAnchor.MiddleCenter;
    }

    void EditVoicePath(int index, float columnWidth)
    {
        GUI.enabled = true;
        theStyle.alignment = TextAnchor.MiddleRight;
        currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].voiceFilePath = EditorGUILayout.TextField(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].voiceFilePath, GUILayout.Width(columnWidth));
        theStyle.alignment = TextAnchor.MiddleCenter;
    }
    #endregion

    #region EDIT_EXPAND_BUTTONS
    void DoEditButtons(int index, float columnWidth)
    {
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = IsRowEdit(index, true);

        if (editIndex == index)
        {
            GUI.enabled = true;
            if (GUILayout.Button("Done"))
            {
                GUI.FocusControl("");
                editIndex = -1;
                dialogueEditIndex = -1;
                dialogueEditConversationIndex = -1;
                currentLibrary.SortLibraryByID();
                currentLibrary.UpdatedUnusedConversationID();
            }
                
            if (GUILayout.Button("Delete"))
            {
                editIndex = -1;
                dialogueEditIndex = -1;
                dialogueEditConversationIndex = -1;
                
                postProcessingDeleteIndex = index;
            }
        }
        else
        {

            if (GUILayout.Button("Edit"))
            {
                editIndex = index;
                dialogueEditIndex = -1;
                dialogueEditConversationIndex = -1;
            }
            if (currentLibrary.theConversationList[index].expandedDialogues.target)
            {
                if (GUILayout.Button("Collapse"))
                {
                    currentLibrary.theConversationList[index].expandedDialogues.target = false;
                }
            }
            else
            {
                if (GUILayout.Button("Expand"))
                {
                    currentLibrary.theConversationList[index].expandedDialogues.target = true;
                }
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
            
      
    }

    void DoDialogueEditButtons(int index, float columnWidth)
    {
       
        
        
        EditorGUILayout.BeginVertical();

        int lineHeight = Mathf.CeilToInt(currentLibrary.theConversationList[currentConversationIndex].involvedDialogues[index].dialogueText.Length / 52f);
        if (lineHeight == 0)
            lineHeight = 1;
        float verticalSpacing = (EditorGUIUtility.singleLineHeight * lineHeight) / 2f - EditorGUIUtility.singleLineHeight / 2f;
        
        GUILayout.Space(verticalSpacing);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = IsRowEdit(index, false);

        if (dialogueEditIndex == index && currentConversationIndex == dialogueEditConversationIndex)
        {
            GUI.enabled = true;
            if (GUILayout.Button("Done"))
            {
                GUI.FocusControl("");
                dialogueEditIndex = -1;
                dialogueEditConversationIndex = -1;
                currentLibrary.theConversationList[currentConversationIndex].SortDialoguesByID();
            }

            if (GUILayout.Button("Delete"))
            {
                dialogueEditIndex = -1;
                dialogueEditConversationIndex = -1;
                currentLibrary.DeleteDialogueIndex(index, currentConversationIndex);
                currentLibrary.theConversationList[currentConversationIndex].UpdateUnusedID();
            }
        }
        else
        {
            if (GUILayout.Button("Edit"))
            {
                dialogueEditIndex = index;
                dialogueEditConversationIndex = currentConversationIndex;
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        

    }

    void DoExpandButtons(int index, float columnWidth)
    {
        GUI.enabled = IsRowEdit(index, true);

        if (expandAll)
        {
            if (GUILayout.Button("Collapse All"))
            {
                expandAll = false;
                for (int i = 0; i < currentLibrary.theConversationList.Count; i++)
                {
                    currentLibrary.theConversationList[i].expandedDialogues.target = false;
                }
            }
        }
        else
        {
            if (GUILayout.Button("Expand All"))
            {
                expandAll = true;
                for(int i = 0; i < currentLibrary.theConversationList.Count; i++)
                {
                    currentLibrary.theConversationList[i].expandedDialogues.target = true;
                }
            }
        }
        
    }
    #endregion

    public static void FixedEndFadeGroup(float aValue)
    {
        if (aValue == 0f || aValue == 1f)
            return;
        EditorGUILayout.EndFadeGroup();
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        // This function is called when a script a reloaded by Unity
        isReloaded = true; //inform the update that we need to resubsbribe and initialize things
    }

    void AddFadeGroupListeners()
    {
        //Tell each fade animation variable that we need to repaint the display each time they update their scroll values
        if(currentLibrary != null && currentLibrary.theConversationList != null)
        {
            for(int i = 0; i < currentLibrary.theConversationList.Count; i++)
            {
                currentLibrary.theConversationList[i].expandedDialogues = new AnimBool(false);
                currentLibrary.theConversationList[i].expandedDialogues.speed = 1f;
                currentLibrary.theConversationList[i].expandedDialogues.valueChanged.AddListener(Repaint);

                currentLibrary.theConversationList[i].expandedPreview = new AnimBool(true);
                currentLibrary.theConversationList[i].expandedPreview.speed = 1f;
                currentLibrary.theConversationList[i].expandedPreview.valueChanged.AddListener(Repaint);
            }
        }
    }

    void RemoveFadeGroupListeners()
    {
        if (currentLibrary != null && currentLibrary.theConversationList != null)
        {
            for (int i = 0; i < currentLibrary.theConversationList.Count; i++)
            {
                if(currentLibrary.theConversationList[i].expandedDialogues != null)
                    currentLibrary.theConversationList[i].expandedDialogues.valueChanged.RemoveAllListeners();

                if (currentLibrary.theConversationList[i].expandedPreview != null)
                    currentLibrary.theConversationList[i].expandedPreview.valueChanged.RemoveAllListeners();
            }
        }
    }

    private void LoadNarrativeData()
    {
        string filePath = Application.dataPath + "/StreamingAssets/";
        string path = EditorUtility.OpenFilePanel("Open stored narrative json file", filePath, "json");
        if (path.Length != 0)
        {
            if (File.Exists(path))
            {
                string dataAsJson = File.ReadAllText(path);
                currentLibrary = JsonUtility.FromJson<ConversationLibrary>(dataAsJson);
                currentFilePath = path;
                currentFileName = Path.GetFileName(path);
                currentLibrary.DoFreshOpen();
                AddFadeGroupListeners();
            }
        }
        
    }

    private void NewNarrativeData()
    {
        currentLibrary = new ConversationLibrary();
        currentFilePath = "";
        currentFileName = "";
    }

    private void SaveAsNarrativeData()
    {
        LoopAndUpdateConversationTimes();

        string dataAsJson = JsonUtility.ToJson(currentLibrary);

        string filePath = "";
        if (currentFilePath == "")
        {
            filePath = Application.dataPath + narrativeDataPath;
        }
        else
        {
            filePath = currentFilePath;
        }

        string path = EditorUtility.SaveFilePanel("Narrative Save As", Path.GetDirectoryName(filePath), Path.GetFileName(filePath), "json");
        if (path.Length != 0)
        {
            File.WriteAllText(path, dataAsJson);
            currentFileName = Path.GetFileName(path);
            currentFilePath = path;
        }
            
    }

    private void SaveNarrativeData()
    {
        LoopAndUpdateConversationTimes();
        if (currentFilePath == "")
        {
            SaveAsNarrativeData();
        }
        else
        {
            if (File.Exists(currentFilePath))
            {
                string dataAsJson = JsonUtility.ToJson(currentLibrary);
                File.WriteAllText(currentFilePath, dataAsJson);
            }
            else
                SaveAsNarrativeData();
        }
    }

    private void LoopAndUpdateConversationTimes()
    {
        for(int i = 0; i < currentLibrary.theConversationList.Count;i++)
        {
            currentLibrary.theConversationList[i].UpdateConversationTime();
        }
    }

    //Determine if the current row is being editted. Can be used for both conversation row types and dialogue row types
    private bool IsRowEdit(int index, bool isConversationRow)
    {
        if(isConversationRow)
        {
            //Logic for conversation rows
            if (dialogueEditIndex != -1)
            {
                return false;
            }
            else
            {
                //no dialogue item being editted, check if conversation rows are in edit mode
                if (editIndex != -1)
                {
                    if (editIndex == index)
                        return true;
                    else
                        return false;
                }
                else
                    return true;
            }
        }
        else
        {
            //Logic for dialogue rows
            if (dialogueEditIndex != -1)
            {
                //Dialogue Item being editted
                if (dialogueEditIndex == index && dialogueEditConversationIndex == currentConversationIndex)
                    return true;
                else
                    return false;
            }
            else
            {
                //no dialogue item being editted, check if conversation rows are in edit mode
                if (editIndex != -1)
                    return false;
                else
                    return true;
            }
        }
        
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];

        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }
}

//Drop down menu item
public class PopupSelectionItem
{
    public int optionInt;
    public string optionName;
    public bool selectedFlag;
    public PopupSelectionItem()
    {
        optionName = "";
        selectedFlag = false;
    }
    public PopupSelectionItem(int _optionInt, string _optionName, bool _selectedFlag)
    {
        optionInt = _optionInt;
        optionName = _optionName;
        selectedFlag = _selectedFlag;
    }
}

//Drop down menu that supports settings so you can select multiple options.
public class MultiSelectionPopup : PopupWindowContent
{
    public delegate void MultiSelectionListCallback(List<int> setList);
    private List<PopupSelectionItem> optionsList;
    private MultiSelectionListCallback editCallbackFunc;
    private bool isMultiSelectEnabled = true;
    private EditorWindow parentWindow;
    private int hoverIndex = -1;
    private Vector2 popupScrollPosition;

    GUIStyle optionsStyle;
    GUIStyle optionsHoverStyle;
    Texture2D colorCyan;

    public MultiSelectionPopup()
    {
        optionsList = new List<PopupSelectionItem>();
        optionsStyle = new GUIStyle();
        editCallbackFunc = null;
        parentWindow = null;
        //optionsStyle.normal
        colorCyan = MakeTex(1, 1, new Color(.6f, .72f, .97f));
        optionsHoverStyle = new GUIStyle(optionsStyle);
        optionsHoverStyle.normal.background = colorCyan;
    }
    public MultiSelectionPopup(List<PopupSelectionItem> selectionList, MultiSelectionListCallback _editCallback, bool _isMultiSelectEnabled, EditorWindow _parentWindow)
    {
        editCallbackFunc = _editCallback;
        optionsList = selectionList.OrderBy(o => o.optionInt).ToList();
        isMultiSelectEnabled = _isMultiSelectEnabled;
        parentWindow = _parentWindow;

        optionsStyle = new GUIStyle();
        colorCyan = MakeTex(1, 1, new Color(.6f, .72f, .97f));

        optionsHoverStyle = new GUIStyle(optionsStyle);
        optionsHoverStyle.normal.background = colorCyan;
    }
    public override Vector2 GetWindowSize()
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (optionsList.Count > 0)
            height = EditorGUIUtility.singleLineHeight * 8;
        return new Vector2(180f, height);
    }
    public override void OnGUI(Rect rect)
    {
        popupScrollPosition = GUILayout.BeginScrollView(popupScrollPosition);
        for (int i = 0; i < optionsList.Count; i++)
        {
            Rect nextRect = EditorGUILayout.BeginHorizontal(GUILayout.Width(150f));
            if (Event.current.type == EventType.MouseMove && nextRect.Contains(Event.current.mousePosition))
            {
                hoverIndex = i;
                this.editorWindow.Repaint();
            }

            if (GUI.Button(nextRect, GUIContent.none, hoverIndex == i ? optionsHoverStyle : optionsStyle))
            {
                optionsList[i].selectedFlag = !optionsList[i].selectedFlag;

                //If multi select is false, only allow a single selection
                if (!isMultiSelectEnabled && optionsList[i].selectedFlag)
                {
                    //If we are setting a value to true, turn all others to false
                    for (int j = 0; j < optionsList.Count; j++)
                    {
                        if (j != i)
                        {
                            optionsList[j].selectedFlag = false;
                        }
                    }
                }
                //Fill a list to send to the callback func

                List<int> editList = new List<int>();
                if(isMultiSelectEnabled)
                {
                    for (int j = 0; j < optionsList.Count; j++)
                    {
                        if(optionsList[j].selectedFlag)
                        {
                            editList.Add(optionsList[j].optionInt);
                        }
                    }
                }
                else
                {
                    editList.Add(optionsList[i].optionInt);
                }
                editCallbackFunc(editList);
                if (parentWindow != null)
                    parentWindow.Repaint();
                
            }
            EditorGUILayout.LabelField(optionsList[i].selectedFlag ? "\u2714" : " ", GUILayout.Width(20f));
            EditorGUILayout.LabelField(optionsList[i].optionName);
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];

        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }

    public override void OnOpen()
    {
    }

    public override void OnClose()
    {
    }
}
 