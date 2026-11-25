#!/usr/bin/env python3
"""Daily MNQ levels and news times for the NinjaTrader "PropSim Levels" indicator.

Runs after the CME close (GitHub Actions, see .github/workflows/daily-levels.yml)
and writes daily/latest.json plus a dated copy. Standard library only.

Data (both checked to allow automated access, Nov 2026):
  * Yahoo Finance chart API, NQ=F: CME E-mini Nasdaq-100 futures, daily bars.
    MNQ trades at exactly the same price as NQ, so levels apply to MNQ as is.
  * ForexFactory weekly calendar feed (nfs.faireconomy.media): event times,
    impact, forecast and previous values.
Not used: CME's website (its terms prohibit scraping) and CBOE (blocks scripts),
so options-based "hedge pressure" levels are entered by hand in the indicator.

Every number comes from these feeds; nothing is estimated except the risk
interval estimate, which is labelled as such.
"""

import csv
import datetime as dt
import json
import math
import os
import statistics
import sys
import urllib.request
from zoneinfo import ZoneInfo

NY = ZoneInfo("America/New_York")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "daily")
UA = {"User-Agent": "Mozilla/5.0 (compatible; trading-levels/1.0)"}


def get_json(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def daily_bars(symbol="NQ=F", rng="6mo"):
    """Daily bars dated by their trading day (Yahoo dates futures bars at midnight New York)."""
    d = get_json(f"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range={rng}&interval=1d")
    res = d["chart"]["result"][0]
    q = res["indicators"]["quote"][0]
    bars = []
    for t, o, h, l, c in zip(res["timestamp"], q["open"], q["high"], q["low"], q["close"]):
        if None in (o, h, l, c):
            continue
        bars.append({"date": dt.datetime.fromtimestamp(t, NY).date(), "o": o, "h": h, "l": l, "c": c})
    return bars


def next_trading_day(now):
    """The CME trading day that starts at 6 PM New York after `now` (or is in progress)."""
    d = now.date()
    if now.hour >= 17:              # after the 5 PM close the next session belongs to tomorrow
        d += dt.timedelta(days=1)
    while d.weekday() >= 5:         # Saturday / Sunday sessions belong to Monday
        d += dt.timedelta(days=1)
    return d


def fmt_day(d):
    return d.strftime("%a %d %b")


def levels_from(bars, session_day):
    done = [b for b in bars if b["date"] < session_day]
    if len(done) < 25:
        raise SystemExit("not enough daily history")
    pd_ = done[-1]
    out = [
        {"price": pd_["h"], "label": "PD High", "kind": "resistance", "why": f"Previous trading day's high ({fmt_day(pd_['date'])})."},
        {"price": pd_["l"], "label": "PD Low", "kind": "support", "why": f"Previous trading day's low ({fmt_day(pd_['date'])})."},
        {"price": pd_["c"], "label": "PD Close", "kind": "reference", "why": f"Previous trading day's settlement-time close ({fmt_day(pd_['date'])})."},
    ]
    # Previous full week (Mon-Fri) and the week so far.
    week_start = session_day - dt.timedelta(days=session_day.weekday())
    prev_week = [b for b in done if week_start - dt.timedelta(days=7) <= b["date"] < week_start]
    this_week = [b for b in done if b["date"] >= week_start]
    if prev_week:
        out.append({"price": max(b["h"] for b in prev_week), "label": "Prev Week High", "kind": "resistance", "why": f"High of the week of {fmt_day(prev_week[0]['date'])}."})
        out.append({"price": min(b["l"] for b in prev_week), "label": "Prev Week Low", "kind": "support", "why": f"Low of the week of {fmt_day(prev_week[0]['date'])}."})
    if this_week:
        out.append({"price": max(b["h"] for b in this_week), "label": "Week High", "kind": "resistance", "why": "High of this week so far."})
        out.append({"price": min(b["l"] for b in this_week), "label": "Week Low", "kind": "support", "why": "Low of this week so far."})
    # Previous calendar month.
    first = session_day.replace(day=1)
    pm_end = first - dt.timedelta(days=1)
    prev_month = [b for b in done if b["date"].year == pm_end.year and b["date"].month == pm_end.month]
    if prev_month:
        out.append({"price": max(b["h"] for b in prev_month), "label": "Prev Month High", "kind": "resistance", "why": f"High of {pm_end.strftime('%B %Y')}."})
        out.append({"price": min(b["l"] for b in prev_month), "label": "Prev Month Low", "kind": "support", "why": f"Low of {pm_end.strftime('%B %Y')}."})
    for lv in out:
        lv["price"] = round(lv["price"] * 4) / 4           # NQ / MNQ tick 0.25
    return done, out


def atr(done, n=14):
    trs = []
    for prev, b in zip(done[-n - 1:-1], done[-n:]):
        trs.append(max(b["h"] - b["l"], abs(b["h"] - prev["c"]), abs(b["l"] - prev["c"])))
    return sum(trs) / len(trs)


def vol_metric(done, upto=None, n=20):
    """Price x standard deviation of the last n daily log returns, as of `upto` (inclusive)."""
    rows = [b for b in done if upto is None or b["date"] <= upto]
    closes = [b["c"] for b in rows[-n - 1:]]
    rets = [math.log(b / a) for a, b in zip(closes, closes[1:])]
    return closes[-1] * statistics.stdev(rets)


def ri_estimate(done):
    """Risk interval estimate, calibrated to the trader's values in ri_history.csv when available."""
    path = os.path.join(ROOT, "ri_history.csv")
    hist = []
    if os.path.exists(path):
        with open(path, newline="") as f:
            for row in csv.DictReader(f):
                if row.get("product") == "NQ":
                    hist.append((dt.date.fromisoformat(row["date"]), float(row["ri"])))
    metric = vol_metric(done)
    ratios = []
    for d, ri in hist:
        try:
            ratios.append(ri / vol_metric(done, upto=d))
        except (statistics.StatisticsError, ZeroDivisionError, IndexError):
            pass
    factor = statistics.median(ratios) if ratios else 1.0
    last = hist[-1] if hist else None
    return {
        "product": "NQ / MNQ",
        "manual": last[1] if last else None,
        "manualDate": last[0].isoformat() if last else None,
        "estimate": round(metric * factor * 4) / 4,
        "method": "price x stdev(20 daily log returns) x calibration factor",
        "factor": round(factor, 4),
        "calibratedOn": len(ratios),
        "note": "Estimate, not the trader's number. The manual value is the last one he showed.",
    }


def events(session_day):
    """High / medium impact releases from the trading-day start (6 PM) to the end of the week."""
    feeds = ["https://nfs.faireconomy.media/ff_calendar_thisweek.json", "https://nfs.faireconomy.media/ff_calendar_nextweek.json"]
    rows = []
    for url in feeds:
        try:
            rows += get_json(url)
        except Exception as e:                       # next week's feed is not always published
            print(f"calendar feed skipped: {url}: {e}", file=sys.stderr)
    start = dt.datetime.combine(session_day - dt.timedelta(days=1), dt.time(18, 0), NY)   # Monday: Sunday 6 PM
    end = start + dt.timedelta(days=7)
    seen, out = set(), []
    for e in rows:
        try:
            t = dt.datetime.fromisoformat(e["date"]).astimezone(NY)
        except (KeyError, ValueError):
            continue
        cur, imp = e.get("country"), e.get("impact")
        keep = (cur == "USD" and imp in ("High", "Medium", "Holiday")) or (cur in ("JPY", "CNY", "AUD", "EUR", "GBP") and imp == "High")
        key = (e.get("title"), e.get("date"), cur)
        if not keep or not (start <= t < end) or key in seen:
            continue
        seen.add(key)
        out.append({
            "time": t.isoformat(), "timeNY": t.strftime("%a %I:%M %p").replace(" 0", " "),
            "currency": cur, "title": e.get("title"), "impact": imp,
            "forecast": e.get("forecast") or None, "previous": e.get("previous") or None,
            "inSession": t < start + dt.timedelta(hours=23),
        })
    out.sort(key=lambda x: x["time"])
    return out


def main():
    now = dt.datetime.now(NY)
    session_day = next_trading_day(now)
    bars = daily_bars()
    done, lvls = levels_from(bars, session_day)
    start = dt.datetime.combine(session_day - dt.timedelta(days=1), dt.time(18, 0), NY)
    doc = {
        "version": 1,
        "market": "MNQ",
        "priceSource": "Yahoo Finance NQ=F daily bars (CME E-mini Nasdaq-100 futures; MNQ has the same price)",
        "generatedAt": now.isoformat(timespec="seconds"),
        "session": {"tradingDay": session_day.isoformat(), "start": start.isoformat(), "end": (start + dt.timedelta(hours=23)).isoformat()},
        "levels": lvls,
        "atr14": round(atr(done), 2),
        "riskInterval": ri_estimate(done),
        "events": events(session_day),
        "recap": None,
        "recapNote": "The recap and the 'why' notes are added by the daily Claude research run.",
    }
    os.makedirs(OUT, exist_ok=True)
    for name in ("latest.json", f"{session_day.isoformat()}.json"):
        path = os.path.join(OUT, name)
        # Keep a recap already written for this trading day.
        if os.path.exists(path):
            try:
                old = json.load(open(path))
                if old.get("session", {}).get("tradingDay") == doc["session"]["tradingDay"] and old.get("recap"):
                    doc["recap"] = old["recap"]
                    for lv in doc["levels"]:
                        for o in old.get("levels", []):
                            if o.get("label") == lv["label"] and o.get("note"):
                                lv["note"] = o["note"]
            except (ValueError, OSError):
                pass
        with open(path, "w") as f:
            json.dump(doc, f, indent=2)
    print(f"wrote levels for trading day {session_day}: {len(lvls)} levels, {len(doc['events'])} events")


if __name__ == "__main__":
    main()
