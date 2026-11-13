# PipeCensus

Oxygen Not Included mod. Sums up what is in the pipes of an area, per kind of pipe and content.

Part of the Census series (see [Critter Census](https://github.com/dha-lo-jd/oni-mod-CritterCensus) and [Plant Census](https://github.com/dha-lo-jd/oni-mod-PlantCensus)).

## What it does

- Adds a **Pipe Census** tool next to the Empty Pipe tool. Drag over an area to summarize its pipes.
- Covers liquid pipes, gas pipes and conveyor rails. One row is one kind of pipe holding one thing (liquid water, gas oxygen,
  a conveyed item...), plus one row of empty tiles per kind.
- Each row shows the total mass, how many pipe tiles hold it, how full those tiles are, the mass-weighted temperature (with the
  range when it varies), the germs in it, and how many separate pipe networks the tiles belong to.
- In a pipe overlay (liquid, gas or conveyor), **Shift + click a pipe** to take the census of its whole network. A plain click selects the pipe as usual.
- In a pipe overlay, hovering a pipe lights up its whole network.
- Filter by kind of pipe and by content, sort by any column, click a row to jump to the pipe that holds the most of it.

It only shows information: nothing in the colony changes. The list is a snapshot taken when the window opens.

English and Japanese are supported. The design is described in [SPEC.md](SPEC.md) (Japanese).

## Build

Copy `PipeCensus/Local.Build.props.example` to `Local.Build.props`, set the paths, then `dotnet build`.
Debug builds are copied to `ModInstallPath`; Release builds produce `release/PipeCensus`.
