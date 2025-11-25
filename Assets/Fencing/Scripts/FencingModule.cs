using Assets.Fencing.Scripts.Enums;
using Assets.Fencing.Scripts.Extensions;
using Assets.Fencing.Scripts.Models;
using Assets.Fencing.Scripts.Rules;
using Assets.Fencing.Scripts.Services;
using Assets.Scripts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class FencingModule : MonoBehaviour
{
    public KMSelectable Salute;
    public KMSelectable Wait;
    public KMSelectable Parry;
    public KMSelectable Follow;
    public KMSelectable Head;
    public KMSelectable Hand;
    public KMSelectable Torso;

    public TextMesh Timer;
    public TextMesh LeftScore;
    public TextMesh RightScore;

    public GameObject OpponentParry;
    public GameObject OpponentForward;
    public GameObject OpponentBackward;
    public GameObject OpponentStatic;

    public GameObject Epee;
    public GameObject Foil;
    public GameObject Sabre;

    public GameObject Piste;

    public MaterialStore MaterialStore;
    public FencingLightsManager FencingLightsManager;

    public KMBombModule BombModule;
    public KMAudio Audio;

    private bool isActivated = false;
    private bool isSolved = false;
    private ScenarioGenerator scenarioGenerator;
    private Solver solver;
    private Scenario currentScenario;
    private Rule currentMatchingRule;
    private List<ActionKeys> expectedKeyPresses = new List<ActionKeys>();

    private void Start()
    {
        SetUpServices();
        InitialiseBombModule();
        InitialiseInteractionHandlers();
        currentScenario = scenarioGenerator.GetScenario();
        SetUpForScenario(currentScenario);
    }

    private void InitialiseInteractionHandlers()
    {
        Salute.OnInteract += GenerateInteractionHandler(Salute, ActionKeys.Salute);
        Wait.OnInteract += GenerateInteractionHandler(Wait, ActionKeys.Wait);
        Parry.OnInteract += GenerateInteractionHandler(Parry, ActionKeys.Parry);
        Follow.OnInteract += GenerateInteractionHandler(Follow, ActionKeys.Follow);
        Head.OnInteract += GenerateInteractionHandler(Head, ActionKeys.HitHead);
        Hand.OnInteract += GenerateInteractionHandler(Hand, ActionKeys.HitHand);
        Torso.OnInteract += GenerateInteractionHandler(Torso, ActionKeys.HitTorso);
    }

    private void SetUpServices()
    {
        scenarioGenerator = new ScenarioGenerator();
        solver = new Solver();
    }

    private void InitialiseBombModule()
    {
        BombModule.GenerateLogFriendlyName();
        BombModule.OnActivate += () => isActivated = true;
    }

    private KMSelectable.OnInteractHandler GenerateInteractionHandler(KMSelectable selectable, ActionKeys pressedKey)
    {
        return () =>
            {
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, selectable.transform);
                selectable.AddInteractionPunch();
                HandlePressedKey(pressedKey);
                return false;
            };
    }

    private void HandlePressedKey(ActionKeys pressedKey)
    {
        if (!isActivated)
        {
            BombModule.HandleStrike();
            return;
        }

        if (isSolved)
        {
            return;
        }

        if (!expectedKeyPresses.Any())
        {
            BombModule.Log("ERROR: module not solved but no keypress expected !");
        }

        if (expectedKeyPresses[0] == pressedKey)
        {
            HandleCorrectKeyPress();
        }
        else
        {
            HandleIncorrectKeyPress(pressedKey);
        }
    }

    private void HandleCorrectKeyPress()
    {
        ActionKeys pressedButton = expectedKeyPresses[0];
        BombModule.LogFormat("Correctly pressed {0}", pressedButton);
        expectedKeyPresses.RemoveAt(0);

        if (expectedKeyPresses.Any())
        {
            LogNextExpectedKeyPress();
            return;
        }

        FencingLightsManager.HandleButtonPress(pressedButton, currentScenario);

        if (!currentMatchingRule.LastStage)
        {
            BombModule.Log("New Scenario needed");
            StartCoroutine(DeactivateTemporarily(Timer.gameObject));
            SetUpForScenario(scenarioGenerator.GetScenario(currentScenario));
            return;
        }

        BombModule.Log("Module solved !");
        isSolved = true;
        BombModule.HandlePass();
    }

    private void SetUpForScenario(Scenario scenario)
    {
        currentScenario = scenario;
        currentScenario.Log(BombModule);
        ApplyScenario(currentScenario);

        currentMatchingRule = solver.Solve(currentScenario);
        expectedKeyPresses = currentMatchingRule.Solution.ToList();
        currentMatchingRule.Log(BombModule);
        LogNextExpectedKeyPress();
    }

    private void HandleIncorrectKeyPress(ActionKeys pressedKey)
    {
        BombModule.LogFormat("Incorrectly pressed {0} when {1} was expected", pressedKey, expectedKeyPresses[0]);
        BombModule.HandleStrike();
    }

    private void ApplyScenario(Scenario scenario)
    {
        Timer.text = scenario.MatchTimer.MinutesAndSecondsFormat();

        OpponentBackward.SetActive(scenario.OpponentAction == OpponentAction.Backward);
        OpponentForward.SetActive(scenario.OpponentAction == OpponentAction.Forward);
        OpponentParry.SetActive(scenario.OpponentAction == OpponentAction.Parry);
        OpponentStatic.SetActive(scenario.OpponentAction == OpponentAction.Static);

        RightScore.text = scenario.OpponentScore.ToString();
        LeftScore.text = scenario.OwnScore.ToString();

        Epee.SetActive(scenario.Weapon == Weapon.Epee);
        Foil.SetActive(scenario.Weapon == Weapon.Foil);
        Sabre.SetActive(scenario.Weapon == Weapon.Sabre);

        for (int i = 0; i < Piste.transform.childCount; ++i)
        {
            Piste.transform.GetChild(i).GetComponent<MeshRenderer>().material
                = GetPisteSectionFromIndex(i) == scenario.PisteSection
                ? MaterialStore.PisteLit
                : MaterialStore.Piste;
        }
    }

    private IEnumerator DeactivateTemporarily(GameObject gameObject)
    {
        MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
        Material initialMaterial = renderer.material;

        gameObject.SetActive(false);
        yield return new WaitForSeconds(.5f);
        gameObject.SetActive(true);
    }

    private void LogNextExpectedKeyPress()
    {
        BombModule.LogFormat("Next expected key press: {0}", expectedKeyPresses[0]);
    }

    private PisteSection GetPisteSectionFromIndex(int index)
    {
        return (PisteSection)index;
    }

    public string TwitchManualCode = "Fencing";
    public string TwitchHelpMessage = "Press a key with \"!{0} press key(s)\"  where key can be: salute, wait, parry, follow, hand, head or torso. e.g. \"!{0} press parry torso\"";

    public KMSelectable[] ProcessTwitchCommand(string command)
    {
        string[] commandParts = command.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if(commandParts.Length < 2
            || (commandParts[0] != "press"))
        {
            throw new FormatException("The command has to start with press and have at least one key name.");
        }

        List<KMSelectable> buttonsToPress = new List<KMSelectable>();

        for(int i=1; i < commandParts.Length; ++i)
        {
            string buttonName = commandParts[i];
            switch (buttonName)
            {
                case "salute": buttonsToPress.Add(Salute); break;
                case "wait": buttonsToPress.Add(Wait); break;
                case "parry": buttonsToPress.Add(Parry); break;
                case "follow": buttonsToPress.Add(Follow); break;
                case "hand": buttonsToPress.Add(Hand); break;
                case "head": buttonsToPress.Add(Head); break;
                case "torso": buttonsToPress.Add(Torso); break;
                default: throw new FormatException("{0} is not a valid key name."); ;
            }
        }

        return buttonsToPress.ToArray();
    }
}