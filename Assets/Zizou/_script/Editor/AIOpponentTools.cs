using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Car2D > Race > Add 3 AI Opponents
///
/// Clones the player's car (so the AI uses exactly the same physics, off-road
/// penalty, checkpoints and lap counting), turns it into an AI car and places it
/// behind the player on a small starting grid.
/// </summary>
public static class AIOpponentTools
{
    const string CarsFolder = "Assets/Zizou/Spriets/Cars";
    const int CarsPerClick = 3;

    [MenuItem("Tools/Car2D/Race/Add 3 AI Opponents", false, 30)]
    static void AddOpponents()
    {
        TopDown2DCarController playerCar = FindPlayerCar();

        if (playerCar == null)
        {
            EditorUtility.DisplayDialog(
                "No player car",
                "Could not find a car with a CarInput (player) component in the scene.",
                "OK");
            return;
        }

        RaceTools.EnsureRaceManager();

        // The player must be identifiable so the AI can compare its progress with them.
        LapTracker playerTracker = playerCar.GetComponent<LapTracker>();
        if (playerTracker == null)
            playerTracker = Undo.AddComponent<LapTracker>(playerCar.gameObject);

        playerTracker.isPlayer = true;
        playerTracker.enabled = true;

        int checkpointCount = Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None).Length;
        if (checkpointCount < 2)
        {
            EditorUtility.DisplayDialog(
                "Not enough checkpoints",
                "Create a START/FINISH checkpoint and at least one more checkpoint first " +
                "(Tools > Car2D > Race), otherwise the AI cars have nowhere to drive.",
                "OK");
            return;
        }

        List<Sprite> sprites = LoadCarSprites();
        int existingAi = Object.FindObjectsByType<AIDriver>(FindObjectsSortMode.None).Length;
        List<GameObject> created = new List<GameObject>();

        for (int i = 0; i < CarsPerClick; i++)
        {
            int slot = existingAi + i;

            GameObject clone = Object.Instantiate(
                playerCar.gameObject,
                SlotPosition(playerCar.transform, slot),
                playerCar.transform.rotation);

            Undo.RegisterCreatedObjectUndo(clone, "Create AI Car");
            clone.name = "AI Car " + (slot + 1);

            // The clone must not read the player's keyboard input.
            CarInput[] clonedInputs = clone.GetComponentsInChildren<CarInput>(true);
            foreach (CarInput clonedInput in clonedInputs)
                Undo.DestroyObjectImmediate(clonedInput);

            LapTracker tracker = clone.GetComponent<LapTracker>();
            if (tracker == null)
                tracker = Undo.AddComponent<LapTracker>(clone);

            tracker.isPlayer = false;
            tracker.racerName = clone.name;
            tracker.enabled = true;

            AIDriver driver = clone.GetComponent<AIDriver>();
            if (driver == null)
                driver = Undo.AddComponent<AIDriver>(clone);

            driver.roadLayers = LayerMask.GetMask("Road");
            driver.path = Object.FindFirstObjectByType<RacePath>();

            TopDown2DCarController controller = clone.GetComponent<TopDown2DCarController>();
            controller.surfaceSpeedMultiplier = 1f;

            SpriteRenderer spriteRenderer = clone.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null && sprites.Count > 0)
                spriteRenderer.sprite = sprites[slot % sprites.Count];

            created.Add(clone);
        }

        Selection.objects = created.ToArray();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        if (Object.FindFirstObjectByType<RacePath>() == null)
        {
            Debug.LogWarning("[AI] There is no Race Path in the scene, so the AI cars will drive straight " +
                             "at the next checkpoint instead of following the track. Create one with " +
                             "Tools > Car2D > Race > Create Race Path From Checkpoints and press Play again.");
        }

        Debug.Log("[AI] Added " + created.Count + " AI opponents behind the player. " +
                  "Save the scene (Ctrl+S) and press Play. " +
                  "Select an AI car and tick \"Draw Debug\" on AIDriver to see its steering and its aim point.");
    }

    static TopDown2DCarController FindPlayerCar()
    {
        TopDown2DCarController fallback = null;

        foreach (TopDown2DCarController car in Object.FindObjectsByType<TopDown2DCarController>(FindObjectsSortMode.None))
        {
            if (car.GetComponent<CarInput>() != null)
                return car;

            if (fallback == null)
                fallback = car;
        }

        return fallback;
    }

    // A small 2-column grid behind the player: the player keeps the front slot.
    static Vector3 SlotPosition(Transform player, int index)
    {
        int row = index / 2;
        int column = index % 2;

        float lateral = column == 0 ? -1.2f : 1.2f;
        float behind = 1.6f + row * 1.6f;

        return player.position - player.up * behind + player.right * lateral;
    }

    static List<Sprite> LoadCarSprites()
    {
        List<Sprite> sprites = new List<Sprite>();

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CarsFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite)
                    sprites.Add(sprite);
            }
        }

        sprites.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        return sprites;
    }
}
