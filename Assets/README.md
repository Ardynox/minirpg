# MiniRPG 资源目录

> 这份文档只说明外部资源包现在放在哪里、为什么这样分层、哪些资源暂时不在这里。

---

## 当前目录

```text
Assets/
├── Branding/
│   ├── icon.svg
│   └── icon.svg.import
├── Art/
│   └── Tilesets/
│       └── FantasyKingdom/
│           ├── FantasyKingdomTileSet.tres
│           └── FantasyKingdomTileset_Godot/
├── UI/
│   └── Themes/
│       └── UITheme.tres
└── Characters/
    └── Spine/
        └── Balin/
```

## 分层规则

- `Assets/Art/` 放地图、美术包、tileset、atlas 这类外部视觉资源。
- `Assets/Branding/` 放项目图标、品牌图形这类项目级视觉资源。
- `Assets/Characters/` 放角色相关资源；当前 `Spine/` 子目录只放 Spine 角色包。
- `Assets/UI/` 放跨场景共用的 UI 资源；当前 `Themes/` 子目录只放主题资源。
- 外部资源包优先按“领域 -> 类型 -> 具体包名”分层，不直接散在项目根目录。
- 项目运行时数据仍放在 [`../Data`](../Data)，不要和外部美术资源混放。

## 当前资源落点

- [`Branding/icon.svg`](./Branding/icon.svg)
  当前项目图标资源。`project.godot` 从这里读取 `config/icon`。
- [`Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres`](./Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres)
  当前 TileMap 渲染入口使用的 TileSet 资源。
- `Art/Tilesets/FantasyKingdom/FantasyKingdomTileset_Godot/`
  Fantasy Kingdom 原始导入包。这个目录体积大，继续按 `.gitignore` 规则不进版本库。
- [`Characters/Spine/Balin`](./Characters/Spine/Balin)
  当前玩家 Spine 角色资源目录，场景和 `Data/entity_render.json` 都从这里取 `atlas/skel`。
- [`UI/Themes/UITheme.tres`](./UI/Themes/UITheme.tres)
  当前主场景、按键面板和运行时浮动面板共用的主题资源。

## 维护约定

- 新增外部资源时，优先放进 `Assets/` 对应领域目录，不再默认堆到项目根目录。
- 如果只是项目内部生成的小型运行时资源，先评估它是否更适合留在当前模块附近，而不是机械迁进 `Assets/`。
- 改动资源目录后，要同步检查场景、`.tres`、`Data/*.json`、工具脚本和相关文档引用。
