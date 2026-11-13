using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="TitleViewRecipe"/>: its settings and Generate, which writes the logo and then renders the
/// title view through <see cref="TitleViewWriter"/>, after offering to save the open scenes, which the render replaces
/// for a few editor updates.
/// </summary>
[CustomEditor(typeof(TitleViewRecipe))]
public sealed class TitleViewRecipeEditor : Editor
{
    /// <summary>
    /// Draws the settings and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        TitleViewRecipe recipe = (TitleViewRecipe)target;
        bool canGenerate = recipe.CanGenerate(out string problem);
        if (!canGenerate)
        {
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate") && UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                if (TitleViewWriter.WriteLogo(recipe))
                {
                    TitleViewWriter.WriteView(recipe, null);
                }
            }
        }
    }
}
