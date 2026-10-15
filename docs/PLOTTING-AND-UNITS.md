# Plotting Architecture — UnitsNet to ScottPlot and Plotly.js

## Decision
Keep **physical quantities and measurement metadata** in the experiment runner and result store. Convert to plain finite numerical coordinates only in a separate plotting layer. A renderer is not allowed to reinterpret volts as ohms or silently change RMS/Vpp/peak basis.

### Package boundaries
- `BenchDouble`: typed UnitsNet quantities and measurement provenance; no visualization dependencies.
- `BenchDouble.Plotting`: renderer-neutral `QuantityAxis<T>`, `MeasurementPlotSeries`, unit conversion and Plotly.js-compatible JSON.
- `BenchDouble.Plotting.ScottPlot`: optional headless PNG renderer, pinned to ScottPlot 5.1.59.
- Browser test-lab runner: consume Plotly.js figure JSON and load a pinned, locally hosted Plotly.js bundle when the web interface is implemented. No plotting library is required on instrument hosts.
- Plotly.NET 5.1.0 is available but not required for browser rendering: the initial adapter writes the Plotly.js figure schema directly, avoiding a redundant F#/C# chart-object dependency. Add Plotly.NET as an optional adapter only if its typed chart construction becomes valuable.
- LiveCharts2 and OxyPlot remain candidates for native desktop/dashboard adapters, not core dependencies.

## Unit-aware adapter
```csharp
var samples = new[]
{
    (Frequency.FromKilohertz(1), ElectricPotential.FromMillivolts(500)),
    (Frequency.FromKilohertz(2), ElectricPotential.FromMillivolts(750))
};

var series = MeasurementPlot.Create(
    "Frequency response", samples,
    QuantityAxes.Kilohertz(), QuantityAxes.Volts(),
    measurementBasis: "RMS");

// Server/report renderer:
ScottPlotRenderer.SavePng(series, "response.png");

// Browser renderer:
string figureJson = PlotlyFigure.ToJson(series);
```

The generic axis type forces compile-time matching between an input physical quantity and its axis conversion. Axis title, dimension and display-unit symbol travel with the numeric values. Plotly JSON also includes this metadata, separate from the displayed axis labels. The source quantities remain untouched.

## Design requirements
1. Axis types/units must be explicit and validated. A frequency axis cannot accidentally use an ElectricPotential conversion.
2. A trace stores display unit **and physical dimension**; axis compatibility must be checked when combining traces. Temperature offsets require affine conversions, not simple multiplication.
3. RMS/peak/Vpp/DC basis is separate from volts and must be preserved. dBm requires explicit logarithmic reference and impedance where converting from voltage.
4. Capture measurement timestamp, simulated elapsed time, quality, uncertainty and provenance before conversion. Future adapters should support error bars and invalid/missing samples without inventing zeros.
5. Axis scaling (linear/log), engineering prefixes, time zones, time-domain alignment and downsampling are presentation choices. Log axes require positive values; downsampling must not hide excursions relevant to acceptance limits.
6. Store raw readings independently of decimated plot points. Live plots may use bounded buffers, but authoritative results are append-only.
7. Renderer objects never cross the instrument-host protocol. Hosts stream versioned typed samples; clients render them.
8. Support headless server-side PNG and browser-interactive Plotly.js without forcing ScottPlot/Plotly on core or hardware host processes.
9. The UI should support zoom, pan, cursor measurements, linked channels, limits and multiple units on separately labeled axes, with warnings for incompatible dimensions.
10. Plot/export tests verify numeric conversion, unit labeling, quality handling, multi-trace compatibility, performance, and headless rendering.

## Implementation status
Committed: core unit-aware plot conversion, built-in axes for frequency/voltage/current/resistance/temperature/duration, Plotly.js JSON figure export, optional ScottPlot PNG adapter, and initial conversion/JSON tests. **Not yet implemented:** UI, live plot subscriptions, multi-trace validation, uncertainty bars, quality filtering, downsampling, log axes, persistent plot templates and full ScottPlot integration tests. The build has not been executed in the current environment because the .NET SDK is unavailable.

## Dependencies and licensing
- ScottPlot 5.1.59: https://www.nuget.org/packages/ScottPlot/ (MIT)
- Plotly.NET 5.1.0 (optional future typed adapter): https://www.nuget.org/packages/Plotly.NET/
- Plotly.js: https://plotly.com/javascript/ (browser runtime to be pinned and packaged)
