using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

[CustomPropertyDrawer(typeof(BeaconTierData))]
public class BeaconDataDrawer : PropertyDrawer
{
    public bool isExpanded = true;
    private int currentTier = 1;
    private float rectWidth = 40f;
    private float horizontalSpacing = 3.0f;
    private float singleRectHeight = EditorGUIUtility.singleLineHeight;
    private SerializedProperty currentTierProp;
    private SerializedProperty totalTierProp;
    private GUIStyle theFloatFieldStyle = new GUIStyle(GUI.skin.textField);
    private GUIStyle theStyle = new GUIStyle(GUI.skin.label);
    private BeaconTierData theTierData;
    private int totalTiers = 0;
    private Rect[] tierRects;

    // Draw the property inside the given rect
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (tierRects == null)
            Resize();

        //Define the display rectangles
        SetRects(position);

        //Display fold out
        if (property.objectReferenceInstanceIDValue != 0)
            isExpanded = EditorGUI.Foldout(SplitObjectFoldoutLine(tierRects[0], true), isExpanded, label);
        else
            EditorGUI.LabelField(SplitObjectFoldoutLine(tierRects[0], true), label);
        EditorGUI.ObjectField(SplitObjectFoldoutLine(tierRects[0], false), property, GUIContent.none);

        //Draw the fold out contents if needed
        if (isExpanded)
        {
            if (property.objectReferenceInstanceIDValue != 0)
            {
                if (currentTierProp == null)
                    currentTierProp = property.serializedObject.FindProperty("_tier");
                if (currentTierProp != null)
                    currentTier = currentTierProp.intValue;
                theTierData = (BeaconTierData)property.objectReferenceValue;

                if (theTierData != null)
                {
                    if(totalTiers != theTierData.tiers)
                    {
                        totalTiers = theTierData.tiers;
                        Resize();
                        SetRects(position);
                    }
                    
                }
                
                theFloatFieldStyle.fontStyle = FontStyle.Normal;

                int currentLine = 1;
                theStyle.alignment = TextAnchor.MiddleCenter;

                //Loop and display the headers of each tier column, with hooks for the right click context menu
                for (int i = 0; i < totalTiers + 1; i++)
                {
                    if (i == 0)
                    {
                        continue;
                    }
                    else
                    {
                        if (i == currentTier)
                            theStyle.fontStyle = FontStyle.Bold;
                        Rect clickArea = SplitRectHorizontal(i, tierRects[currentLine], false);
                        EditorGUI.LabelField(clickArea, "Tier " + i.ToString(), theStyle);
                        theStyle.fontStyle = FontStyle.Normal;
                        
                        Event current = Event.current;
                        if (clickArea.Contains(current.mousePosition) && current.type == EventType.ContextClick)
                        {
                            //Show context menu

                            GenericMenu menu = new GenericMenu();

                            menu.AddItem(new GUIContent("Remove Tier"),false, theTierData.RemoveTier, i-1);
                            menu.AddItem(new GUIContent("Insert Tier After"), false, theTierData.InsertBefore, i);
                            menu.AddItem(new GUIContent("Insert Tier Before"), false, theTierData.InsertBefore, i-1);
                            menu.ShowAsContext();

                            current.Use();

                            EditorUtility.SetDirty(theTierData);
                            return;
                        }
                    }
                }

                //Split the provided rect into multiple rects for the number of tiers available, divide content 
                Rect buttonRect = SplitRectHorizontal(totalTiers+1, tierRects[currentLine], false);
                if(GUI.Button(buttonRect, "+"))
                {
                    theTierData.AppendEmpty();
                    EditorUtility.SetDirty(theTierData);
                    return;

                }
                theStyle.alignment = TextAnchor.MiddleRight;

                //Done dislaying the header and add buttons, now we display the actual array data
                currentLine++;
                DisplayArrayTier(theTierData, ref theTierData._max_energy_by_tier , "Max Energy ", currentLine);
                currentLine++;
                DisplayArrayTier(theTierData, ref theTierData._energy_transfer_rate_by_tier, "Transfer Rate ", currentLine);
                currentLine++;
                DisplayArrayTier(theTierData, ref theTierData._charge_radius_by_tier, "Charge Radius ", currentLine);
                currentLine++;
                DisplayArrayTier(theTierData, ref theTierData._solar_charge_rate_by_tier, "Solar Charge ", currentLine);
                EditorUtility.SetDirty(theTierData);
            }
        }
    }

    //Display the array horizontally with each entry in a sequential rect divided already in tierRect array
    private void DisplayArrayTier(BeaconTierData theTierData, ref List<float> theArray, string label, int currentLine)
    {
        EditorGUI.LabelField(SplitRectHorizontal(0, tierRects[currentLine], true), label, theStyle);

        for(int i = 0; i < theArray.Count; i++)
        {
            if (i+1 == currentTier)
                theFloatFieldStyle.fontStyle = FontStyle.Bold;
            theArray[i] = EditorGUI.FloatField(SplitRectHorizontal(i+1, tierRects[currentLine], false), theArray[i], theFloatFieldStyle);
            theFloatFieldStyle.fontStyle = FontStyle.Normal;
        }

    }

    //Divide the rect into two parts, with variable width depending on the isLabel bool
    private Rect SplitObjectFoldoutLine(Rect theLineRect, bool isLabel)
    {
        Rect retRect = new Rect();
        if (isLabel)
        {
            //label rect is double the width of edit rects
            retRect.Set(theLineRect.position.x, theLineRect.position.y, rectWidth * 2.5f, singleRectHeight);
        }
        else
        {
            
            retRect.Set(theLineRect.position.x + rectWidth * 2.5f + horizontalSpacing, theLineRect.position.y, theLineRect.width - rectWidth * 2.5f - horizontalSpacing, singleRectHeight);
        }
        return retRect;
    }

    //Split the rect passed in into multiple edit box rectangles for the displaying of the array values
    private Rect SplitRectHorizontal(int currentTier, Rect theLineRect, bool isLabel)
    {
        Rect retRect = new Rect();
        if(isLabel)
        {
            //label rect is double the width of edit rects
            retRect.Set(theLineRect.position.x, theLineRect.position.y, rectWidth * 2.5f, singleRectHeight);
        }
        else
        {
            retRect.Set(theLineRect.position.x + rectWidth*2.5f + rectWidth * (currentTier-1) + (currentTier * horizontalSpacing), theLineRect.position.y, rectWidth, singleRectHeight);
        }
        return retRect;
    }

    //Recalculate the rects used to display data
    private void SetRects(Rect position)
    {
        for (int i = 0; i < tierRects.Length; i++)
        {
            if(i == 0)
                tierRects[i].Set(position.x, position.y, position.size.x, EditorGUIUtility.singleLineHeight);
            else
            {
                tierRects[i].Set(position.x, tierRects[i-1].position.y + tierRects[i - 1].size.y + EditorGUIUtility.standardVerticalSpacing, position.size.x, EditorGUIUtility.singleLineHeight);
            }

        }
    }


    private void Resize()
    {
        //This needs to be expanded further, to support dynamic addition / removal of horizontal arrays we are displaying. Hard code for now.
        tierRects = new Rect[6];

    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        
        if(isExpanded && property.objectReferenceInstanceIDValue != 0 && property != null)
        {
            totalTiers = ((BeaconTierData)property.objectReferenceValue).tiers;
            return 6 * EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * totalTiers;
        }
        else
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
