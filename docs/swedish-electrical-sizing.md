# Swedish electrical sizing support

EutherWire treats conduit fill and current-carrying capacity as separate checks.
Neither result replaces verification and completion by an authorised electrical
installation company.

The default rule profile in schema 8 identifies the intended reference set:

- `SS 436 40 00`, edition `4:2023`;
- `SEK Handbok 421`, edition `5.1:2026`.

The identifier records which rules were intended when a project was evaluated.
It does not claim certification or embed copyrighted standard tables.

## Mechanical conduit fill

The calculation uses circular occupied area, which reduces to the sum of each
outside diameter squared divided by the conduit inner diameter squared. A
complete result therefore requires:

- the actual conduit inner diameter, not merely its nominal product size;
- overall outside diameter for a sheathed cable; or
- insulated outside diameter for every loose FK/RK conductor.

Unknown diameters remain explicitly unknown. EutherWire does not infer a
thermal current limit from physical fill.

Schema 10 stores a traceable conduit product, nominal size, and actual inner
diameter separately. The initial catalog deliberately contains only Pipelife
Halovolt 750N products in the common 16, 20, and 25 mm sizes. Their source URLs
and E-numbers live beside the dimensions in `catalog/electrical-products.toml`.
Changing a `16 mm` product selection therefore never silently changes the
diameter used by the fill calculation.

## Thermal planning check

For a power circuit, the model can store:

- nominal voltage and phase count;
- conductor material and number of loaded conductors;
- design current `Ib`;
- protective-device rating and characteristic `In`;
- verified reference current-carrying capacity;
- ambient, grouping, and thermal-insulation correction factors;
- a human-readable source for the reference value.

The corrected capacity `Iz` is the reference capacity multiplied by the three
explicit correction factors. The initial planning rule passes only when:

```text
Ib <= In <= Iz
```

If any required value or its source is absent, the result is `UNKNOWN`, not an
optimistic estimate. A later licensed rule-table provider can populate the
reference capacity while retaining the same calculation and provenance model.

Voltage drop, fault-loop impedance, automatic disconnection, short-circuit
withstand, harmonics, manufacturer restrictions, pullability through bends,
and final electrician verification remain required future checks.

## Desktop workflow

Select a conduit to choose its nominal product size with `-` and `+`, then type
the manufacturer's actual inner diameter in `INNER MM`. The latter is the only
diameter used for physical fill.

Select a cable and press `DESIGN` to open the sizing page. The profile arrows
currently offer CAT6, EKRK 3G2.5, EKRK 5G6, FK 3G1.5, FK 3G2.5, FK 5G2.5, and
an FK lighting bundle. Enter:

- `IB`: design current;
- `IN`: protective-device rated current;
- `REF IZ`: verified reference current-carrying capacity;
- ambient, grouping, and thermal-insulation correction factors;
- insulated conductor OD for FK/RK, or overall cable OD for sheathed cable;
- a reference source identifying where the capacity value came from.

Changing the conductor profile clears `REF IZ` and its source because a value
for one product or cross-section must never silently follow another. `CABLE`
returns to installation status, measured length, and route editing.
