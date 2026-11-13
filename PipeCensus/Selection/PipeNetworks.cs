using System.Collections.Generic;
using UnityEngine;

namespace PipeCensus;

// Pipe networks as the pipe overlays show them: which kind of pipe the current overlay is about, the network a pipe tile
// belongs to, and the tiles of a network.
public static class PipeNetworks
{
    // Tests cannot press a key the game reads straight from Input, so they can say it is held.
    internal static bool TestShiftHeld;

    // Clicking a pipe selects it as always; with Shift held in a pipe overlay it takes the census of its network instead.
    public static bool ShiftHeld =>
        TestShiftHeld || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    // The kind of pipe the active overlay shows, or null when the overlay is not a pipe overlay.
    public static PipeKind? OverlayKind()
    {
        if (OverlayScreen.Instance == null)
        {
            return null;
        }

        var mode = OverlayScreen.Instance.GetMode();
        if (mode == OverlayModes.LiquidConduits.ID)
        {
            return PipeKind.Liquid;
        }

        if (mode == OverlayModes.GasConduits.ID)
        {
            return PipeKind.Gas;
        }

        return mode == OverlayModes.SolidConveyor.ID ? PipeKind.Solid : (PipeKind?)null;
    }

    // The network of the pipe tile at "cell", or null when there is no pipe of that kind there.
    public static FlowUtilityNetwork NetworkAt(PipeKind kind, int cell)
    {
        if (!Grid.IsValidCell(cell) || !PipeCollector.HasPipe(kind, cell))
        {
            return null;
        }

        var game = Game.Instance;
        UtilityNetwork network = kind == PipeKind.Liquid
            ? game.liquidConduitSystem.GetNetworkForCell(cell)
            : kind == PipeKind.Gas
                ? game.gasConduitSystem.GetNetworkForCell(cell)
                : game.solidConduitSystem.GetNetworkForCell(cell);
        return network as FlowUtilityNetwork;
    }

    // The pipe tiles of a network (not the buildings at its ends). The game keeps no list of them (a network only counts its
    // tiles), so the active world is scanned for tiles that belong to it; a network never reaches into another world.
    public static List<int> Cells(FlowUtilityNetwork network, PipeKind kind)
    {
        var cells = new List<int>();
        var world = ClusterManager.Instance != null ? ClusterManager.Instance.activeWorld : null;
        if (world == null)
        {
            return cells;
        }

        for (var y = world.WorldOffset.y; y < world.WorldOffset.y + world.WorldSize.y; y++)
        {
            for (var x = world.WorldOffset.x; x < world.WorldOffset.x + world.WorldSize.x; x++)
            {
                var cell = Grid.XYToCell(x, y);
                if (PipeCollector.HasPipe(kind, cell) && ReferenceEquals(NetworkAt(kind, cell), network))
                {
                    cells.Add(cell);
                }
            }
        }

        return cells;
    }

    public static GameObject TileAt(PipeKind kind, int cell) => PipeCollector.TileObject(kind, cell);
}
