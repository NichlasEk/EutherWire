# Electrical cable and conductor model

Date: 2026-07-20

## Goal

EutherWire cables must describe the actual installation, not only a route and
colour. The model must support data cable, sheathed installation cable, loose
conductors in conduit, single-phase and three-phase circuits, protective earth,
neutral, switched lives, conduit fill, material lists, and later rule checks.

## Cable families

The first library should include:

- CAT6 and CAT6A, with shielding and PoE capability;
- EKRK and other multi-core installation cable;
- RK and FK as individual conductors placed in conduit;
- user-defined cable and conductor products.

Product names belong to the library. Electrical meaning belongs to structured
fields, so a renamed product does not break analysis.

## Conductors

Every cable or loose-conductor bundle contains explicit conductors:

```toml
[[cables.conductors]]
id = "l1"
function = "line_1"
colour = "brown"
area_mm2 = 2.5

[[cables.conductors]]
id = "n"
function = "neutral"
colour = "blue"
area_mm2 = 2.5

[[cables.conductors]]
id = "pe"
function = "protective_earth"
colour = "green_yellow"
area_mm2 = 2.5
```

Initial conductor functions:

- `line_1`, `line_2`, `line_3`;
- `neutral`;
- `protective_earth`;
- `switched_live` with an optional switch/control label;
- `control`, `dc_positive`, `dc_negative`, `data_pair`, and `spare`.

Each conductor stores stable ID, function, colour, cross-sectional area in
square millimetres, and optional terminal labels at both ends.

## Circuit presets

Presets create editable conductors rather than opaque strings:

- single phase: L1 + N + PE;
- three phase: L1 + L2 + L3 + N + PE;
- three phase without neutral: L1 + L2 + L3 + PE;
- lighting: L1 + N + PE plus one or more switched lives;
- loose FK/RK bundle: user chooses conductor count, function, colour, and area;
- CAT6: four twisted pairs with category, shielding, and PoE metadata.

The inspector should show both a quick preset and the resulting conductor list.
Changing the list must not silently change existing terminal assignments.

## Conduit contents and fill

A conduit contains cable IDs and/or loose-conductor bundle IDs. Fill analysis
must use product outer diameter when known. For loose RK/FK it uses each
conductor's insulated outer diameter, not only copper area. The report shows:

- total conductor/cable count;
- count grouped by area and function;
- known and unknown outside diameters;
- calculated occupied area and fill percentage;
- reserve capacity;
- a warning when required product dimensions are unknown;
- an error or warning when the configured fill limit is exceeded.

The material list should therefore be able to report entries such as:

```text
FK 1.5 mm² brown       18 m
FK 1.5 mm² blue        18 m
FK 1.5 mm² green/yellow 18 m
FK 1.5 mm² black       12 m  switched live
CAT6 U/UTP             24 m
Conduit 20 mm          18 m
```

## Validation

The first deterministic checks should detect:

- duplicate or missing conductor IDs;
- a circuit missing protective earth where its preset requires PE;
- single-phase circuits with multiple phase conductors;
- three-phase circuits missing a configured phase;
- neutral or protective-earth colour/function conflicts;
- switched lives without a destination/control label;
- conductor area incompatible with its terminal or circuit rule;
- loose RK/FK assigned without a conduit;
- unknown or excessive conduit fill;
- mismatched conductor assignments at the two cable ends.

These are planning diagnostics, not a replacement for electrical design,
installation rules, or verification by a qualified installer. Jurisdictional
rules must be separate versioned rule packs rather than hard-coded assumptions.

## Delivery order

1. **Implemented:** conductor, cable-product, and circuit-preset data types with schema 7.
2. **Implemented:** preserve old projects by migrating their current `CableKind` into a default
   product while leaving unknown conductor data explicit.
3. Add conductor editing and presets to the inspector.
4. **Partly implemented:** material rows for loose RK/FK; next extend conduit
   fill with insulated conductor diameters.
5. Add terminal assignment and deterministic diagnostics.
6. Expose the same conductor checklist in mobile installation mode.
