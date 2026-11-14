# CLAUDE.md

ONI mod（Census シリーズ。兄弟は `../CritterCensus`、`../PlantCensus`）。Build: `dotnet build` (Debug copies to `ModInstallPath` from `Local.Build.props`; Release creates `release/PipeCensus`). 共通事項は HQ（親フォルダ）の `CLAUDE.md`。仕様は `SPEC.md`、機能の概要は `README.md`。

- 構成は `PlantCensus` を写している。`Ui/`（`UiFactory`、`UiStyle`、`ModalScreens`、`ToolIcon`）と `Selection/` は同じ内容で、名前空間と名前だけが違う。直すときは兄弟側にも同じ問題がないか見る。`Ui/CensusScreen.cs` は、共通の部分（キャンバス、左ペイン、トグル、ヘッダー、並べ替えの切り替え、リスト）を `PlantCensus` から切り出して作った。
- 集計は `Core/PipeCollector.cs`。範囲のセルごとに、液体は `Game.Instance.liquidConduitFlow`、気体は `gasConduitFlow`、固体は `solidConduitFlow` の `HasConduit` / `GetContents` を読む。固体の内容物は `Pickupable`（キーは `PrefabTag`）。
- **作った直後の配管に `AddElement` しても中身が消える**ことがある。ゲームが配管を登録する前に入れたものは捨てられる。テストは、配管を作ってから 2 ティック待って入れる。
- 1 区画の容量は `ConduitFlow.MAX_LIQUID_MASS`（10）、`MAX_GAS_MASS`（1）、`SolidConduitFlow.MAX_SOLID_MASS`（20）。満たされ具合の計算に使う。
- ツールボタンの位置は「配管を空にする」ツール（`EmptyPipeTool`）の次。アイコンは `Ui/ToolIcon.cs` が `ModAssets/tool_icon.png` を読む（ファイルがなければバニラの `icon_action_empty_pipes` を借りる）。元画像は HQ の `_Incoming/PipeCensus/2026-11-14-icon-tool/`（採用は案 A のエルボ配管）、加工は `tools/asset.py fix --trim --pad 6 --size 128x128`。
- 行は（種類, 内容物, ネットワーク）。同じ内容物が複数のネットワークにあるときだけ `PipeCollector.NumberNetworks` が番号を振る（`PipeRow.NetworkNumber`、区画数の多い順）。各行は `Tiles`（`PipeTile`、質量の多い順）を持ち、`Ui/CensusScreen.cs` の「+」で開く。20 区画を超える行は、`Ui/NestedScrollRect.cs`（端まで来たらホイールを外側の ScrollRect へ渡す）の窓に入れる。開閉は `expanded`（`PipeRow` の集合）で覚える。
- ネットワーク: `Selection/PipeNetworks.cs`。ゲームの `FlowUtilityNetwork` は配管の区画の一覧を持たない（`conduits` は空のまま、数だけ `conduitCount` にある）ので、ネットワークの区画は、アクティブな世界の全セルを見て、`GetNetworkForCell` が同じネットワークを返す区画を集める。ハイライト（`Ui/NetworkHighlight.cs`）は、ホバーしたネットワークが変わったときと、1 秒ごとにだけ走査する。Shift は `Input.GetKey` で読む（ハーネスから押せないので、テストは `PipeNetworks.TestShiftHeld`）。
- ハイライトは `KBatchedAnimController.HighlightColour`。配管オーバーレイの配管は色づけされているので、効きは控えめ（端の輪郭が光る程度）。色は `Color32(60, 170, 200, 0)`。
- ゲームの文字列クラス `STRINGS` は、この Mod の `STRINGS` に隠れる。ゲーム側を使うときは `global::STRINGS`。
- `CritterCensus`、`PlantCensus` と同じ対象（`GameInputMapping.CompareActionKeyCodes`、`GameUtil.GetHotkeyString`、`KButtonEvent.IsAction`）にパッチを当てている。どれも自分の条件に合わないときは元の処理へ通すので、三つとも入れても動く。

## 実機検証
ONI 実機で検証するには Gravitas.TestHarness を使う。テストは `[HarnessTest]` を付けた static メソッドとして書く（`PipeCensus/HarnessTests.cs`、`#if DEBUG`）。
実行: `python C:/Users/dha_1/ONI-Modding/GravitasTestHarness/Gravitas.TestProbe/probe.py run --project . --summary --tests '{"tags":["pipecensus"]}'`
ゲームを起動し本番の mods.json を書き換えるので、実行前にユーザーに確認する。
初回登録（`--setup`）は 2026-11-14 に済んでいる。別の環境に初めて置くときは、DLL 配置 → `probe.py run --project . --setup` → 通常の probe、の順にする。
詳細は harness repo の `README.md` と `protocol.md`。

テストは、ツールバーのボタンのクリック → ドラッグ → 行クリックでの選択まで、ハーネスの入力再現（`Play`）で実際の経路を通す。キー入力（`Play.Press`）でホットキーなしのボタンの回帰も見ている。
テストが見ていないもの: 画面の見た目（デモの画像で確認）、ハイライトの目立ち方（実プレイでの見え方）、専用アイコンの実寸での見え方。

見た目を確かめるときは、デモ用の場面を作って撮る（ホバーのハイライトは、`DemoHover` を `DemoFill` のあとに足し、2 秒待ってから撮る）:

```
python C:/Users/dha_1/ONI-Modding/GravitasTestHarness/Gravitas.TestProbe/probe.py run --project . --steps '[["POST","/invoke",{"method":"PipeCensus.HarnessTests.DemoBuild"}],["POST","/time/advance",{"seconds":2}],["POST","/invoke",{"method":"PipeCensus.HarnessTests.DemoFill"}],["POST","/invoke",{"method":"PipeCensus.HarnessTests.DemoOpen"}],["POST","/screenshot",{"name":"census"}]]'
```
