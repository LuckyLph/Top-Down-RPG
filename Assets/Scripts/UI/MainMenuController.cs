using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;

[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Selectable firstSelected;

    private GameFlow gameFlow;

    [Inject]
    public void Construct(GameFlow flow)
    {
        gameFlow = flow;
    }

    private void Start()
    {
        // Lets keyboard and gamepad navigate the menu without a mouse click first.
        if (firstSelected != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
        }
    }

    // Wired to the New Game button.
    public void StartNewGame()
    {
        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(MainMenuController)} was not injected; load MainMenu through the Main scene.", this);
            return;
        }

        _ = gameFlow.StartNewGameAsync();
    }

    // Wired to the Quit button.
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
