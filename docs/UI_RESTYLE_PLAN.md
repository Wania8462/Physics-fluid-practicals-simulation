# uGUI restyle: plan and agent prompt

Hand this whole file to the agent that does the work.

## Prompt

Restyle the existing uGUI of this Unity 6.3 (URP) project so it looks modern and consistent. **Do not migrate to UI Toolkit and do not change any layout, behaviour or serialized references.** Only colours, sprites, fonts, spacing details and hover/press feedback change. Read `docs/ARCHITECTURE.md` (UI section) first.

### Hard constraints

- Keep every existing script path, class name and `[SerializeField]` field name, so scene and prefab references survive. Add new files, don't rename or move old ones (GUIDs are in the `.meta` files).
- Keep `Field.GetHeight()` pixel heights and the `FieldUI` / `Window` layout maths unchanged. If a restyle needs a different size, flag it instead of changing it.
- `WindowManager.Create…Window`, `OnSideMenuClick` and `OnSettingsClick` are wired through Inspector `OnClick` events. Don't touch them.
- Keep drag, drop-to-group, minimise, close and rename behaviour exactly as it is.
- Keep the repo's code style: `///` summary comments on members, short methods, no unnecessary abstraction.
- Work on branch `Dev`. Commit in small steps (see Steps). Don't open a pull request unless asked.
- Out of scope: simulation, shaders, `Visualiser` world-space labels (only reuse the theme font and colours), the scene background.

### Approach (decided)

Apply the style **at runtime from one central theme**, not by hand-editing prefab and scene YAML. This catches the objects that `FieldUI` creates in code (separator, foldout, button row, graph and its labels) and lets `AppSettings.theme` switch palettes.

1. `UITheme` (static class, `Assets/Interaction/UITheme.cs`)
   - Holds the palette for `Theme.Dark` and `Theme.Light`, picked from `AppSettings.theme`.
   - Tokens: `background`, `panel`, `titleBar`, `accent`, `textPrimary`, `textSecondary`, `textDisabled`, `warning`, `border`, `graphLines[4]`.
   - Also: `font` (TMP_FontAsset loaded from `Resources`), `sizeTitle`, `sizeBody`, `sizeCaption`, `radius`, `spacing`.
   - Colourblind filter (`AppSettings.colorblindFilter`): `graphLines` must stay distinguishable, so give each filter its own graph palette.
2. `UISprites` (static class, `Assets/Interaction/UISprites.cs`)
   - Builds and caches procedural sprites: rounded rectangle with 9-slice borders, circle (slider handle, toggle knob), chevron (dropdown and foldout), check mark, cross.
   - Use `Sprite.Create` with the border set and `Image.Type.Sliced`. Mipmaps off, bilinear, so the edges are smooth.
3. `UIStyler` (static class, `Assets/Interaction/UIStyler.cs`)
   - `Apply(Transform root)` walks the hierarchy and styles, by component type:
     `Slider`, `Toggle`, `TMP_Dropdown`, `TMP_InputField`, `Button`, `TMP_Text`, and panel `Image`s.
   - Panels and title bars are identified by an optional `UIRole` component (`Panel`, `TitleBar`, `Card`, `Backdrop`) on the object. Anything without a role is left alone.
   - `Button` and `Toggle` get a `ColorBlock` from the palette: normal, hover (+10% lighter), pressed (−10%), disabled (40% opacity), fade duration 0.15 s.
4. `SceneStyler` (MonoBehaviour, `Assets/Interaction/SceneStyler.cs`)
   - One per scene on the Canvas. Calls `UIStyler.Apply` in `Awake` for the static scene UI (main menu buttons, side menu, settings card).

### Steps and commits

1. **Theme, sprites, styler.** Add the three static classes and `UIRole`. Compile clean. Commit.
2. **Windows.** Call `UIStyler.Apply` from `Window.Awake` and `FieldUI.Awake`. Replace the hard-coded colours in `FieldUI` (`CreateSeparator`, `CreateFoldoutButton`, `CreateButtons`, `CreateGraphLabel`, `graph.frameColor = label.color`) with theme tokens. Replace the scale-up highlight in `Window.Highlight` with scale-up **plus** an accent outline (an `Outline` or a 2 px sprite border). Commit.
3. **Graph.** In `LineGraph`, replace `LineColors = { red, green }` and the black frame with `UITheme` colours. Add a panel-coloured background fill, 4 faint horizontal gridlines and an antialiased look if it is cheap (1 px lines are fine). Keep the existing draw API. Commit.
4. **Menus.** Add `SceneStyler` to `MainMenu`, `SettingsScene`, `BuoyancyPractical` and `WavesPractical`. Style `SettingsScript`'s slider and dropdowns. Commit.
5. **Polish.** Soft shadow and 1 px border on windows (`UIStyler`, sprite based), text sizes standardised to the theme tokens, a title bar style that matches the rest. Commit.
6. **Docs.** Add a short "UI theme" section to `docs/ARCHITECTURE.md`: where the palette lives, how to add a new themed widget. Commit.

### Needs the Unity Editor (do or ask the user)

- Import the font (`.ttf`) and create the TMP Font Asset (Window → TextMeshPro → Font Asset Creator, Atlas 2048, SDF). Save it as `Assets/Resources/UIFont.asset` so `UITheme` can load it.
- Check that every `UnityEngine.UI` and `TMPro` component in `Window.prefab`, `Field.prefab` and the four scenes still references what it did (nothing should be missing).
- Play each scene and run the checklist below.

### Design decisions (edit before handing over)

| Token | Value |
|---|---|
| Theme | Dark default, light palette also built |
| Background | `#12151C` |
| Panel | `#1C212B` |
| Title bar | `#252B38` |
| Accent | `#3B82F6` |
| Text primary / secondary / disabled | `#E6E9EF` / `#8B93A5` / `#4B5263` |
| Warning | `#EF4444` |
| Graph lines | blue `#3B82F6`, orange `#F59E0B`, green `#22C55E`, pink `#EC4899` |
| Font | Inter |
| Sizes (1920×1080 reference) | title 28, body 22, caption 18 |
| Corner radius / spacing unit | 8 px / 8 px |
| Window shadow and border | soft shadow plus 1 px border `#2F3645` |
| Hover / pressed / disabled | +10% / −10% / 40% opacity |
| Animation | 0.15 s fades |
| Icons | generated in code (chevron, check, cross) |

### Acceptance checklist

- [ ] No scene or prefab has a missing reference. Console shows no new errors or warnings.
- [ ] All four scenes use the same palette, font and corner radius.
- [ ] Every window field type looks right: Numeric, Dropdown, Toggle, Readout, Separator, Foldout, Graph, Buttons.
- [ ] Dragging, grouping (highlight shows), minimising, closing and renaming work as before.
- [ ] Text is readable on every background in both themes.
- [ ] Switching `AppSettings.theme` on the settings screen switches the palette on the next scene load.
- [ ] Graph lines are distinguishable with each colourblind filter.
- [ ] Layout is unchanged at 1920×1080 and at 1280×720.
- [ ] No per-frame allocations were added (sprites cached, no `Apply` in `Update`).
