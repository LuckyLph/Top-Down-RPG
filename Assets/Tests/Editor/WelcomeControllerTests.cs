using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the first run's Welcome panel (<see cref="WelcomeController"/>, Docs/UiRevamp.md, Phase 11) on the real
/// prefab over the menu rig on a 1920 x 1080 display: shown only before the first run is over; the three rows with their
/// values, Reset gone; the UI size stepping through the sizes that fit and the toggles flipping, each saved at once; the
/// focused row described; looping navigation; and Continue or Back marking the first run over.
/// </summary>
public class WelcomeControllerTests
{
    private const string PanelPath = "Assets/Prefabs/UI/Welcome.prefab";

    private MenuTestRig rig;
    private WelcomeView view;
    private WelcomeController controller;
    private int closes;

    /// <summary>
    /// Builds the rig in its first run and a controller over an instance of the panel in a 640 x 360 UI px parent.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        rig = new MenuTestRig(false);
        GameObject parent = rig.Track(new GameObject("Canvas", typeof(RectTransform)));
        ((RectTransform)parent.transform).sizeDelta = new Vector2(640f, 360f);
        GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath), parent.transform);
        view = panel.GetComponent<WelcomeView>();
        controller = rig.CreateWelcome(view);
        closes = 0;
    }

    /// <summary>
    /// Disposes the controller and the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        rig.Dispose();
    }

    /// <summary>
    /// Opening shows the panel with the rows' values and no Reset, selects the UI size and describes it, and pushes a
    /// menu layer; it shows only while the first run is not over.
    /// </summary>
    [Test]
    public void Opening_ShowsTheRows_AndSelectsTheUiSize()
    {
        Assert.That(controller.ShouldShow, Is.True);
        Open();

        Assert.That(view.IsShown, Is.True);
        Assert.That(view.UiSizeRow.BindingText.text, Is.EqualTo("Normal"));
        Assert.That(view.ReduceFlashingRow.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
        Assert.That(view.ReduceMotionRow.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
        Assert.That(view.UiSizeRow.Label.text, Is.EqualTo(OptionsMenuController.UiSizeLabel));
        Assert.That(view.UiSizeRow.IsResetShown || view.ReduceFlashingRow.IsResetShown || view.ReduceMotionRow.IsResetShown, Is.False);
        Assert.That(rig.Selection.Current, Is.EqualTo(view.UiSizeRow.BindingButton.gameObject));
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.UiSizeDescription));
        Assert.That(rig.Menus.Top, Is.SameAs(controller));
    }

    /// <summary>
    /// The UI size steps through the sizes the display fits, back to Normal; the toggles flip and show their mark; each
    /// change is saved at once.
    /// </summary>
    [Test]
    public void TheRows_ChangeAndSaveTheSettingsAtOnce()
    {
        Open();
        Assert.That(rig.Scale.StepsThatFit, Is.EqualTo(1), "A 1080p display fits Large.");

        view.UiSizeRow.BindingButton.onClick.Invoke();
        Assert.That(rig.Settings.UiSizeStep, Is.EqualTo(1));
        Assert.That(view.UiSizeRow.BindingText.text, Is.EqualTo("Large"));
        view.UiSizeRow.BindingButton.onClick.Invoke();
        Assert.That(rig.Settings.UiSizeStep, Is.Zero, "Larger does not fit 1080p.");

        view.ReduceFlashingRow.BindingButton.onClick.Invoke();
        view.ReduceMotionRow.BindingButton.onClick.Invoke();
        Assert.That(rig.Settings.ReduceFlashing && rig.Settings.ReduceMotion, Is.True);
        Assert.That(view.ReduceFlashingRow.BindingText.text, Is.EqualTo(OptionsMenuController.OnName));
        Assert.That(view.ReduceFlashingRow.ToggleMark.enabled, Is.True);
    }

    /// <summary>
    /// Up and Down move along the rows and Continue, looping; moving onto a row describes it, and Continue has no
    /// description.
    /// </summary>
    [Test]
    public void TheFocus_LoopsThroughTheRowsAndContinue_DescribingEach()
    {
        Open();
        Selectable[] order = { view.UiSizeRow.BindingButton, view.ReduceFlashingRow.BindingButton, view.ReduceMotionRow.BindingButton, view.ContinueButton };
        for (int i = 0; i < order.Length; i++)
        {
            Assert.That(order[i].navigation.selectOnDown, Is.EqualTo(order[(i + 1) % order.Length]), $"Down from {order[i].name}");
            Assert.That(order[i].navigation.selectOnUp, Is.EqualTo(order[(i + order.Length - 1) % order.Length]), $"Up from {order[i].name}");
        }

        rig.Selection.Select(view.ReduceMotionRow.BindingButton);
        controller.Tick();
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.ReduceMotionDescription));
        rig.Selection.Select(view.ContinueButton);
        controller.Tick();
        Assert.That(view.Description.text, Is.Empty);
    }

    /// <summary>
    /// Continue marks the first run over in the file, hides the panel and tells its opener; Back (Escape) does the same,
    /// keeping what was set.
    /// </summary>
    [Test]
    public void ContinueOrBack_MarkTheFirstRunOver_AndClose()
    {
        Open();
        view.ContinueButton.onClick.Invoke();

        Assert.That(rig.Settings.FirstRunDone, Is.True);
        Assert.That(controller.ShouldShow, Is.False);
        Assert.That(view.IsShown, Is.False);
        Assert.That(closes, Is.EqualTo(1));
        Assert.That(rig.Menus.Top, Is.Null);

        controller.Dispose();
        MenuTestRig other = new(false);
        try
        {
            WelcomeController second = other.CreateWelcome(view);
            second.Open(() => closes++);
            view.ReduceMotionRow.BindingButton.onClick.Invoke();
            other.MenuInput.BackPressedThisFrame = true;
            other.Menus.Tick();
            Assert.That(other.Settings.FirstRunDone && other.Settings.ReduceMotion, Is.True, "Escape accepts what is set.");
            Assert.That(closes, Is.EqualTo(2));
            second.Dispose();
        }
        finally
        {
            other.Dispose();
        }
    }

    /// <summary>
    /// Opens the panel, counting its closings.
    /// </summary>
    private void Open()
    {
        controller.Open(() => closes++);
    }
}
