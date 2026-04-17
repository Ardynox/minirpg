# Audio Attribution

这个文件对应 `Assets/Audio/audio_manifest.json` 的人工可读版本，方便后续筛选、复查和发行时整理 credits。

## 外部采样资源

当前已入库的外部采样都允许不署名使用，但仍然必须保留来源与许可证记录。

- `kenney_music_jingles`
  - 路径：`Assets/Audio/Library/Kenney/MusicJingles`
  - 用途：UI 确认、失败、奖励、转场短 stinger
  - 来源：Kenney `Music Jingles` — <https://kenney.nl/assets/music-jingles>
  - 许可证：CC0 1.0
  - 是否需署名：否
- `forest_ambience`
  - 路径：`Assets/Audio/Library/OpenGameArt/Ambience/forest_ambience.mp3`
  - 用途：林地 / 旅行 / 白天环境底噪
  - 来源：TinyWorlds `Forest Ambience` — <https://opengameart.org/content/forest-ambience>
  - 许可证：CC0
  - 是否需署名：否
- `loopable_dungeon_ambience`
  - 路径：`Assets/Audio/Library/OpenGameArt/Ambience/dungeon_ambient_1.ogg`
  - 用途：洞穴 / 地牢 / 地下探索环境底噪
  - 来源：JaggedStone `Loopable Dungeon Ambience` — <https://opengameart.org/content/loopable-dungeon-ambience>
  - 许可证：CC0
  - 是否需署名：否
- `dark_rainy_night_ambience`
  - 路径：`Assets/Audio/Library/OpenGameArt/Ambience/dark_rainy_night_ambience.ogg`
  - 用途：夜雨、风暴、危险 buildup
  - 来源：kindland `rain and thunders` — <https://opengameart.org/content/rain-and-thunders>
  - 许可证：CC0
  - 是否需署名：否
- `prepare_your_swords`
  - 路径：`Assets/Audio/Library/OpenGameArt/Music/prepare_your_swords.ogg`
  - 用途：遭遇战开场、威胁升高、短战斗 cue
  - 来源：bojidar-bg `Prepare your swords` — <https://opengameart.org/content/prepare-your-swords>
  - 许可证：CC0
  - 是否需署名：否
- `underwater_theme_ii`
  - 路径：`Assets/Audio/Library/OpenGameArt/Music/underwater_theme_ii.ogg`
  - 用途：平静探索、海边 / 潮湿遗迹氛围乐
  - 来源：CleytonKauffman `Underwater Theme II` — <https://opengameart.org/content/underwater-theme-ii>
  - 许可证：CC0
  - 是否需署名：否

## 程序化运行时样本

这些文件当前是仓库内程序化音乐系统直接读取的样本输出，不需要外部署名；如果以后改为基于外部 SoundFont 或别的授权素材重新导出，先在 `ProceduralSources/` 记录源，再改这里和 manifest。

- `sampler_harp_placeholders`
  - 路径：`Assets/Audio/Samples/harp`
  - 来源：`Tools/extract_samples.py`
  - 许可证：仓库内生成占位样本
  - 是否需署名：否
- `sampler_lute_placeholders`
  - 路径：`Assets/Audio/Samples/lute`
  - 来源：`Tools/extract_samples.py`
  - 许可证：仓库内生成占位样本
  - 是否需署名：否
- `sampler_recorder_placeholders`
  - 路径：`Assets/Audio/Samples/recorder`
  - 来源：`Tools/extract_samples.py`
  - 许可证：仓库内生成占位样本
  - 是否需署名：否
- `sampler_viol_placeholders`
  - 路径：`Assets/Audio/Samples/viol`
  - 来源：`Tools/extract_samples.py`
  - 许可证：仓库内生成占位样本
  - 是否需署名：否
- `sampler_percussion_placeholders`
  - 路径：`Assets/Audio/Samples/percussion`
  - 来源：`Tools/extract_samples.py`
  - 许可证：仓库内生成占位样本
  - 是否需署名：否

## 维护约定

- 新增外部资源时，如果许可证要求署名，单独新增一个“需署名资源”小节，避免发行时漏记。
- 任何外部采样如果没有来源页或许可证信息，不要接入运行时，也不要写进 `Samples/`。
