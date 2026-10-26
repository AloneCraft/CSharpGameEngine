# ADR-0082 — Virtual directional shadows and matched comparison

- Status: Accepted (user-approved implementation plan, 2026-10-25)
- Features: F-L4-020 (virtual directional shadows), F-L0-007, F-L4-014

## Context
The existing directional shadow uses four 512-pixel cascades in a fixed atlas. Virtual shadow maps are absent. TopDown's non-uniformly scaled ramps also expose an incorrect normal transform (`normal * World`); its contribution to the reported dark ramp must be measured separately.

## Decision
Implement four stable directional clipmaps, 4096 virtual texels per level, 128-texel pages and a default budget of 1024 physical pages. CPU conservative receiver bounds request pages; cached depth is rendered only for new or invalidated pages. Moving/removed casters invalidate their old and new footprints, skinned casters invalidate each frame, and light/depth-origin changes invalidate the cache. Missing pages sample the existing 200-m cascades. Page-table uploads and constant buffers use fence-protected frame slots.

Expose Cascaded/Virtual policy and budget through an optional environment component without changing LightShadow ownership. Keep Cascaded as the default. Compare one world and synchronized camera/time/exposure in equal left/right views (left cascades, right virtual). Publish page counters, captures and timing; measure each mode alone for performance evidence.

Correct normals by inverse transpose and orthogonalize tangents. Reproduce TopDown before/after and isolate shadows, AO and indirect lighting before attributing its darkness. Never tune global exposure or ambient light to hide a geometry/visibility bug.

## Boundaries
Point, spot, area and emissive shadows retain their existing backend. This is software page virtualization over an ordinary D3D12 depth texture, not hardware tiled-resource residency. GPU feedback and indirect raster scheduling are outside this first implementation. Shader Graph remains authoritative; runtime uses offline artifacts.

## Verification
Test geometric normal/tangent agreement, negative coordinates and page borders, stable-cache reuse, camera motion, moving/removed/skinned casters, light rotation, exhaustion/fallback, Standard/Radial, switching/resizing/recovery, WARP pixels and zero D3D12 errors. A normal-budget comparison must have zero missing pages. Report hardware timing separately from WARP correctness. Preserve existing local work.
