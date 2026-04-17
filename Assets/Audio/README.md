# Audio 资源管线（v1）

这个目录只解决一个问题：后续音频该放哪里、怎么留出处、怎么把程序化声音和外部采样分开。

## 目录职责

- `Library/`
  - 第三方 / 外部采样成品。
  - 新资源统一放到 `Library/<Category>/<SourcePack>/`。
  - `Category` 当前固定为 `UI/`、`Ambience/`、`Foley/`、`Combat/`、`Music/`。
  - 保留源包自带 `License.txt`、README、网页导出说明或截图。
- `Samples/`
  - 运行时直接消费的样本输出。
  - `Module/Audio/SamplerEngine.cs` 当前直接读 `res://Assets/Audio/Samples/`，不要把原始 SoundFont 或下载包直接丢这里。
- `ProceduralSources/`
  - 程序化声音的输入材料和配方。
  - `SoundFonts/` 放 `.sf2` 等源。
  - `MIDI/` 放旋律动机、节奏片段或测试短句。
  - `Recipes/` 放生成备注、参数文件或人工筛选记录。
- `audio_manifest.json`
  - 机器可校验的统一清单。
  - 每个外部采样或程序化样本集都要有一条记录。
- `ATTRIBUTION.md`
  - 面向发行和人工复查的署名 / 许可证摘要。

## 新资源怎么放

1. 外部采样先放到 `Library/<Category>/<SourcePack>/`，并保留原始许可证文件。
2. 在 `audio_manifest.json` 追加条目，至少写 `id`、`usage`、`source`、`license`、`attributionRequired`、`format`、`durationSec` 或 `durationNote`。
3. 外部资源同步更新 `ATTRIBUTION.md`。
4. 如果是给程序化系统用的源材料，把源文件放到 `ProceduralSources/`；只有运行时要直接加载的导出结果才放到 `Samples/`。
5. 运行 `python Tools/validate_audio_manifest.py`。

## 现状与 legacy

- 现有 `Library/Kenney/...` 和 `Library/OpenGameArt/...` 是旧的“来源优先”布局，先保留不挪；后续新增资源按新的 `Library/<Category>/<SourcePack>/` 结构落地。
- 现有 `Samples/*` 是当前程序化音乐的运行时样本库；如果后续改成基于外部 SoundFont 导出，必须先把源 SoundFont 记到 `ProceduralSources/`，再更新 manifest。
