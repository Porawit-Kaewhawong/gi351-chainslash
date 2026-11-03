using UnityEngine;

/// <summary>
/// Interface for objects that can be targeted and slashed during Chain Slash mode.
/// </summary>
public interface ISlashable
{
    /// <summary>
    /// Invoked when the slash line passes through this object upon exiting Chain Slash mode.
    /// </summary>
    /// <param name="slashVector">The direction of the slash trajectory.</param>
    void OnSlashed(Vector2 slashVector);
}

