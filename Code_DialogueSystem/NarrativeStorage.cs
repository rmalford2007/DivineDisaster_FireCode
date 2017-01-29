using UnityEngine;
using System.Collections;
using DialogueHelpers;
using System.Collections.Generic;

/********************************************
 * Narrative Storage holds all the narrative for the game, or a level
 * 
 * Holds a list of books - A book should be thought of as all the conversations in an area of a game
 * Each book has a list of chapters - A chapter should be thought of as single side of a conversation
 * Each chapter has a list of pages - A page should be thought of as sections of sequential text. Only having room in your dialogue to read a few sentences at a time is a page, each page has a read time before flipping to the next page. 
 * 
 * Chapters are linked together in the dialogues to create conversations. Chapter 1 will be shown, then chapter 2, then chapter 3 could be a list of choices for the player to pick
 * *****************************************/
public class NarrativeStorage : MonoBehaviour {

    public List<StoryBook> allNarratives;

	// Use this for initialization
	void Start () {
        Initialize();
    }
	
	// Update is called once per frame
	void Update () {
	
	}

    public void Initialize()
    {
        //Debug.Log("Start narrative");
        allNarratives = new List<StoryBook>();
        BuildExampleNarratives();
        BuildNarrative_OrphanageScene();
    }

    void BuildExampleNarratives()
    {
        StoryBook nextBook = new StoryBook("Test Room");
        {
            //All objects in a room get separate chapters
            //if an object can have more than 1 type of conversation in this room, it gets multiple chapters on separate dialogues
            StoryChapter nextChapter = NewChapter(1); //dialogue ID is passed in here
            {
                //Define all the pages this chapter (dialogue) will flip through along with its read time
                nextChapter.pages.Add(NewPage("Dialogue 1 Page 1 of new narrative, 2 seconds", 2.0f));
                nextChapter.pages.Add(NewPage("Dialogue 1 Page 2 of new narrative, 1 seconds", 1.0f));
            }
            //add the chapter to the book
            nextBook.chapters.Add(nextChapter);

            nextChapter = NewChapter(2);
            {
                //Define all the pages this chapter (dialogue) will flip through along with its read time
                nextChapter.pages.Add(NewPage("Dialogue 2 Page 1 of new narrative, 1 seconds", 1.0f));
                nextChapter.pages.Add(NewPage("Dialogue 2 Page 2 of new narrative, 2 seconds", 2.0f));
                nextChapter.pages.Add(NewPage("Dialogue 2 Page 3 of new narrative, 1 seconds", 1.0f));
            }
            //add the chapter to the book
            nextBook.chapters.Add(nextChapter);
        }
        //add the book to the narrative
        allNarratives.Add(nextBook);

        
        
    }

    void BuildNarrative_OrphanageScene()
    {
        StoryBook nextBook = new StoryBook("Orphanage Scene");
        {
            //Snoring Cubie conversation
            {
                StoryChapter nextChapter = NewChapter(100); //Snoring Cubie
                {
                    nextChapter.pages.Add(NewPage("zzzzzZZZZZ... ", 2.0f));
                    nextChapter.pages.Add(NewPage("Stay away... no... salad", 2.0f));
					nextChapter.pages.Add(NewPage("Can't help it... allergic...", 2.0f));
                }
                nextBook.chapters.Add(nextChapter);

				nextChapter = NewChapter(101); //Boxii response
                {
                    nextChapter.pages.Add(NewPage("He must be dreaming about dinner earlier.", 3.0f));
					//nextChapter.pages.Add(NewPage("Maybe not.", 2.0f));
                }
                nextBook.chapters.Add(nextChapter);
            }
     //***OBJECT POP-UPS***
        //**DORMITORY**
			//*Loud Speakers
			{
				StoryChapter nextChapter = NewChapter(200); //Boxii Comment 1
				{
					nextChapter.pages.Add(NewPage("Ms. Mahble delivers daily words of encouragement through these.", 4.0f));
				}
				nextBook.chapters.Add(nextChapter);

				nextChapter = NewChapter(201); //Boxii Comment 2
                {
					nextChapter.pages.Add(NewPage("Things like, “A good cubie is an obedient cubie.”", 4.0f));
				}
				nextBook.chapters.Add(nextChapter);
			}
            //*Food
            {
                StoryChapter nextChapter = NewChapter(202); //Boxii Comment 1
                {
                    nextChapter.pages.Add(NewPage("Spheres claim that this is food, but I doubt they’ve tried it.", 4.0f));
                }
                nextBook.chapters.Add(nextChapter);

                nextChapter = NewChapter(203); //Boxii Comment 2
                {
                    nextChapter.pages.Add(NewPage("Flies sure seem to like it, though.", 4.0f));
                }
                nextBook.chapters.Add(nextChapter);
            }
            //*Tissue Box
            {
                StoryChapter nextChapter = NewChapter(204); //Boxii Comment 1
                {
                    nextChapter.pages.Add(NewPage("The box reads, ‘At Osnosis, we pride ourselves on a 99.98% absorption rate of most nasal afflictions.", 5.0f));
                }
                nextBook.chapters.Add(nextChapter);            
            }
            //*Food Trays
            {
                StoryChapter nextChapter = NewChapter(205); //Boxii Comment 1
                {
                    nextChapter.pages.Add(NewPage("If Ms. Mahble can’t see her reflection in these, you’re going to get a timeout.", 4.0f));
                }
                nextBook.chapters.Add(nextChapter);
            }
            //*Food Chute
            {
                StoryChapter nextChapter = NewChapter(206); //Boxii Comment 1
                {
                    nextChapter.pages.Add(NewPage("This dispenses three square meals a day.", 3.0f));
                }
                nextBook.chapters.Add(nextChapter);

                nextChapter = NewChapter(207); //Boxii Comment 2
                {
                    nextChapter.pages.Add(NewPage("Cold air is coming out of the opening.", 3.0f));
                }
                nextBook.chapters.Add(nextChapter);
            }
        }
        //add the book to the narrative
        allNarratives.Add(nextBook);
    }

    StoryChapter NewChapter(int dialogueID)
    {
        return new StoryChapter(dialogueID);
    }

    StoryPage NewPage(string text, float readTime)
    {
        return new StoryPage(text, readTime);
    }

    public StoryChapter FindChapter(int prDialogueID)
    {
        if (allNarratives != null)
        {
            for (int i = 0; i < allNarratives.Count; i++)
            {
                StoryBook nextBook = allNarratives[i];
                if (nextBook != null)
                {
                    for (int x = 0; x < nextBook.chapters.Count; x++)
                    {
                        if (prDialogueID == nextBook.chapters[x].dialogueID)
                        {
                            return nextBook.chapters[x];
                        }
                    }
                }
            }
        }

        return null;
    }
}
