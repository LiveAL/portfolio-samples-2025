using Ink.Runtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DialogueDisplay : ConversationDisplay
{
    #region Name plaque variables
    [Header("Character Name")]
    [SerializeField, Tooltip("Parent for plaque.")]
    private GameObject _plaque;
    [SerializeField, Tooltip("Text for character name.")]
    private TMP_Text _plaqueText;
    #endregion

    #region Portrait variables
    [Header("Portraits")]
    [SerializeField, Tooltip("Spawnable prefab for portrait.")]
    private GameObject _portraitPrefab;
    [SerializeField, Tooltip("Parent for portrait objects.")]
    private Transform _portraitTray;

    [SerializeField, Tooltip("Size of portrait when emphasized.")]
    private float _emphasizeSize = 0.3f;
    [SerializeField, Tooltip("Size of portrait when de-emphasized.")]
    private float _deemphasizeSize = 0.2f;
    [SerializeField, Tooltip("Color overlay for de-emphasized sprites.")]
    private Color _deemphasizedColor;
    private const string DEEMPHASIZE_TAG = "DEEMPHASIZED";
    #endregion

    #region Location variables
    [Header("Locations")]
    [SerializeField, Tooltip("Background UI image.")] 
    private Image _backgroundImage;
    [SerializeField, Tooltip("Data container for backgrounds.")] 
    private ConversationLocations _locations;
    #endregion

    [Header("Dialogue")]
    [SerializeField, Tooltip("Text field for body text.")] 
    private TMP_Text _bodyText;

    // Tracks active characters to ensure no repeating portraits
    private Dictionary<ConversationCharacter, GameObject> _activePortraits = new Dictionary<ConversationCharacter, GameObject>();

    // Skip scrawling body text and show entire contents immediately
    bool isSkippingScrawl = false;

    /// <summary>
    /// Set current Ink story
    /// </summary>
    public override void SetStory(Story story)
    {
        base.SetStory(story);

        _bodyText.text = "";
    }

    /// <summary>
    /// Display next line in the story
    /// </summary>
    public override void ContinueToNextLine()
    {
        if (story.currentChoices.Count > 0 && yieldPreChoiceLine)
        { // Player is not currently on choice selection or the prompt line for a choice selection
            LogCurrentLine();

            DisplayChoices();
            yieldPreChoiceLine = false;
        }
        else
        { // No choices to wait for, continue
            if (choiceScroll.childCount > 0) // Remove choices if they are displayed
            {
                DestroyChoices();
            }
            else
            {
                LogCurrentLine();
            }

            UpdateDialogueBody();

            // Hit choices, wait for choice display before allowing story continuation
            if (story.currentChoices.Count > 0)
            {
                yieldPreChoiceLine = true;
            }
        }
    }

    /// <summary>
    /// Update the dialogue text
    /// </summary>
    public override void UpdateDialogueBody()
    {
        _bodyText.text = "";

        try
        {
            ParseLineText();

            StartCoroutine(ExecuteDialogueTimeline());
        }
        catch (System.Exception e)
        {
            Debug.LogError("Markup incorrect: " + e.Message);

            // Just display the line with errors, better to see something than nothing
            _bodyText.text = story.currentText;

            isSkippingScrawl = false;
            OnVisualizeComplete.Invoke();
        }
    }

    /// <summary>
    /// Separate markup commands and body text into a timeline
    /// </summary>
    protected override void ParseLineText()
    {
        string storyText = story.currentText;
        storyText = storyText.Trim();

        currentLine = new ConversationLine();

        // Get speaker if a speaker tag is specified
        if (TryGetCharacterFromText(storyText, out ConversationCharacter character))
        {
            currentLine.speaker = character;

            RemoveSpeechTagFromLine(ref storyText);
        }

        PlaqueUpdate();

        // Split timeline for markup cues
        Dictionary<Markup, string> splitTimeline = new Dictionary<Markup, string>();

        // Replace player name placeholder
        ReplacePlayerName(ref storyText);

        // Separate out timeline of this dialogue 
        bool foundLineEnd = false;
        while (!foundLineEnd)
        {
            // Get first markup character
            int firstMarkup = storyText.IndexOf(MARKUP);

            if (firstMarkup == -1) // No more markup found, end parse
            {
                // Add the rest of the line to timeline as regular body text
                currentLine.timeline.Add(new Cue(Markup.DEFAULT, storyText));

                foundLineEnd = true;
            }
            else
            {
                // Add preceding default dialogue from before markup cue to timeline as regular body text
                string prev = storyText.Substring(0, firstMarkup);
                if (prev.Length > 0)
                {
                    currentLine.timeline.Add(new Cue(Markup.DEFAULT, prev));
                }

                // Find next markup indicator character
                string next = storyText.Substring(firstMarkup + 1);
                int lastMarkup = next.IndexOf(MARKUP); // End of markup tag

                if (lastMarkup == -1) // Markup tag was not properly ended or reserved character used in regular dialogue
                {
                    throw new System.Exception("Markup end not found.");
                }

                // Get type of markup command
                int markupCommandEnd = next.IndexOf(MARKUP_SEPARATOR); // End of the markup type
                if (markupCommandEnd == -1) // Command has no extra specifiers
                {
                    markupCommandEnd = lastMarkup;
                }

                // Separate extra specifying text for what markup command should do
                string markup = next.Substring(0, markupCommandEnd);
                markup = markup.ToUpper();

                if (System.Enum.TryParse(markup, out Markup type))
                { // Markup command matches available commands
                    string fullMarkup = next.Substring(markupCommandEnd + 1, lastMarkup - markupCommandEnd - 1); // Text between command and end of markup tag

                    // Add markup type and specifiers to timeline
                    currentLine.timeline.Add(new Cue(type, fullMarkup));

                    if (next.Length < lastMarkup + 1)
                    { // Markup was end of line
                        foundLineEnd = true;
                    }
                    else
                    { // Remove markup tag and continue
                        storyText = next.Substring(lastMarkup + 1);
                    }
                }
                else
                {
                    throw new System.Exception("Markup command not recognized. You tried: " + markup);
                }
            }
        }
    }

    /// <summary>
    /// Run dialogue timeline
    /// </summary>
    private IEnumerator ExecuteDialogueTimeline()
    {
        foreach (var cue in currentLine.timeline)
        {
            switch (cue.markup)
            {
                case Markup.SPRITE: // Add or update portrait
                    ConversationCharacter character;
                    GameObject portraitObject;

                    try
                    {
                        bool isEmphasized = GetEmphasis(cue.text, out int nextIndexAfterEmphasis);
                        string markupText = cue.text.Substring(nextIndexAfterEmphasis);

                        Sprite portrait = GetPortraitSprite(markupText, out character);

                        // Add new portrait if not yet created
                        if (!_activePortraits.TryGetValue(character, out portraitObject))
                        {
                            portraitObject = Instantiate(_portraitPrefab, _portraitTray);
                            _activePortraits.Add(character, portraitObject);
                        }

                        // Replace portrait sprite image
                        Image spriteObject = portraitObject.GetComponentInChildren<Image>();
                        spriteObject.sprite = portrait;

                        ChangePortraitEmphasis(spriteObject.gameObject, isEmphasized);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Portrait will not be added:" + e.Message);
                    }

                    break;

                case Markup.SPRITEREMOVE: // Remove portrait
                    try
                    {
                        character = GetCharacterFromMarkup(cue.text);

                        if (_activePortraits.TryGetValue(character, out portraitObject))
                        {
                            Destroy(portraitObject);
                            _activePortraits.Remove(character);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Portrait will not be removed: " + e.Message);
                    }
                    
                    break;

                case Markup.SPRITEEMPHASIZE: // Emphasize portrait 
                    try
                    {
                        ChangePortraitEmphasis(cue, true);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Portrait will not be emphasized: " + e.Message);
                    }

                    break;

                case Markup.SPRITEDEEMPHASIZE: // Deemphasize portrait
                    try
                    {
                        ChangePortraitEmphasis(cue, false);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Portrait will not be deemphasized: " + e.Message);
                    }

                    break;

                case Markup.LOCATION: // Update location background
                    try
                    {
                        Sprite location = GetLocationFromMarkup(cue.text);
                        _backgroundImage.sprite = location;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Location will not be added: " + e.Message);
                    }

                    break;

                case Markup.KEY: // Next dialogue or gameplay scene requested by key
                    LevelManager.instance.LoadKey(cue.text);
                    break;

                case Markup.DEFAULT: // Regular body text expected
                    if (isSkippingScrawl) // Display all text in this timeline cue immediately
                    {
                        _bodyText.text += cue.text;
                    }
                    else // Add text char by char
                    {
                        for (int i = 0; i < cue.text.Length; i++)
                        {
                            // If regular text markup found (italic, underline, etc.), add entire specifier 
                            if (cue.text[i] == BODY_TEXT_MARKUP)
                            {
                                // Get end of markup
                                int markupEnd = cue.text.IndexOf(BODY_TEXT_MARKUP_END);

                                // Ensure markup end is within the expected tag range
                                if (markupEnd < i + 4)
                                {
                                    // Add tag, update index to end
                                    _bodyText.text += cue.text.Substring(i, markupEnd);
                                    i = markupEnd + 1;
                                }
                            }
                            else
                            { // No text markup found, continue normally

                                _bodyText.text += cue.text[i];
                            }

                            if (isSkippingScrawl) // Add rest of text immediately
                            {
                                _bodyText.text += cue.text.Substring(i + 1);
                                break;
                            }
                            else
                            {
                                yield return new WaitForSeconds(0.02f);
                            }
                        }
                    }
                    
                    break;
            }
        }

        isSkippingScrawl = false;

        if (_bodyText.text.Length == 0)
        {
            OnCommandOnlyComplete.Invoke();
            yield break;
        }

        OnVisualizeComplete.Invoke();
        yield break;
    }

    /// <summary>
    /// Update the current speaker plaque
    /// </summary>
    private void PlaqueUpdate()
    {
        if (currentLine.speaker)
        {
            _plaqueText.text = currentLine.speaker.displayName;
            _plaque.SetActive(true);
        }
        else
        {
            _plaque.SetActive(false);
            _plaqueText.text = "";
        }
    }

    /// <summary>
    /// Get sprite from markup ID
    /// </summary>
    /// <param name="character">Character associated with this portrait</param>
    private Sprite GetPortraitSprite(string markupTag, out ConversationCharacter character)
    {
        character = GetCharacterFromMarkup(markupTag);

        //Isolate portrait ID from character tag
        int characterEnd = markupTag.IndexOf(MARKUP_SEPARATOR);
        string trySprite = markupTag.Substring(characterEnd + 1);
        trySprite = trySprite.ToUpper();

        for (int i = 0; i < character.portraits.Length; i++) // Get sprite ID match
        {
            if (character.portraits[i].ID.ToUpper() == trySprite) // Sprite match
            {
                return character.portraits[i].sprite;
            }
        }

        throw new System.Exception("Portrait: " + trySprite + " not found. Ensure portrait ID is correct.");
    }

    /// <summary>
    /// Should this sprite be emphasized?
    /// </summary>
    private bool GetEmphasis(string markupTag, out int nextIndexAfterEmphasis)
    {
        nextIndexAfterEmphasis = 0;

        int delimiterIndex = markupTag.IndexOf(MARKUP_SEPARATOR);
        if (delimiterIndex == -1) // No end of delimiter, sprite has no special deemphasis command.
        {
            return true;
        }

        string tryEmphasis = markupTag.Substring(0, delimiterIndex);

        if (tryEmphasis.ToUpper() == DEEMPHASIZE_TAG)
        {
            nextIndexAfterEmphasis = delimiterIndex + 1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Change the size of a portrait from a cue
    /// </summary>
    private void ChangePortraitEmphasis(Cue cue, bool isEmphasized)
    {
        ConversationCharacter character = GetCharacterFromMarkup(cue.text);

        if (_activePortraits.TryGetValue(character, out GameObject portraitObject))
        {
            ChangePortraitEmphasis(portraitObject, isEmphasized);
        }
    }

    /// <summary>
    /// Change the size of a portrait with the game object
    /// </summary>
    /// <param name="portraitObject">Game object with the sprite transform</param>
    private void ChangePortraitEmphasis(GameObject portraitObject, bool isEmphasized)
    {
        GameObject spriteObject = portraitObject.GetComponentInChildren<Image>().gameObject;
        spriteObject.GetComponent<RectTransform>().localScale = Vector3.one * (isEmphasized ? _emphasizeSize : _deemphasizeSize);
        spriteObject.GetComponent<Image>().color = (isEmphasized ? Color.white : _deemphasizedColor);
    }

    /// <summary>
    /// Get character ID in markup command
    /// </summary>
    private ConversationCharacter GetCharacterFromMarkup(string markupTag)
    {
        // Isolate character ID
        int characterEnd = markupTag.IndexOf(MARKUP_SEPARATOR); // Markup continues after character ID
        string tryCharacter = "";

        if (characterEnd == -1) // Markup tag is only character ID
        {
            tryCharacter = markupTag;
        }
        else // Markup tag contains other information. Isolate.
        {
            tryCharacter = markupTag.Substring(0, characterEnd);
        }

        tryCharacter = tryCharacter.ToUpper();

        foreach (var testCharacter in characters.characters)
        {
            if (testCharacter.name.ToUpper() == tryCharacter) // Found character ID match
            {
                return testCharacter;
            }
        }

        throw new System.Exception("Character: " + tryCharacter + " not found. Ensure character object exists and has been added to characters.");
    }

    /// <summary>
    /// Retrieve sprite for location in markup
    /// </summary>
    private Sprite GetLocationFromMarkup(string markupTag)
    {
        foreach (var location in _locations.locations)
        {
            if (location.name.ToUpper() == markupTag.ToUpper())
            {
                return location.sprite;
            }
        }

        throw new System.Exception("Location: " + markupTag + " not found. Ensure location ID is correct");
    }


    /// <summary>
    /// Display choice buttons
    /// </summary>
    public override void DisplayChoices()
    {
        _bodyText.text = "";

        base.DisplayChoices();
    }

    /// <summary>
    /// Process any updates needed on exiting a choice gate
    /// </summary>
    public override void ProcessFinalChoice(string choiceText)
    {
        // Add choice text to current timeline so log can grab it
        ConversationLine conversationLine = new ConversationLine();
        conversationLine.speaker = playableCharacter;
        conversationLine.timeline.Add(new Cue(Markup.DEFAULT, choiceText));

        // Log choice text
        LogDisplay.Instance.AddConversationLine(conversationLine);
    }

    /// <summary>
    /// Skip to end of the line
    /// </summary>
    public override void SkipDeploymentToEnd()
    {
         isSkippingScrawl = true;
    }

    /// <summary>
    /// Log the current line in the conversation log
    /// </summary>
    private void LogCurrentLine()
    {
        if (currentLine != null && _bodyText.text != "")
        {
            LogDisplay.Instance.AddConversationLine(currentLine);
        }
    }
}