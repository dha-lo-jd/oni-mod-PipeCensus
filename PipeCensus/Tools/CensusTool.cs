using UnityEngine;

namespace PipeCensus;

// Box-select tool: the dragged rectangle becomes the census area.
public class CensusTool : DragTool
{
    public const string ToolName = "PipeCensusTool";

    protected override void OnDragTool(int cell, int distFromOrigin)
    {
    }

    protected override void OnDragComplete(Vector3 cursorDown, Vector3 cursorUp)
    {
        var selection = CensusSelection.FromRect(Grid.PosToCell(cursorDown), Grid.PosToCell(cursorUp));
        CensusScreen.Open(selection);
    }
}
