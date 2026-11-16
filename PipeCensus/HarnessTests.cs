#if DEBUG
// In-game tests run by Gravitas.TestHarness:
//   python Gravitas.TestProbe/probe.py run --project <this repo> --tests '{"tags":["pipecensus"]}'
// Needs HarnessDllPath in Local.Build.props (Check / Wait / Arrange come from the harness assembly).
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gravitas.TestHarness;
using HarmonyLib;
using UnityEngine;

namespace PipeCensus;

internal static class HarnessTests
{
    [HarnessTest(Name = "pipecensus.mod_loaded", Tags = "pipecensus", RequiresGame = false)]
    public static void ModLoaded()
    {
        Check.True(PipeCensusMod.modPath != null, "PipeCensusMod.OnLoad has not run");
    }

    // UserMod2.OnLoad applies the patches; a second PatchAll would run every patch twice.
    [HarnessTest(Name = "pipecensus.patches_applied_once", Tags = "pipecensus", RequiresGame = false)]
    public static void PatchesAppliedOnce()
    {
        CheckPatchedOnce(AccessTools.Method(typeof(PlayerController), "OnPrefabInit"), typeof(PlayerControllerOnPrefabInitPatch), prefix: false);
        CheckPatchedOnce(AccessTools.Method(typeof(ToolMenu), "CreateBasicTools"), typeof(ToolMenuCreateBasicToolsPatch), prefix: false);
        CheckPatchedOnce(AccessTools.Method(typeof(GameInputMapping), "CompareActionKeyCodes"), typeof(CompareActionKeyCodesPatch), prefix: true);
        CheckPatchedOnce(AccessTools.Method(typeof(GameUtil), nameof(GameUtil.GetHotkeyString)), typeof(GetHotkeyStringPatch), prefix: true);
        CheckPatchedOnce(AccessTools.Method(typeof(KButtonEvent), nameof(KButtonEvent.IsAction)), typeof(KButtonEventIsActionPatch), prefix: true);
        CheckPatchedOnce(AccessTools.Method(typeof(SelectTool), nameof(SelectTool.OnLeftClickDown)), typeof(SelectToolNetworkClickPatch), prefix: true);
        CheckPatchedOnce(AccessTools.Method(typeof(Game), "OnSpawn"), typeof(GameOnSpawnPatch), prefix: false);
    }

    private static void CheckPatchedOnce(System.Reflection.MethodBase target, System.Type patch, bool prefix)
    {
        Check.NotNull(target, "patch target for " + patch.Name + " not found");
        var info = Harmony.GetPatchInfo(target);
        Check.NotNull(info, target.Name + " is not patched");
        var patches = prefix ? info.Prefixes : info.Postfixes;
        Check.Equal(1, patches.Count(p => p.PatchMethod.DeclaringType == patch), patch.Name + " on " + target.Name);
    }

    // The tool exists on the player controller and its button sits right after the empty pipe tool, once.
    [HarnessTest(Name = "pipecensus.tool_registered", Tags = "pipecensus")]
    public static void ToolRegistered()
    {
        Check.Equal(1, PlayerController.Instance.tools.OfType<CensusTool>().Count(), "census tools on the player controller");

        var names = ToolMenu.Instance.basicTools.Where(c => c.tools.Count > 0).Select(c => c.tools[0].toolName).ToList();
        Check.Equal(1, names.Count(n => n == CensusTool.ToolName), "census buttons in the tool menu");
        Check.Equal(names.IndexOf("EmptyPipeTool") + 1, names.IndexOf(CensusTool.ToolName), "position of the census button");
    }

    // The button shows the mod's own icon when ModAssets/tool_icon.png is there, else the borrowed vanilla one.
    [HarnessTest(Name = "pipecensus.tool_icon", Tags = "pipecensus")]
    public static void ToolIconUsed()
    {
        var file = System.IO.Path.Combine(PipeCensusMod.modPath, "ModAssets", ToolIcon.FileName);
        var expected = System.IO.File.Exists(file) ? ToolIcon.SpriteName : ToolIcon.Fallback;
        Check.Equal(expected, ToolIcon.Resolve(), "icon name for the tool button");
        Check.NotNull(Assets.GetSprite(expected), "the game has no sprite named " + expected);

        var collection = ToolMenu.Instance.basicTools.First(c => c.tools.Count > 0 && c.tools[0].toolName == CensusTool.ToolName);
        Check.Equal(expected, collection.icon, "icon of the census button");
        Check.NoLogErrors();
    }

    // Actions above NumActions come from other mods (PLib registers NumActions + id and extends the flag array). The IsAction
    // patch must leave them alone; only the unbound NumActions itself is "never pressed".
    [HarnessTest(Name = "pipecensus.is_action_passes_extended_actions", Tags = "pipecensus", RequiresGame = false)]
    public static void IsActionPassesExtendedActions()
    {
        var flags = new bool[(int)Action.NumActions + 5];
        flags[(int)Action.NumActions + 3] = true;
        var e = new KButtonEvent(null, InputEventType.KeyDown, flags);
        Check.Equal(true, e.IsAction((Action)((int)Action.NumActions + 3)), "an extended action that is set");
        Check.Equal(false, e.IsAction((Action)((int)Action.NumActions + 2)), "an extended action that is not set");
        Check.Equal(false, e.IsAction(Action.NumActions), "the unbound action is never pressed");
    }

    // The census button has no hotkey (Action.NumActions). Vanilla's ToolMenu.OnKeyDown used to throw an IndexOutOfRangeException
    // for it on every key press (fixed by KButtonEventIsActionPatch). The press goes through the game's own input path.
    [HarnessTest(Name = "pipecensus.key_press_does_not_throw", Tags = "pipecensus")]
    public static IEnumerator KeyPressDoesNotThrow()
    {
        yield return Wait.Ticks(2);
        Play.ClearTool();
        Play.Press(KKeyCode.Escape);
        Play.Press(Action.Dig);
        Play.ClearTool();
        Play.Press(KKeyCode.Escape);
    }

    // Pure filter logic on made-up rows: kinds AND contents, and the empty lines only when no content is picked.
    [HarnessTest(Name = "pipecensus.filter", Tags = "pipecensus", RequiresGame = false)]
    public static void FilterRows()
    {
        var rows = new List<PipeRow>
        {
            new PipeRow { Kind = PipeKind.Liquid, ContentKey = "Water", Name = "Water", Segments = 2 },
            new PipeRow { Kind = PipeKind.Liquid, IsEmpty = true, Name = "Empty", Segments = 1 },
            new PipeRow { Kind = PipeKind.Gas, ContentKey = "Oxygen", Name = "Oxygen", Segments = 1 },
            new PipeRow { Kind = PipeKind.Gas, ContentKey = "Water", Name = "Steam?", Segments = 1 },
        };
        var all = new HashSet<PipeKind> { PipeKind.Liquid, PipeKind.Gas, PipeKind.Solid };
        var none = new HashSet<string>();

        Check.Equal(4, CensusScreen.Filter(rows, all, none).Count, "everything passes with no filter");
        Check.Equal(2, CensusScreen.Filter(rows, new HashSet<PipeKind> { PipeKind.Liquid }, none).Count, "liquid only");
        Check.Equal(2, CensusScreen.Filter(rows, all, new HashSet<string> { "Water" }).Count, "a content key matches in every kind");
        var picked = CensusScreen.Filter(rows, all, new HashSet<string> { "Oxygen" });
        Check.Equal(1, picked.Count, "one content picked");
        Check.True(picked.All(r => !r.IsEmpty), "the empty lines are hidden once a content is picked");
        Check.Equal(0, CensusScreen.Filter(rows, new HashSet<PipeKind>(), none).Count, "no kind, no rows");

        var sorted = CensusScreen.Sorted(rows, 2, descending: true); // mass column: the empty line is always last
        Check.True(sorted.Last().IsEmpty || sorted.Count(r => r.IsEmpty) == 1, "the empty line sorts last");
    }

    // A short run of liquid pipes with water, dirty water and an empty tile, plus gas pipes above them.
    private sealed class Scene
    {
        public readonly List<int> Liquid = new List<int>();
        public readonly List<int> Gas = new List<int>();
        private readonly List<GameObject> built = new List<GameObject>();

        public static Scene Create()
        {
            var scene = new Scene();
            var home = Arrange.HomeCell();
            var run = FindRun(home, 4);
            Check.True(run != null, "no free row of 4 cells near the duplicants for the pipes");
            foreach (var cell in run)
            {
                scene.Liquid.Add(cell);
                scene.built.Add(Arrange.Build("LiquidConduit", cell));
            }

            foreach (var cell in run.Take(2).Select(Grid.CellAbove))
            {
                scene.Gas.Add(cell);
                scene.built.Add(Arrange.Build("GasConduit", cell));
            }

            return scene;
        }

        // The first row of "length" free air cells (also free above) near "near".
        private static List<int> FindRun(int near, int length)
        {
            Grid.CellToXY(near, out var nx, out var ny);
            for (var radius = 1; radius < 25; radius++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != radius)
                        {
                            continue;
                        }

                        var run = new List<int>();
                        for (var i = 0; i < length; i++)
                        {
                            var cell = Grid.XYToCell(nx + dx + i, ny + dy);
                            if (!Grid.IsValidCell(cell) || !IsFree(cell) || !IsFree(Grid.CellAbove(cell)))
                            {
                                break;
                            }

                            run.Add(cell);
                        }

                        if (run.Count == length)
                        {
                            return run;
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsFree(int cell) =>
            Grid.IsValidBuildingCell(cell) && !Grid.Solid[cell] && Grid.Objects[cell, (int)ObjectLayer.LiquidConduit] == null
            && Grid.Objects[cell, (int)ObjectLayer.GasConduit] == null && Grid.Objects[cell, (int)ObjectLayer.Building] == null;

        // Joins the first "count" liquid pipes into one network, as laying them with the build tool does.
        public void ConnectLiquid(int count)
        {
            var system = Game.Instance.liquidConduitSystem;
            for (var i = 0; i + 1 < count; i++)
            {
                system.AddConnection(UtilityConnections.Right, Liquid[i], true);
                system.AddConnection(UtilityConnections.Left, Liquid[i + 1], true);
            }
        }

        public void Dispose()
        {
            foreach (var go in built)
            {
                if (go != null)
                {
                    Arrange.Destroy(go);
                }
            }
        }
    }

    // Water in two tiles (5 + 3 kg), dirty water in one, one empty tile, oxygen in a gas pipe: the rows add them up per kind and content.
    [HarnessTest(Name = "pipecensus.contents_summed", Tags = "pipecensus", TimeoutSeconds = 120)]
    public static IEnumerator ContentsSummed()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2);
            scene.ConnectLiquid(3); // one network for the water, the empty tile apart
            yield return Wait.Ticks(3);
            var liquid = Game.Instance.liquidConduitFlow;
            var germs = Db.Get().Diseases.GetIndex(Db.Get().Diseases.SlimeGerms.id);
            liquid.AddElement(scene.Liquid[0], SimHashes.Water, 5f, 300f, byte.MaxValue, 0);
            liquid.AddElement(scene.Liquid[1], SimHashes.Water, 3f, 310f, germs, 4000);
            liquid.AddElement(scene.Liquid[2], SimHashes.DirtyWater, 2f, 320f, byte.MaxValue, 0);
            Game.Instance.gasConduitFlow.AddElement(scene.Gas[0], SimHashes.Oxygen, 0.5f, 290f, byte.MaxValue, 0);

            var rows = PipeCollector.Collect(CensusSelection.FromCells(scene.Liquid.Concat(scene.Gas)));
            var water = rows.Single(r => r.Kind == PipeKind.Liquid && r.ContentKey == SimHashes.Water.ToString());
            Check.True(Mathf.Abs(water.Mass - 8f) < 0.01f, "water mass is " + water.Mass);
            Check.Equal(2, water.Segments, "tiles holding water");
            Check.True(Mathf.Abs(water.Fill - 8f / 20f) < 0.01f, "water fill is " + water.Fill);
            Check.True(Mathf.Abs(water.Temperature - (5f * 300f + 3f * 310f) / 8f) < 0.1f, "mass-weighted temperature is " + water.Temperature);
            Check.True(Mathf.Abs(water.MinTemperature - 300f) < 0.1f && Mathf.Abs(water.MaxTemperature - 310f) < 0.1f, "temperature range is " + water.MinTemperature + " - " + water.MaxTemperature);
            Check.Equal(4000L, water.GermTotal, "germs in the water");
            Check.Equal(0, water.NetworkNumber, "the water is in one network, so it is not numbered");
            Check.Equal(2, water.Tiles.Count, "tiles listed under the water");
            Check.True(water.Tiles[0].Cell == scene.Liquid[0] && water.Tiles[1].Cell == scene.Liquid[1], "tiles are listed with the most water first");
            Check.True(Mathf.Abs(water.Tiles[0].Mass - 5f) < 0.01f && Mathf.Abs(water.Tiles[0].Fill - 0.5f) < 0.01f, "first tile holds " + water.Tiles[0].Mass);
            Check.Equal(4000, water.Tiles[1].DiseaseCount, "germs of the second tile");
            Check.True(!string.IsNullOrEmpty(water.Name) && !water.Name.Contains("<"), "water name is '" + water.Name + "'");
            Check.NotNull(water.Icon, "water icon");
            Check.True(water.Target == Grid.Objects[scene.Liquid[0], (int)ObjectLayer.LiquidConduit], "the row selects the tile with the most water");

            var dirty = rows.Single(r => r.Kind == PipeKind.Liquid && r.ContentKey == SimHashes.DirtyWater.ToString());
            Check.True(Mathf.Abs(dirty.Mass - 2f) < 0.01f, "dirty water mass is " + dirty.Mass);
            Check.Equal(0L, dirty.GermTotal, "germs in the dirty water");

            var empty = rows.Single(r => r.Kind == PipeKind.Liquid && r.IsEmpty);
            Check.Equal(1, empty.Segments, "empty liquid tiles");
            Check.True(empty.Target == Grid.Objects[scene.Liquid[3], (int)ObjectLayer.LiquidConduit], "the empty row selects the empty tile");

            var oxygen = rows.Single(r => r.Kind == PipeKind.Gas && !r.IsEmpty);
            Check.True(Mathf.Abs(oxygen.Mass - 0.5f) < 0.01f, "oxygen mass is " + oxygen.Mass);
            Check.True(Mathf.Abs(oxygen.Fill - 0.5f) < 0.01f, "a gas tile holds 1 kg: " + oxygen.Fill);
            Check.Equal(1, rows.Single(r => r.Kind == PipeKind.Gas && r.IsEmpty).Segments, "empty gas tiles");
            Check.Equal(0, rows.Count(r => r.Kind == PipeKind.Solid), "no conveyor rail here");

            // A cell with no pipe, and an area beside the pipes, give nothing.
            var beside = Grid.CellRight(Grid.CellRight(Grid.CellRight(Grid.CellRight(scene.Liquid[3]))));
            Check.Equal(0, PipeCollector.Collect(CensusSelection.FromRect(beside, beside)).Count, "rows beside the pipes");
            Check.NoLogErrors();
        }
        finally
        {
            scene.Dispose();
        }
    }

    // Pipes joined with the build tool's connections form one network; a pipe beside them that is not joined is another.
    [HarnessTest(Name = "pipecensus.network_cells", Tags = "pipecensus", TimeoutSeconds = 120)]
    public static IEnumerator NetworkCells()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2); // a new pipe sets its own connections when it spawns
            scene.ConnectLiquid(3);
            yield return Wait.Ticks(3);
            var first = PipeNetworks.NetworkAt(PipeKind.Liquid, scene.Liquid[0]);
            Check.NotNull(first, "no network at the first pipe");
            Check.True(first == PipeNetworks.NetworkAt(PipeKind.Liquid, scene.Liquid[2]), "joined pipes should share a network");
            Check.True(first != PipeNetworks.NetworkAt(PipeKind.Liquid, scene.Liquid[3]), "the pipe that is not joined is another network");
            Check.Equal(3, PipeNetworks.Cells(first, PipeKind.Liquid).Count, "tiles of the network");
            Check.True(PipeNetworks.NetworkAt(PipeKind.Gas, scene.Liquid[0]) == null, "no gas network at a liquid pipe");

            var rows = PipeCollector.Collect(CensusSelection.FromCells(scene.Liquid));
            var empties = rows.Where(r => r.IsEmpty).OrderBy(r => r.NetworkNumber).ToList();
            Check.Equal(2, empties.Count, "the empty tiles split into the joined three and the one apart");
            Check.True(empties[0].Segments == 3 && empties[0].NetworkNumber == 1, "the larger network is number 1");
            Check.True(empties[1].Segments == 1 && empties[1].NetworkNumber == 2, "the smaller network is number 2");
            Check.True(empties[0].Network != empties[1].Network, "different networks");
            Check.NoLogErrors();
        }
        finally
        {
            scene.Dispose();
        }
    }

    // In a pipe overlay a plain click selects the pipe; Shift + click opens the census of the whole network.
    [HarnessTest(Name = "pipecensus.network_click", Tags = "pipecensus", TimeoutSeconds = 180)]
    public static IEnumerator NetworkClick()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2); // a new pipe sets its own connections when it spawns
            scene.ConnectLiquid(3);
            yield return Wait.Ticks(3);
            Game.Instance.liquidConduitFlow.AddElement(scene.Liquid[1], SimHashes.Water, 4f, 300f, byte.MaxValue, 0);
            OverlayScreen.Instance.ToggleOverlay(OverlayModes.LiquidConduits.ID);
            yield return null;
            Check.True(PipeNetworks.OverlayKind() == PipeKind.Liquid, "the liquid overlay is not on");

            Play.ClearTool();
            Play.Activate(SelectTool.Instance);
            SelectTool.Instance.Select(null);
            Play.Click(scene.Liquid[1]);
            yield return null;
            Check.True(CensusScreen.Current == null, "a plain click must not open the census");
            Check.NotNull(SelectTool.Instance.selected, "a plain click should select the pipe");

            SelectTool.Instance.Select(null);
            PipeNetworks.TestShiftHeld = true;
            Play.Click(scene.Liquid[0]);
            yield return null;
            PipeNetworks.TestShiftHeld = false;
            var screen = CensusScreen.Current;
            Check.NotNull(screen, "Shift + click on a pipe in the overlay should open the census");
            Check.Equal(2, screen.TotalRows, "rows of the network: water in one of its three tiles, and the empty tiles");
            Check.True(SelectTool.Instance.selected == null, "Shift + click must not also select the pipe");
            Check.NoLogErrors();
        }
        finally
        {
            PipeNetworks.TestShiftHeld = false;
            if (CensusScreen.Current != null)
            {
                CensusScreen.Current.Close();
            }

            OverlayScreen.Instance.ToggleOverlay(OverlayModes.None.ID);
            SelectTool.Instance.Select(null);
            Play.ClearTool();
            scene.Dispose();
        }
    }

    // Hovering a pipe in the overlay lights the whole network; moving to a pipe of another network (or off the pipes) changes it.
    [HarnessTest(Name = "pipecensus.network_hover_highlight", Tags = "pipecensus", TimeoutSeconds = 180)]
    public static IEnumerator NetworkHoverHighlight()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2); // a new pipe sets its own connections when it spawns
            scene.ConnectLiquid(3);
            yield return Wait.Ticks(3);
            OverlayScreen.Instance.ToggleOverlay(OverlayModes.LiquidConduits.ID);
            Play.ClearTool();
            Play.Activate(SelectTool.Instance);
            yield return null;

            Check.Equal(0, NetworkHighlight.LitCount, "tiles lit before hovering");
            Play.Hover(scene.Liquid[0]);
            yield return Wait.Seconds(0.5f, 1);
            Check.Equal(3, NetworkHighlight.LitCount, "tiles lit while hovering a pipe of the three-tile network");

            Play.Hover(scene.Liquid[3]);
            yield return Wait.Seconds(0.5f, 1);
            Check.Equal(1, NetworkHighlight.LitCount, "tiles lit while hovering the single pipe");

            Play.ReleaseMouse();
            Play.Hover(Grid.CellAbove(Grid.CellAbove(Grid.CellAbove(scene.Liquid[0]))));
            yield return Wait.Seconds(0.5f, 1);
            Check.Equal(0, NetworkHighlight.LitCount, "tiles lit while hovering empty space");
            Check.NoLogErrors();
        }
        finally
        {
            Play.ReleaseMouse();
            OverlayScreen.Instance.ToggleOverlay(OverlayModes.None.ID);
            Play.ClearTool();
            scene.Dispose();
        }
    }

    // The full path of a player: toolbar button, drag over the pipes, click a row; the pipe with the most of it is selected.
    [HarnessTest(Name = "pipecensus.toolbar_drag_and_row_click", Tags = "pipecensus", TimeoutSeconds = 180)]
    public static IEnumerator ToolbarDragAndRowClick()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2);
            scene.ConnectLiquid(3);
            yield return Wait.Ticks(3);
            Game.Instance.liquidConduitFlow.AddElement(scene.Liquid[1], SimHashes.Water, 6f, 300f, byte.MaxValue, 0);

            Play.ClickToolButton(CensusTool.ToolName);
            Check.True(Play.ActiveTool is CensusTool, "clicking the census button should activate the census tool, got " + Play.ActiveTool);
            Check.True(CensusScreen.Current == null, "the window is open before the drag");

            Play.Drag(scene.Liquid[0], scene.Liquid[3]);
            yield return null;
            var screen = CensusScreen.Current;
            Check.NotNull(screen, "dragging the census tool over the pipes did not open the window");
            Check.Equal(3, screen.TotalRows, "rows for the dragged liquid pipes (water, the empty tiles of its network, the empty tile apart)");
            var rowButtons = Play.FindUi("Row", screen);
            Check.Equal(3, rowButtons.Count, "row buttons on screen");

            Play.ClickUi(rowButtons[0]);
            yield return null;
            Check.True(CensusScreen.Current == null, "picking a row should close the window");
            Check.True(Play.ActiveTool is SelectTool, "picking a row should hand over to the select tool, got " + Play.ActiveTool);
            var selected = SelectTool.Instance.selected;
            Check.NotNull(selected, "the clicked pipe should be selected");
            Check.True(selected.gameObject == Grid.Objects[scene.Liquid[1], (int)ObjectLayer.LiquidConduit], "the pipe holding the water should be selected");
            Check.NoLogErrors();
        }
        finally
        {
            if (CensusScreen.Current != null)
            {
                CensusScreen.Current.Close();
            }

            SelectTool.Instance.Select(null);
            Play.ClearTool();
            scene.Dispose();
        }
    }

    // Conveyor rail: an item on a rail tile is listed by what it is.
    [HarnessTest(Name = "pipecensus.solid_listed", Tags = "pipecensus", TimeoutSeconds = 120)]
    public static IEnumerator SolidListed()
    {
        var home = Arrange.HomeCell();
        var cell = Arrange.FindBuildableCell("SolidConduit", home);
        Check.True(cell >= 0, "no free cell for a conveyor rail");
        var rail = Arrange.Build("SolidConduit", cell);
        GameObject chunk = null;
        try
        {
            yield return Wait.Ticks(2);
            chunk = Arrange.SpawnElement(SimHashes.SandStone, cell, 12f, 300f);
            Game.Instance.solidConduitFlow.AddPickupable(cell, chunk.GetComponent<Pickupable>());

            var rows = PipeCollector.Collect(CensusSelection.FromCells(new[] { cell }));
            var row = rows.Single(r => r.Kind == PipeKind.Solid);
            Check.True(!row.IsEmpty, "the rail tile should hold the chunk");
            Check.True(Mathf.Abs(row.Mass - 12f) < 0.1f, "chunk mass is " + row.Mass);
            Check.True(Mathf.Abs(row.Fill - 12f / 20f) < 0.01f, "rail fill is " + row.Fill);
            Check.True(!string.IsNullOrEmpty(row.Name) && !row.Name.Contains("<"), "name is '" + row.Name + "'");
            Check.NotNull(row.Icon, "icon");
            Check.NoLogErrors();
        }
        finally
        {
            Game.Instance.solidConduitFlow.EmptyConduit(cell);
            if (chunk != null)
            {
                Arrange.Destroy(chunk);
            }

            Arrange.Destroy(rail);
        }
    }

    // The same content in several networks is told apart by number, the network with the most tiles first.
    [HarnessTest(Name = "pipecensus.network_numbering", Tags = "pipecensus", RequiresGame = false)]
    public static void NetworkNumbering()
    {
        var rows = new List<PipeRow>
        {
            new PipeRow { Kind = PipeKind.Liquid, ContentKey = "Water", Name = "Water", Segments = 2, Network = 7 },
            new PipeRow { Kind = PipeKind.Liquid, ContentKey = "Water", Name = "Water", Segments = 5, Network = 9 },
            new PipeRow { Kind = PipeKind.Liquid, ContentKey = "Water", Name = "Water", Segments = 5, Network = 8 },
            new PipeRow { Kind = PipeKind.Liquid, ContentKey = "Oil", Name = "Oil", Segments = 1, Network = 7 },
            new PipeRow { Kind = PipeKind.Gas, ContentKey = "Water", Name = "Steam", Segments = 3, Network = 7 },
        };
        PipeCollector.NumberNetworks(rows);
        Check.Equal(3, rows[0].NetworkNumber, "the smallest water network is last");
        Check.Equal(2, rows[1].NetworkNumber, "equal sizes go by network id (9 after 8)");
        Check.Equal(1, rows[2].NetworkNumber, "the first of the largest");
        Check.Equal(0, rows[3].NetworkNumber, "oil is in one network: no number");
        Check.Equal(0, rows[4].NetworkNumber, "the same name in another kind of pipe is counted apart");
    }

    // Made-up rows with tiles: the + opens a row (collapsed at first), up to 20 tiles are plain rows, more sit in a window
    // of 20 that scrolls by itself and hands the wheel to the list once it is at the end.
    [HarnessTest(Name = "pipecensus.tree_and_nested_scroll", Tags = "pipecensus", TimeoutSeconds = 120)]
    public static IEnumerator TreeAndNestedScroll()
    {
        var rows = new List<PipeRow>
        {
            MadeUpRow("Water", 30, mass: 100f),
            MadeUpRow("Oil", 3, mass: 50f),
        };
        for (var i = 0; i < 12; i++)
        {
            rows.Add(MadeUpRow("Filler" + i, 1, mass: 1f));
        }

        try
        {
            CensusScreen.OpenRows(rows);
            yield return null;
            var screen = CensusScreen.Current;
            Check.NotNull(screen, "the window did not open");
            Check.Equal(2, Play.FindUi("Expander", screen).Count, "only rows with more than one tile have a +");
            Check.Equal(0, Play.FindUi("TileRow", screen).Count, "tiles are collapsed at first");

            var oil = rows[1];
            screen.ToggleExpanded(oil);
            yield return null;
            Check.True(screen.IsExpanded(oil), "the oil row should be open");
            Check.Equal(3, Play.FindUi("TileRow", screen).Count, "three tiles under the oil");
            Check.Equal(0, screen.GetComponentsInChildren<NestedScrollRect>(true).Length, "up to 20 tiles need no window of their own");

            var water = rows[0];
            screen.ToggleExpanded(water);
            yield return null;
            yield return null;
            var nested = screen.GetComponentsInChildren<NestedScrollRect>(true);
            Check.Equal(1, nested.Length, "thirty tiles sit in one scrolling window");
            Check.Equal(33, Play.FindUi("TileRow", screen).Count, "all tiles are built");
            var window = (RectTransform)nested[0].transform;
            Check.True(Mathf.Abs(window.rect.height - (CensusScreen.MaxTileRows * 30f + (CensusScreen.MaxTileRows - 1) * 2f)) < 1f, "the window shows 20 rows: " + window.rect.height);

            // Wheel: inside the window it scrolls the window; at its end the list takes over.
            var outer = screen.ListScroll;
            var outerBefore = outer.content.anchoredPosition.y;
            nested[0].verticalNormalizedPosition = 1f;
            var down = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { scrollDelta = new Vector2(0f, -1f) };
            nested[0].OnScroll(down);
            Check.True(nested[0].verticalNormalizedPosition < 1f, "scrolling down inside the window moves it");
            Check.True(Mathf.Abs(outer.content.anchoredPosition.y - outerBefore) < 0.01f, "the list stays while the window can scroll");

            nested[0].verticalNormalizedPosition = 0f;
            nested[0].OnScroll(down);
            Check.True(outer.content.anchoredPosition.y > outerBefore + 0.5f, "at the end of the window the wheel scrolls the list: " + outer.content.anchoredPosition.y);

            nested[0].verticalNormalizedPosition = 1f;
            var listNow = outer.content.anchoredPosition.y;
            var up = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { scrollDelta = new Vector2(0f, 1f) };
            nested[0].OnScroll(up);
            Check.True(outer.content.anchoredPosition.y < listNow - 0.5f, "at the top of the window, wheel up scrolls the list up");

            screen.ToggleExpanded(water);
            yield return null;
            Check.Equal(3, Play.FindUi("TileRow", screen).Count, "closing the row hides its tiles");
            Check.NoLogErrors();
        }
        finally
        {
            if (CensusScreen.Current != null)
            {
                CensusScreen.Current.Close();
            }
        }
    }

    private static PipeRow MadeUpRow(string name, int tiles, float mass)
    {
        var row = new PipeRow { Kind = PipeKind.Liquid, ContentKey = name, Name = name, Segments = tiles, Mass = mass, Fill = 0.5f, Temperature = 300f, MinTemperature = 300f, MaxTemperature = 300f };
        for (var i = 0; i < tiles; i++)
        {
            row.Tiles.Add(new PipeTile { Cell = Grid.XYToCell(10 + i, 10), Mass = mass / tiles, Fill = 0.5f, Temperature = 300f });
        }

        return row;
    }

    // Clicking a tile under a row selects that pipe.
    [HarnessTest(Name = "pipecensus.tile_row_click", Tags = "pipecensus", TimeoutSeconds = 180)]
    public static IEnumerator TileRowClick()
    {
        var scene = Scene.Create();
        try
        {
            yield return Wait.Ticks(2);
            scene.ConnectLiquid(3);
            yield return Wait.Ticks(3);
            var liquid = Game.Instance.liquidConduitFlow;
            liquid.AddElement(scene.Liquid[0], SimHashes.Water, 5f, 300f, byte.MaxValue, 0);
            liquid.AddElement(scene.Liquid[1], SimHashes.Water, 3f, 300f, byte.MaxValue, 0);

            CensusScreen.Open(CensusSelection.FromCells(scene.Liquid));
            yield return null;
            var screen = CensusScreen.Current;
            var water = Enumerable.Range(0, screen.TotalRows).Select(screen.Row).Single(r => !r.IsEmpty);
            screen.ToggleExpanded(water);
            yield return null;
            var tileRows = Play.FindUi("TileRow", screen);
            Check.Equal(2, tileRows.Count, "tile rows under the water");

            Play.ClickUi(tileRows[1]);
            yield return null;
            Check.True(CensusScreen.Current == null, "picking a tile should close the window");
            var selected = SelectTool.Instance.selected;
            Check.NotNull(selected, "the clicked tile should be selected");
            Check.True(selected.gameObject == Grid.Objects[scene.Liquid[1], (int)ObjectLayer.LiquidConduit], "the second tile (3 kg) should be selected");
            Check.NoLogErrors();
        }
        finally
        {
            if (CensusScreen.Current != null)
            {
                CensusScreen.Current.Close();
            }

            SelectTool.Instance.Select(null);
            Play.ClearTool();
            scene.Dispose();
        }
    }

    // A scene for looking at the window (see CLAUDE.md): pipes with a mix of contents, left in the game.
    private static Scene demo;
    private static GameObject demoRail;
    private static GameObject demoChunk;

    public static string DemoBuild()
    {
        demo = Scene.Create();
        var railCell = Arrange.FindBuildableCell("SolidConduit", Arrange.HomeCell());
        demoRail = Arrange.Build("SolidConduit", railCell);
        return "pipes at " + string.Join(",", demo.Liquid) + " rail " + railCell;
    }

    // Run after DemoBuild and a short wait: a tile that has just been built forgets what is put into it at once.
    public static string DemoFill()
    {
        var liquid = Game.Instance.liquidConduitFlow;
        var germs = Db.Get().Diseases.GetIndex(Db.Get().Diseases.SlimeGerms.id);
        liquid.AddElement(demo.Liquid[0], SimHashes.Water, 9.5f, 300f, byte.MaxValue, 0);
        liquid.AddElement(demo.Liquid[1], SimHashes.Water, 7f, 330f, germs, 52000);
        liquid.AddElement(demo.Liquid[2], SimHashes.DirtyWater, 4f, 310f, germs, 8000);
        Game.Instance.gasConduitFlow.AddElement(demo.Gas[0], SimHashes.Oxygen, 0.8f, 290f, byte.MaxValue, 0);
        var railCell = Grid.PosToCell(demoRail);
        demoChunk = Arrange.SpawnElement(SimHashes.SandStone, railCell, 15f, 300f);
        Game.Instance.solidConduitFlow.AddPickupable(railCell, demoChunk.GetComponent<Pickupable>());
        return "filled";
    }

    // Joins three of the demo pipes, turns the liquid overlay on and hovers the first pipe (wait a moment, then look).
    public static string DemoHover()
    {
        demo.ConnectLiquid(3);
        OverlayScreen.Instance.ToggleOverlay(OverlayModes.LiquidConduits.ID);
        Play.ClearTool();
        Play.Activate(SelectTool.Instance);
        Play.Hover(demo.Liquid[0]);
        return "hovering " + demo.Liquid[0];
    }

    // Joins three of the demo pipes without the overlay (run before DemoFill, after a short wait).
    public static string DemoConnect()
    {
        demo.ConnectLiquid(3);
        return "joined";
    }

    // Opens every row that has tiles under it (run after DemoOpen).
    public static string DemoExpand()
    {
        var screen = CensusScreen.Current;
        for (var i = 0; i < screen.TotalRows; i++)
        {
            if (screen.Row(i).Tiles.Count > 1 && !screen.IsExpanded(screen.Row(i)))
            {
                screen.ToggleExpanded(screen.Row(i));
            }
        }

        return "expanded";
    }

    public static string DemoOpen()
    {
        var cells = demo.Liquid.Concat(demo.Gas).Concat(new[] { Grid.PosToCell(demoRail) });
        CensusScreen.Open(CensusSelection.FromCells(cells));
        return "rows " + CensusScreen.Current.TotalRows;
    }
}
#endif
