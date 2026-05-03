# Reference: gpm.unity

**Source**: [https://github.com/islee23520/gpm.unity](https://github.com/islee23520/gpm.unity) (Fork of NHN Game Package Manager)

## Relationship
LUX's addon management system references the patterns established by `gpm.unity` for managing internal Unity packages and services. While LUX uses a Rust-based CLI for backend operations, the Unity-side UI and discovery patterns should feel familiar to users of GPM.

## Key Patterns

### Manager UI
- **Service List**: A vertical list of available services/addons.
- **Action Buttons**: Each item should have clear `Install`, `Uninstall`, and `Update` buttons based on its current state.
- **Info Panel**: Selecting a service displays detailed information:
  - Version and Unity support version.
  - Documentation and License links.
  - Description of the service.

### Service Discovery
- **Remote Listing**: Uses a `servicelist.xml` (or equivalent JSON) to discover available services from a remote repository.
- **Local Configuration**: A `config.json` stores the manager's settings and local state.

### Package Structure
- **Release Directory**: Services are typically bundled as separate packages in a `release/` directory.
- **Manifests**: Each service/addon must have a manifest (e.g., `addon.json`) defining its identity and dependencies.

## Applicable LUX Components

### LuxAddonManagerWindow
The Unity Editor window (`Window > Linalab > Lux > Addon Manager`) should follow the GPM layout: service list on the left, details on the right.

### Addons/ Directory
Optional features are stored in the `Addons/` directory within the package. This mirrors the GPM `release/` pattern.

### `lux addon` CLI
The Rust CLI provides the underlying logic for:
- `lux addon list`: Discovery and status.
- `lux addon install/remove`: Package manipulation.

## When to Reference
Load this document when:
- Improving the Addon Manager UI layout or UX.
- Modifying the addon discovery logic (how LUX finds available addons).
- Updating the `addon.json` manifest format.
- Implementing multi-language support for the addon UI.
