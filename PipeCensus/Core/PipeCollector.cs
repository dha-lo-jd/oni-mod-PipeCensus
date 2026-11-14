using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PipeCensus;

// Reads the pipe tiles in the selected area (liquid, gas and conveyor rail) and sums what they hold up per kind and content.
public static class PipeCollector
{
    // What one tile of each kind can hold (the game's constants).
    public static float Capacity(PipeKind kind)
    {
        switch (kind)
        {
            case PipeKind.Liquid: return ConduitFlow.MAX_LIQUID_MASS;
            case PipeKind.Gas: return ConduitFlow.MAX_GAS_MASS;
            default: return SolidConduitFlow.MAX_SOLID_MASS;
        }
    }

    private sealed class Sum
    {
        public PipeRow Row;
        public double WeightedTemperature;
    }

    // The content of a tile as read from the flow manager.
    private struct Content
    {
        public bool IsEmpty;
        public string Key;
        public string Name;
        public Sprite Icon;
        public Color IconColor;
        public float Mass;
        public float Temperature;
        public byte DiseaseIdx;
        public int DiseaseCount;
    }

    public static List<PipeRow> Collect(CensusSelection selection)
    {
        var sums = new Dictionary<string, Sum>();
        var cells = selection.Cells.OrderBy(c => c).ToList();
        foreach (var kind in new[] { PipeKind.Liquid, PipeKind.Gas, PipeKind.Solid })
        {
            foreach (var cell in cells)
            {
                if (!HasPipe(kind, cell))
                {
                    continue;
                }

                var content = Read(kind, cell);
                var network = NetworkId(kind, cell);
                var key = (int)kind + ":" + (content.IsEmpty ? "" : content.Key) + ":" + network;
                if (!sums.TryGetValue(key, out var sum))
                {
                    sum = new Sum
                    {
                        Row = new PipeRow
                        {
                            Kind = kind,
                            ContentKey = content.IsEmpty ? "" : content.Key,
                            IsEmpty = content.IsEmpty,
                            Name = content.IsEmpty ? STRINGS.UI.FORMATS.EMPTY_CONTENT.text : content.Name,
                            Icon = content.Icon,
                            IconColor = content.IconColor,
                            Network = network,
                        },
                    };
                    sums[key] = sum;
                }

                var row = sum.Row;
                row.Segments++;
                var tile = TileObject(kind, cell);
                var pipeTile = new PipeTile
                {
                    Cell = cell,
                    IsEmpty = content.IsEmpty,
                    Mass = content.IsEmpty ? 0f : content.Mass,
                    Fill = content.IsEmpty ? -1f : Mathf.Clamp01(content.Mass / Capacity(kind)),
                    Temperature = content.IsEmpty ? -1f : content.Temperature,
                    DiseaseIdx = content.DiseaseIdx,
                    DiseaseCount = content.DiseaseCount,
                    Target = tile,
                };
                row.Tiles.Add(pipeTile);
                if (content.IsEmpty)
                {
                    if (row.Target == null)
                    {
                        row.Target = tile;
                    }

                    continue;
                }

                row.Mass += content.Mass;
                sum.WeightedTemperature += (double)content.Mass * content.Temperature;
                row.MinTemperature = row.MinTemperature < 0f ? content.Temperature : Mathf.Min(row.MinTemperature, content.Temperature);
                row.MaxTemperature = Mathf.Max(row.MaxTemperature, content.Temperature);
                if (content.DiseaseCount > 0 && content.DiseaseIdx != byte.MaxValue)
                {
                    row.Germs.TryGetValue(content.DiseaseIdx, out var count);
                    row.Germs[content.DiseaseIdx] = count + content.DiseaseCount;
                    row.GermTotal += content.DiseaseCount;
                }

                if (content.Mass > row.TargetMass)
                {
                    row.TargetMass = content.Mass;
                    row.Target = tile;
                }
            }
        }

        foreach (var sum in sums.Values)
        {
            var row = sum.Row;
            row.Tiles.Sort((a, b) => b.Mass.CompareTo(a.Mass));
            if (row.IsEmpty)
            {
                continue;
            }

            row.Temperature = row.Mass > 0f ? (float)(sum.WeightedTemperature / row.Mass) : row.MinTemperature;
            row.Fill = Mathf.Clamp01(row.Mass / (row.Segments * Capacity(row.Kind)));
        }

        var result = sums.Values.Select(s => s.Row).ToList();
        NumberNetworks(result);
        return result;
    }

    // When the same content (of the same kind of pipe) sits in several networks, tells them apart by number, the one with
    // the most tiles first. A content in one network only stays unnumbered.
    internal static void NumberNetworks(List<PipeRow> rows)
    {
        foreach (var group in rows.GroupBy(r => (int)r.Kind + ":" + (r.IsEmpty ? "" : r.ContentKey)))
        {
            var members = group.OrderByDescending(r => r.Segments).ThenBy(r => r.Network).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                members[i].NetworkNumber = members.Count > 1 ? i + 1 : 0;
            }
        }
    }

    internal static bool HasPipe(PipeKind kind, int cell)
    {
        var game = Game.Instance;
        switch (kind)
        {
            case PipeKind.Liquid: return game.liquidConduitFlow.HasConduit(cell);
            case PipeKind.Gas: return game.gasConduitFlow.HasConduit(cell);
            default: return game.solidConduitFlow.HasConduit(cell);
        }
    }

    internal static GameObject TileObject(PipeKind kind, int cell)
    {
        var layer = kind == PipeKind.Liquid ? ObjectLayer.LiquidConduit : kind == PipeKind.Gas ? ObjectLayer.GasConduit : ObjectLayer.SolidConduit;
        return Grid.Objects[cell, (int)layer];
    }

    private static int NetworkId(PipeKind kind, int cell)
    {
        var game = Game.Instance;
        var network = kind == PipeKind.Liquid
            ? game.liquidConduitSystem.GetNetworkForCell(cell)
            : kind == PipeKind.Gas
                ? game.gasConduitSystem.GetNetworkForCell(cell)
                : game.solidConduitSystem.GetNetworkForCell(cell);
        return network == null ? -1 : network.id;
    }

    private static Content Read(PipeKind kind, int cell)
    {
        if (kind == PipeKind.Solid)
        {
            return ReadSolid(cell);
        }

        var flow = kind == PipeKind.Liquid ? Game.Instance.liquidConduitFlow : Game.Instance.gasConduitFlow;
        var contents = flow.GetContents(cell);
        if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
        {
            return new Content { IsEmpty = true };
        }

        var element = ElementLoader.FindElementByHash(contents.element);
        var content = new Content
        {
            Key = contents.element.ToString(),
            Name = element != null ? Texts.StripLinks(element.name) : contents.element.ToString(),
            IconColor = Color.white,
            Mass = contents.mass,
            Temperature = contents.temperature,
            DiseaseIdx = contents.diseaseIdx,
            DiseaseCount = contents.diseaseCount,
        };
        if (element != null)
        {
            var icon = Def.GetUISprite(element);
            if (icon != null)
            {
                content.Icon = icon.first;
                content.IconColor = icon.second;
            }
        }

        return content;
    }

    private static Content ReadSolid(int cell)
    {
        var flow = Game.Instance.solidConduitFlow;
        var contents = flow.GetContents(cell);
        var pickupable = contents.pickupableHandle.IsValid() ? flow.GetPickupable(contents.pickupableHandle) : null;
        if (pickupable == null)
        {
            return new Content { IsEmpty = true };
        }

        var element = pickupable.PrimaryElement;
        var selectable = pickupable.GetComponent<KSelectable>();
        var name = selectable != null ? selectable.GetName() : pickupable.name;
        var content = new Content
        {
            Key = pickupable.KPrefabID.PrefabTag.ToString(),
            Name = Texts.StripLinks(name),
            IconColor = Color.white,
            Mass = element != null ? element.Mass : pickupable.TotalAmount,
            Temperature = element != null ? element.Temperature : 0f,
            DiseaseIdx = element != null ? element.DiseaseIdx : byte.MaxValue,
            DiseaseCount = element != null ? element.DiseaseCount : 0,
        };
        var icon = Def.GetUISprite(pickupable.gameObject);
        if (icon != null)
        {
            content.Icon = icon.first;
            content.IconColor = icon.second;
        }

        return content;
    }
}
