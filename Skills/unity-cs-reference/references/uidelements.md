# UIElements / UI Toolkit Core

Use this file when LUX UI work needs VisualElement behavior, UXML/USS parsing, bindings, panels, event dispatch, or debugger internals.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/UIElements/` | UI Toolkit core runtime and Editor-shared implementation |
| `Modules/UIElements/Core/` | VisualElement tree, layout, styles, panels, event dispatch |
| `Modules/UIElements/Controls/` | built-in controls and base field patterns |
| `Modules/UIElements/UXML/` | UXML traits, factories, template loading |
| `Modules/UIElements/StyleSheets/` | USS parsing, style resolution, variables |
| `Modules/UIElements/Bindings/` | data binding and SerializedObject binding behavior |
| `Modules/UIElements/Debugger/` | UI Toolkit debugger and inspection patterns |
| `Modules/UIElements/InputSystem/` | input routing where present |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Custom element behavior | `VisualElement` | lifecycle, hierarchy callbacks, style dirtiness |
| Custom control pattern | `BaseField`, `TextField`, `Button` | value change events and binding hooks |
| UXML support | `UxmlFactory`, `UxmlTraits`, `UxmlSerializedData` | Unity version-specific authoring path |
| USS behavior | `StyleSheet`, `StyleVariable`, `StyleProperty` | supported value types and resolution order |
| Panel lifecycle | `Panel`, `BaseVisualElementPanel` | attach/detach and scheduler behavior |
| Event routing | `EventBase`, `CallbackEventHandler`, `EventDispatcher` | trickle-down/bubble-up order |
| Binding to serialized data | `SerializedObjectBinding`, `BindingExtensions` | update timing and path rules |
| Debug UI | `Debugger`, `PanelDebugger` | inspection surface and internal state names |

## VisualElement Checklist

- Check whether the element is attached to a panel before scheduling or measuring.
- Use class names and USS variables for styling; avoid hard-coded style churn.
- Register callbacks once and unregister on detach or window disable when needed.
- Use `ChangeEvent<T>` patterns for value changes.
- Use `userData` sparingly; prefer typed LUX controller state.

## UXML/USS Checklist

- Inspect UXML trait names before writing `.uxml` attributes.
- Confirm USS property support in the target Unity version.
- Keep LUX USS selectors stable and local to the window root.
- Avoid depending on internal debugger-only APIs.

## Binding Checklist

- Use `SerializedObject` binding when editing Unity objects or settings assets.
- Verify binding path names against serialized field names, not display labels.
- Call update/apply methods only where Unity patterns require them.
- Treat binding refresh timing as Editor-frame dependent.

## LUX Fit

- Use UI Toolkit for Lux Workbench, dashboard-like panels, status indicators, and structured command UIs.
- Use IMGUI only when matching existing LUX code or Unity API requires it.
- For web dashboard UI, use React code under `RustGateway~/ui-src/`; do not mix UI Toolkit assumptions into React.

## License Reminder

- Extract signatures and behavior only.
- Do not duplicate Unity control implementations or USS parser logic.
