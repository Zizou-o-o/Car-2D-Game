using UnityEngine;

/// <summary>
/// The core of context steering (after Ruben Frans' ContextSteering-Unity):
///
/// - A circle of candidate directions (the "map resolution").
/// - Each direction gets an interest value (how much we want to go there) and a
///   danger value (how much we want to avoid it).
/// - Interest and danger are merged per direction (highest value wins) and the
///   danger is subtracted from the interest, exactly like the reference project.
/// - The direction with the highest remaining score is the one we steer towards.
///
/// This class only does the maths; <see cref="AIDriver"/> fills the maps.
/// </summary>
public class ContextSteering
{
    readonly Vector2[] directions;
    readonly float[] interest;
    readonly float[] danger;

    public ContextSteering(int resolution)
    {
        resolution = Mathf.Clamp(resolution, 4, 128);

        directions = new Vector2[resolution];
        interest = new float[resolution];
        danger = new float[resolution];

        float interval = Mathf.PI * 2f / resolution;

        for (int i = 0; i < resolution; i++)
        {
            float angle = i * interval;
            directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }

    public int Count => directions.Length;

    public Vector2 Direction(int index) => directions[index];
    public float Interest(int index) => interest[index];
    public float Danger(int index) => danger[index];

    /// <summary>Interest minus danger: the value that decides where we steer.</summary>
    public float Score(int index) => interest[index] - danger[index];

    /// <summary>Clear both maps. Call this once before filling them every frame.</summary>
    public void Begin()
    {
        System.Array.Clear(interest, 0, interest.Length);
        System.Array.Clear(danger, 0, danger.Length);
    }

    /// <summary>Adds a behavior's interest for this direction. The highest value wins.</summary>
    public void AddInterest(int index, float value)
    {
        if (value > interest[index])
            interest[index] = value;
    }

    /// <summary>Adds a behavior's danger for this direction. The highest value wins.</summary>
    public void AddDanger(int index, float value)
    {
        if (value > danger[index])
            danger[index] = value;
    }

    public Vector2 Choose()
    {
        return Choose(out float _);
    }

    /// <summary>Returns the best direction and its score (interest - danger).</summary>
    public Vector2 Choose(out float bestScore)
    {
        int bestIndex = 0;
        bestScore = Score(0);

        for (int i = 1; i < directions.Length; i++)
        {
            float score = Score(i);

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return directions[bestIndex];
    }
}
