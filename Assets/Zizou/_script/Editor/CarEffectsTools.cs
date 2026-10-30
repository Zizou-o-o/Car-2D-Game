using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Car2D > FX
///
/// Adds the drift / smoke / dust / collision-spark cosmetics to every car in the scene,
/// plus the speed wind + focus overlay on the player car.
/// </summary>
public static class CarEffectsTools
{
    [MenuItem("Tools/Car2D/FX/Add Drift + Smoke + Dirt To Cars", false, 40)]
    static void AddToCars()
    {
        int added = 0;
        int skipped = 0;

        foreach (TopDown2DCarController car in Object.FindObjectsByType<TopDown2DCarController>(FindObjectsSortMode.None))
        {
            if (car.GetComponent<CarEffects>() != null)
            {
                skipped++;
                continue;
            }

            Undo.AddComponent<CarEffects>(car.gameObject);
            added++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        if (added == 0 && skipped == 0)
        {
            Debug.Log("[FX] No cars with a TopDown2DCarController were found in the scene.");
            return;
        }

        Debug.Log("[FX] Added drift lines, white tyre smoke, off-road dust and collision sparks " +
                  "to " + added + " car(s)" + (skipped > 0 ? " (" + skipped + " already had them)" : "") +
                  ". Save the scene (Ctrl+S) and press Play.");
    }

    [MenuItem("Tools/Car2D/FX/Add Speed Wind + Focus To Player", false, 42)]
    static void AddSpeedToPlayer()
    {
        CarInput[] players = Object.FindObjectsByType<CarInput>(FindObjectsSortMode.None);

        if (players.Length == 0)
        {
            Debug.Log("[FX] No car with a CarInput component was found, so there is no player " +
                      "car to add the speed overlay to.");
            return;
        }

        int added = 0;

        foreach (CarInput player in players)
        {
            if (player.GetComponent<SpeedEffects>() != null)
                continue;

            Undo.AddComponent<SpeedEffects>(player.gameObject);
            added++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[FX] Speed wind + focus overlay added to " + added + " player car(s). " +
                  "Save the scene (Ctrl+S) and press Play.");
    }

    [MenuItem("Tools/Car2D/FX/Log Car Effects Settings", false, 41)]
    static void LogSettings()
    {
        CarEffects[] effects = Object.FindObjectsByType<CarEffects>(FindObjectsSortMode.None);

        if (effects.Length == 0)
        {
            Debug.Log("[FX] No CarEffects in the scene yet. Run Tools > Car2D > FX > " +
                      "Add Drift + Smoke + Dirt To Cars first.");
            return;
        }

        foreach (CarEffects effect in effects)
        {
            Debug.Log("[FX] " + effect.name + ": drift " + effect.driftLateralSpeed + " u/s or " +
                      effect.driftSlipAngle + " deg, skid " + effect.skidWidth + " wide / " +
                      effect.skidTime + " s, smoke " + effect.smokeRate + "/s, dust " +
                      effect.dustRate + "/s, sparks " + effect.sparkCount + " per hit above " +
                      effect.minSparkImpact + " u/s, shake " + effect.maxHitShake + " u.", effect);
        }

        SpeedEffects speed = Object.FindFirstObjectByType<SpeedEffects>();

        if (speed == null)
            Debug.Log("[FX] No SpeedEffects in the scene yet. Run Tools > Car2D > FX > " +
                      "Add Speed Wind + Focus To Player.");
        else
            Debug.Log("[FX] " + speed.name + ": wind from " + speed.showAt + " to " + speed.fullAt +
                      " of top speed, " + speed.windParticlesPerSecond + " streaks/s of " +
                      speed.windSize + " units, wind alpha " + speed.windAlpha +
                      ", border alpha " + speed.borderAlpha + ".", speed);
    }
}
