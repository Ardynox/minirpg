# B12 · 生活细节 + 联机色带（style lock，P2）

继承 `Artifacts/style_locks_B0.md`。

## 磁盘占位（初版）

- `Assets/Art/Placeholders/detail/`：**20** 张（脚印 / 血迹 / 污渍 / glow / 门开 / 烟柱），`Tools/banana/b12_detail_mp_fill_local.py`。
- `Assets/Art/Placeholders/mp/`：**8** 张 `player_slot_0..7.png`（色带条占位）。
- **Banana 换血**：`python Tools/banana/b12_detail_mp_driver.py`（覆盖**同名路径**）。
- **接线**：当前无 `res://` 引用；需要脚印 / 联机顶标时再在渲染或 UI 中 `ResourceLoader` 接入。

开批前按身份卡 B12 清单问 Gemini（additive / 脚印单枚 vs 轨迹等），结论写本节「批复」段。
