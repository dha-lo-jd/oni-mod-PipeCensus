using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PipeCensus;

// Registers the census tool like a vanilla one: a tool object on the PlayerController (look and cursor copied from the
// empty pipe tool) and a button in the basic tool menu.
[HarmonyPatch(typeof(PlayerController), "OnPrefabInit")]
public static class PlayerControllerOnPrefabInitPatch
{
    public static void Postfix(PlayerController __instance)
    {
        var template = __instance.tools.OfType<EmptyPipeTool>().FirstOrDefault();
        if (template == null || __instance.tools.OfType<CensusTool>().Any())
        {
            return;
        }

        var go = new GameObject(CensusTool.ToolName);
        go.SetActive(false);
        go.transform.SetParent(__instance.gameObject.transform, false);
        var tool = go.AddComponent<CensusTool>();
        CopyFields(typeof(InterfaceTool), template, tool);
        CopyFields(typeof(DragTool), template, tool);
        go.SetActive(true);
        go.SetActive(false);
        __instance.tools = __instance.tools.Concat(new InterfaceTool[] { tool }).ToArray();
    }

    // Copies the prefab-configured (public / serialized) fields so the new tool looks and behaves like the template.
    private static void CopyFields(Type type, object from, object to)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var field in type.GetFields(flags))
        {
            if (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
            {
                field.SetValue(to, field.GetValue(from));
            }
        }
    }
}

[HarmonyPatch(typeof(ToolMenu), "CreateBasicTools")]
public static class ToolMenuCreateBasicToolsPatch
{
    public static void Postfix(ToolMenu __instance)
    {
        // CreateBasicTools can run more than once on the same menu; add the button only once.
        if (__instance.basicTools.Any(c => c.tools.Count > 0 && c.tools[0].toolName == CensusTool.ToolName))
        {
            return;
        }

        var collection = ToolMenu.CreateToolCollection(STRINGS.UI.TOOL_NAME, ToolIcon.Resolve(), Action.NumActions,
            CensusTool.ToolName, STRINGS.UI.TOOL_TOOLTIP, largeIcon: false);
        var index = __instance.basicTools.FindIndex(c => c.tools.Count > 0 && c.tools[0].toolName == "EmptyPipeTool");
        __instance.basicTools.Insert(index >= 0 ? index + 1 : __instance.basicTools.Count, collection);
    }
}

// ToolMenu.OnKeyDown calls e.IsAction(tool.hotkey) without checking for the unbound NumActions, and the key event's
// flag array has exactly NumActions entries, so any key press threw IndexOutOfRangeException. An unbound action is never pressed.
// Only NumActions itself is unbound: PLib mods register actions above it (NumActions + id) and extend the array, so those must pass.
[HarmonyPatch(typeof(KButtonEvent), nameof(KButtonEvent.IsAction))]
public static class KButtonEventIsActionPatch
{
    public static bool Prefix(Action action, ref bool __result)
    {
        if (action == Action.NumActions)
        {
            __result = false;
            return false;
        }

        return true;
    }
}

// The census button has no hotkey; vanilla asserts when comparing an unbound action.
[HarmonyPatch(typeof(GameInputMapping), "CompareActionKeyCodes")]
public static class CompareActionKeyCodesPatch
{
    public static bool Prefix(Action a, Action b, ref bool __result)
    {
        if (a == Action.NumActions || b == Action.NumActions)
        {
            __result = false;
            return false;
        }

        return true;
    }
}

// The tool menu builds the button tooltip with the hotkey text of its action. Vanilla looks the action up in the key
// bindings and throws when it is missing; our unbound NumActions can be missing there (e.g. after a mod that added
// bindings was disabled), so give it an empty hotkey text instead.
[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GetHotkeyString))]
public static class GetHotkeyStringPatch
{
    public static bool Prefix(Action action, ref string __result)
    {
        if (action == Action.NumActions)
        {
            __result = "";
            return false;
        }

        return true;
    }
}

// Pipe overlay: Shift + click on a pipe takes the census of its whole network instead of selecting the pipe. Without Shift
// the click selects as always.
[HarmonyPatch(typeof(SelectTool), nameof(SelectTool.OnLeftClickDown))]
public static class SelectToolNetworkClickPatch
{
    public static bool Prefix(Vector3 cursor_pos)
    {
        var kind = PipeNetworks.OverlayKind();
        if (kind == null || !PipeNetworks.ShiftHeld)
        {
            return true;
        }

        var network = PipeNetworks.NetworkAt(kind.Value, Grid.PosToCell(cursor_pos));
        if (network == null)
        {
            return true;
        }

        CensusScreen.Open(CensusSelection.FromCells(PipeNetworks.Cells(network, kind.Value)));
        return false;
    }
}
