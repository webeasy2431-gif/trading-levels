# trading-levels

Daily MNQ levels, news times and a research recap, read by the NinjaTrader
"PropSim Levels" indicator (see the `Trading-sim` repo, `docs/ninjatrader-handoff.md`).

**Latest file:** https://raw.githubusercontent.com/webeasy2431-gif/trading-levels/main/daily/latest.json

**NinjaTrader indicator:** [`ninjatrader/`](ninjatrader/) (install steps in its README).

## How it updates

1. **Every day at 5:20 PM New York (6:20 PM in summer)**, the GitHub Action
   `.github/workflows/daily-levels.yml` runs `scripts/daily_levels.py` and saves
   `daily/latest.json` and `daily/<trading day>.json`. It can also be run by hand from
   the Actions tab ("Daily MNQ levels" -> Run workflow).
2. **Shortly after**, a daily Claude research run adds the recap, the "why" notes and a
   news log (`news/`), so the recap is ready before Tokyo opens (scheduled task "Daily MNQ recap", 6:45 PM New York).

The trading day runs 6 PM New York to 5 PM New York, so one run covers Asia, London and
New York.

## What is in `latest.json`

| Field | Meaning |
|---|---|
| `session` | The trading day the file is for, with its start and end times. |
| `levels` | Previous day high / low / close, previous week and this week's high / low, previous month high / low. NQ futures prices, which are also MNQ prices. Each has `label`, `kind` (support / resistance / reference) and `why`. |
| `atr14` | Average daily true range over 14 days, in points. |
| `riskInterval` | The trader's last VARIS risk interval for NQ / MNQ (`manual`) and our estimate (`estimate`), calibrated to his past values in `ri_history.csv`. |
| `events` | High-impact releases (plus medium-impact US ones and US holidays) from the session start through the next 7 days, in New York time, with forecast and previous values. `inSession` marks the ones inside this trading day. |
| `recap` | Filled in by the daily research run. |

## Data sources

- Yahoo Finance chart API, `NQ=F` daily bars (CME futures).
- ForexFactory weekly calendar feed (`nfs.faireconomy.media`).
- Not used: CME Group's website (its terms prohibit scraping) and CBOE (blocks scripts).
  So options-based "hedge pressure" levels are entered by hand in the indicator.

Free feeds can change or stop without notice. If a run fails, the Actions tab shows why,
and the previous file stays in place.

## Updating the trader's risk interval

Add a line to `ri_history.csv` when he shows a new value on stream
(`date,product,ri,source`). The estimate recalibrates on the next run.
