# PropSim Levels (NinjaTrader 8 indicator)

Draws on your MNQ chart:

- **Session levels:** previous day high / low / close, Asian range (6 PM New York to 07:00 London, "forming" until then), London open (08:00 London), optional London session range, New York opening range (9:30 to 10:00 New York for index futures, 8:20 to 8:50 for others).
- **Session VWAP with VARIS-style bands:** bands at 1/3, 1/2 and 2/3 of the risk interval and the DD bands at a full risk interval from VWAP, with the zone between 1/2 and 1 shaded. The settings mirror the trader's VARIS V3.0 panel (same groups and defaults: NQ / MNQ 272.5, ES / MES 70.25, GC / MGC 24.1 ...).
- **Support / resistance** from swing highs and lows (merged when close together, labelled with how many times price turned there), and **trendlines** through the last two higher lows / lower highs (removed once broken).
- **Daily file** (`daily/latest.json` in this repo, refreshed every 10 minutes): weekly / monthly levels, news times shaded on the chart, and the **recap box** with the day's research, next releases with a countdown and why the key levels matter.
- **Pasted levels:** type levels from the stream, e.g. `HP 26807.91, MHP 26684.28`.
- **Alerts** with a sound when price comes within 8 ticks of a level (with its reason, if it has one).
- Optional: daily pivots, session fib.

Every feature can be switched on and off: checkboxes in the indicator settings, or the
buttons the indicator adds to the chart toolbar (Levels, VARIS, S/R, Trend, Fib, File,
Recap, News, Paste). It only draws; it never places or changes orders.

## Install

1. Download `PropSimLevels.cs` from this folder (open it on GitHub, then the download
   button, "Download raw file").
2. Put it in `Documents\NinjaTrader 8\bin\Custom\Indicators\`.
3. In NinjaTrader: **New -> NinjaScript Editor**. Press **F5** (or the Compile button).
   Wait for the sound / "compiled successfully" at the bottom.
   - If errors appear at the bottom of the editor, take a screenshot of that error list
     and send it to Claude. It was written without access to NinjaTrader, so a first
     compile may need a fix or two.
4. Open an MNQ chart (5 minute is a good start). Right-click the chart -> **Indicators**
   -> find **PropSim Levels** -> **Add** -> **OK**.

## Settings worth knowing

- **2. VARIS - Risk interval by product family:** change a number when the trader shows a
  new one; the bands redraw. Also add the date and value to `ri_history.csv` in this repo so
  the daily estimate recalibrates.
- **5. VARIS - Band visibility -> RI source:** Manual (your numbers), Daily file estimate,
  or Show both (the estimate drawn dashed beside yours).
- **6. Levels -> Paste levels:** `RI 291.75, MHP 26684.28, WHP 26807.91` (label then price; commas,
  semicolons or new lines between them). Prices with thousands commas are fine. `RI` sets
  tonight's VARIS risk interval for that chart instead of drawing a line.
- **8. Daily file:** the URL of the daily file (already set), how often to refresh, news
  shading width, alert distance and sound (a file in NinjaTrader's `sounds` folder).
- To save your settings as the default, use **Template -> Save** at the bottom of the
  indicator dialog.

## Notes

- Time zones are converted with Windows' zone data, so London / New York daylight-saving
  changes are handled. NinjaTrader's own time zone setting is respected.
- Previous-day levels need the chart to load at least two days. On short tick charts
  that load less, the indicator uses the previous-day levels from the daily file instead.
- The daily file is for one trading day (6 PM to 5 PM New York). Between 5 PM and the next
  update, or at weekends before the Sunday open, the recap box says the file is for
  another day and the file's levels are hidden.
- Hedge-pressure levels (HP / MHP) are not in any free data the script can download, so
  paste them from the stream.
- Not yet included: markers for the simulator's mechanical setups (planned).
