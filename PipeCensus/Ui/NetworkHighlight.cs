using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PipeCensus;

// While the cursor is over a pipe in a pipe overlay, the whole network that pipe belongs to is lit (the same additive
// tint of the animation the game uses for hovering). It only looks; nothing is selected or changed.
public sealed class NetworkHighlight : MonoBehaviour
{
    private static readonly Color32 Lit = new Color32(60, 170, 200, 0);
    private static readonly Color32 None = new Color32(0, 0, 0, 0);
    private const float Interval = 0.1f;
    private const float Rescan = 1f;

    private static NetworkHighlight instance;

    // The tiles lit now (read by the in-game tests).
    internal static int LitCount => instance == null ? 0 : instance.lit.Count;

    private readonly List<KBatchedAnimController> lit = new List<KBatchedAnimController>();
    private PipeKind litKind;
    private int litNetwork = -1;
    private float nextCheck;
    private float nextRescan;

    internal static void Create()
    {
        if (instance != null)
        {
            Destroy(instance.gameObject);
        }

        instance = new GameObject("PipeCensusNetworkHighlight").AddComponent<NetworkHighlight>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
        {
            return;
        }

        nextCheck = Time.unscaledTime + Interval;
        Refresh();
    }

    internal void Refresh()
    {
        var network = HoveredNetwork(out var kind);
        if (network == null)
        {
            Clear();
            return;
        }

        // Same network as before: scanning the world for its tiles again is only worth it now and then (a pipe built or removed).
        if (network.id == litNetwork && kind == litKind && Time.unscaledTime < nextRescan)
        {
            return;
        }

        var cells = PipeNetworks.Cells(network, kind);
        nextRescan = Time.unscaledTime + Rescan;
        if (network.id == litNetwork && kind == litKind && cells.Count == lit.Count)
        {
            return;
        }

        Clear();
        litNetwork = network.id;
        litKind = kind;
        foreach (var cell in cells)
        {
            var tile = PipeNetworks.TileAt(kind, cell);
            var controller = tile != null ? tile.GetComponent<KBatchedAnimController>() : null;
            if (controller != null)
            {
                controller.HighlightColour = Lit;
                lit.Add(controller);
            }
        }
    }

    // The network of the pipe under the cursor, when the select tool is hovering one in the matching pipe overlay.
    private static FlowUtilityNetwork HoveredNetwork(out PipeKind kind)
    {
        kind = PipeKind.Liquid;
        var overlay = PipeNetworks.OverlayKind();
        if (overlay == null || SelectTool.Instance == null || PlayerController.Instance == null || PlayerController.Instance.ActiveTool != SelectTool.Instance)
        {
            return null;
        }

        var hover = SelectTool.Instance.hover;
        if (hover == null)
        {
            return null;
        }

        var cell = Grid.PosToCell(hover.transform.GetPosition());
        if (PipeNetworks.TileAt(overlay.Value, cell) != hover.gameObject)
        {
            return null;
        }

        kind = overlay.Value;
        return PipeNetworks.NetworkAt(kind, cell);
    }

    private void Clear()
    {
        foreach (var controller in lit)
        {
            if (controller != null)
            {
                controller.HighlightColour = None;
            }
        }

        lit.Clear();
        litNetwork = -1;
    }

    private void OnDestroy()
    {
        Clear();
        if (instance == this)
        {
            instance = null;
        }
    }
}

// A new highlighter with every game that is loaded.
[HarmonyPatch(typeof(Game), "OnSpawn")]
public static class GameOnSpawnPatch
{
    public static void Postfix() => NetworkHighlight.Create();
}
