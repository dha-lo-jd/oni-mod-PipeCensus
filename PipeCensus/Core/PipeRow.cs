using System.Collections.Generic;
using UnityEngine;

namespace PipeCensus;

public enum PipeKind
{
    Liquid,
    Gas,
    Solid,
}

// One line of the census: what one kind of pipe holds of one thing in the area, or its empty tiles.
public sealed class PipeRow
{
    public PipeKind Kind;

    // What it is (the element, or the item on a conveyor); empty for the "empty tiles" line. Not unique across kinds.
    public string ContentKey = "";
    public bool IsEmpty;
    public string Name;
    public Sprite Icon;
    public Color IconColor = Color.white;

    public float Mass;
    public int Segments;

    // Mass over what the tiles can hold, 0 to 1 (-1 for the empty line).
    public float Fill = -1f;

    // Mass-weighted average and the extremes, in kelvin (-1: none).
    public float Temperature = -1f;
    public float MinTemperature = -1f;
    public float MaxTemperature = -1f;

    // Germs by disease index.
    public readonly Dictionary<byte, long> Germs = new Dictionary<byte, long>();
    public long GermTotal;
    public int Networks;

    // The pipe tile holding the most of it (the first one for the empty line), what a click on the row selects.
    public GameObject Target;
    internal float TargetMass = -1f;
}
