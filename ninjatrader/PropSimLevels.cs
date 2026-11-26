// PropSim Levels for NinjaTrader 8
// Session levels (incl. Asia), session VWAP with VARIS-style bands, pivots,
// swing support / resistance, trendlines, session fib, the daily levels file
// (levels with reasons, recap box, news times, alerts) and pasted levels.
// Every feature can be switched on and off (settings or chart buttons).
// Drawing only: this indicator never places, changes or cancels orders.
//
// Install: copy this file to Documents\NinjaTrader 8\bin\Custom\Indicators\,
// open New > NinjaScript Editor, press F5, then add "PropSim Levels" to a chart.
// Source and instructions: https://github.com/webeasy2431-gif/trading-levels

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum PslVwapAnchor { SixPmNewYork, MidnightNewYork, SevenLondon, EightLondon, NineThirtyNewYork }
	public enum PslRiSource { Manual, DailyFileEstimate, ShowBoth }
	public enum PslNyOpen { Auto, EightTwentyNewYork, NineThirtyNewYork }

	public class PropSimLevels : Indicator
	{
		#region Private state
		private TimeZoneInfo chartTz, nyTz, londonTz;
		private DateTime curDay = DateTime.MinValue;
		private int sessionStartBar = -1;
		private double dayHigh, dayLow, lastClose;
		private DateTime dayFirstNy;
		private bool prevValid;
		private double prevHigh, prevLow, prevClose;
		private DateTime asiaEndT, londonOpenT, nyEightT, orStartT, orEndT, sessionEndT, vwapAnchorT;
		private double asiaHigh, asiaLow; private bool asiaAny, asiaDone;
		private double londonOpenPrice; private bool londonOpenSet;
		private double lonRangeHigh, lonRangeLow; private bool lonRangeAny;
		private double orHigh, orLow; private bool orAny, orDone;
		private double cumPV, cumV; private bool vwapActive; private int vwapStartBar = -1;

		private class Swing { public int Bar; public double Price; public bool IsHigh; }
		private readonly List<Swing> swings = new List<Swing>();

		private class Lvl { public string Key; public string Label; public double Price; public string Note; public Brush Brush; public DashStyleHelper Dash; public bool Alert; }

		private class FileEvent { public DateTime Time; public string Title; public string Currency; public string Impact; public string Forecast; public bool InSession; }
		private class FileData
		{
			public string TradingDay; public string GeneratedAt;
			public List<Lvl> Levels = new List<Lvl>();
			public List<FileEvent> Events = new List<FileEvent>();
			public double RiEstimate; public double RiManual; public string RiManualDate;
			public string RecapSummary; public string RecapWrittenAt;
			public List<string> Watch = new List<string>();
		}
		private volatile FileData fileData;
		private string fileError;
		private System.Threading.Timer fileTimer;
		private int downloading;

		private HashSet<string> drawnPrev = new HashSet<string>();
		private HashSet<string> drawnNow = new HashSet<string>();
		private Brush[] origPlotBrushes;
		private SimpleFont labelFont, recapFont;

		private NinjaTrader.Gui.Chart.Chart chartWindow;
		private System.Windows.Controls.StackPanel buttonBar;
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Session levels, VWAP with VARIS-style bands, support / resistance, trendlines and the daily levels file. Drawing only.";
				Name = "PropSim Levels";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = true;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;

				// 1. Features
				ShowSessionLevels = true; ShowLondonRange = false; ShowNyOpeningRange = true;
				ShowVwap = true; ShowVarisBands = true; ShowPivots = false;
				ShowSupportResistance = true; ShowTrendlines = true; ShowFib = false;
				ShowFileLevels = true; ShowRecap = true; ShowNews = true; ShowPasteLevels = true;
				EnableAlerts = true; ShowChartButtons = true;

				// 2. VARIS risk interval by product family (the trader's panel defaults)
				RiNq = 272.5; RiEs = 70.25; RiGc = 24.1; RiCl = 1.15; RiSi = 0.45; RiVix = 4.05;
				RiYm = 421; RiRty = 59.5; RiBtc = 2490; RiNg = 0.038; Ri6E = 0; Ri6B = 0; Ri6J = 0;
				RiOther = 5; ForceOther = false;
				VixSymbol = "VX 12-26";
				VwapAnchor = PslVwapAnchor.SixPmNewYork;
				ShowInnerBands = true; ShowDdBands = true; ShowThirdBands = true; ShowZoneShading = true; ShowBandLabels = true;
				RiSource = PslRiSource.Manual;

				// Levels
				NyOpenTime = PslNyOpen.Auto;
				PasteLevels = "";
				SwingStrength = 5; SrMaxLevels = 6; SrToleranceAtr = 0.25; SrLookbackBars = 600;
				TrendBreakTicks = 2;
				FibLevels = "0, 0.5, 1";

				// Daily file
				FileUrl = "https://raw.githubusercontent.com/webeasy2431-gif/trading-levels/main/daily/latest.json";
				RefreshMinutes = 10;
				NewsShadeMinutes = 5;
				AlertTicks = 8;
				AlertSound = "Alert2.wav";

				// Colours
				SessionBrush = Brushes.Goldenrod;
				FileBrush = Brushes.DeepSkyBlue;
				PasteBrush = Brushes.Orchid;
				SrBrush = Brushes.SlateGray;
				TrendBrush = Brushes.LightSeaGreen;
				FibBrush = Brushes.Silver;
				NewsBrush = Brushes.OrangeRed;
				ZoneBrush = Brushes.SteelBlue;
				RecapBackBrush = Brushes.Black;

				AddPlot(new Stroke(Brushes.HotPink, 2), PlotStyle.Line, "VWAP");
				AddPlot(new Stroke(Brushes.SteelBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "Inner upper");
				AddPlot(new Stroke(Brushes.SteelBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "Inner lower");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "DD Band upper");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "DD Band lower");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "DD Band upper (est.)");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "DD Band lower (est.)");
				AddPlot(new Stroke(Brushes.CadetBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "1/3 upper");
				AddPlot(new Stroke(Brushes.CadetBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "1/3 lower");
				AddPlot(new Stroke(Brushes.CadetBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "2/3 upper");
				AddPlot(new Stroke(Brushes.CadetBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "2/3 lower");
			}
			else if (State == State.DataLoaded)
			{
				chartTz = Core.Globals.GeneralOptions.TimeZoneInfo;
				nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
				londonTz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
				labelFont = new SimpleFont("Arial", 10);
				recapFont = new SimpleFont("Arial", 11);
				origPlotBrushes = new Brush[Plots.Length];
				for (int i = 0; i < Plots.Length; i++) origPlotBrushes[i] = Plots[i].Brush;
				StartFileTimer();
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null && ShowChartButtons) AddButtons();
			}
			else if (State == State.Terminated)
			{
				if (fileTimer != null) { fileTimer.Dispose(); fileTimer = null; }
				RemoveButtons();
			}
		}

		#region Time helpers
		private DateTime ToZone(DateTime chartTime, TimeZoneInfo zone)
		{
			return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(chartTime, DateTimeKind.Unspecified), chartTz, zone);
		}

		// A clock time on a date in `zone`, as chart time.
		private DateTime At(DateTime date, double hours, TimeZoneInfo zone)
		{
			DateTime local = DateTime.SpecifyKind(date.Date.AddHours(hours), DateTimeKind.Unspecified);
			return TimeZoneInfo.ConvertTime(local, zone, chartTz);
		}

		// CME trading day: 6 PM New York (previous evening) to 5 PM New York. Weekend sessions belong to Monday.
		private DateTime TradingDayOf(DateTime ny)
		{
			DateTime d = ny.Date;
			if (ny.TimeOfDay >= TimeSpan.FromHours(18)) d = d.AddDays(1);
			if (d.DayOfWeek == DayOfWeek.Saturday) d = d.AddDays(2);
			else if (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
			return d;
		}

		private bool IsIndexProduct()
		{
			string r = Instrument.MasterInstrument.Name.ToUpperInvariant();
			return r == "NQ" || r == "MNQ" || r == "ES" || r == "MES" || r == "YM" || r == "MYM" || r == "RTY" || r == "M2K" || r == "EMD" || r == "NKD";
		}
		#endregion

		#region Risk interval
		private double pastedRi;   // "RI 291.75" in the paste box overrides the panel value for this chart

		private double ManualRi()
		{
			if (pastedRi > 0) return pastedRi;
			if (ForceOther) return RiOther;
			string r = Instrument.MasterInstrument.Name.ToUpperInvariant();
			switch (r)
			{
				case "NQ": case "MNQ": return RiNq;
				case "ES": case "MES": return RiEs;
				case "GC": case "MGC": return RiGc;
				case "CL": case "MCL": case "QM": return RiCl;
				case "SI": case "SIL": return RiSi;
				case "VX": case "VXM": return RiVix;
				case "YM": case "MYM": return RiYm;
				case "RTY": case "M2K": return RiRty;
				case "BTC": case "MBT": return RiBtc;
				case "NG": case "QG": return RiNg;
				case "6E": case "M6E": return Ri6E;
				case "6B": case "M6B": return Ri6B;
				case "6J": case "MJY": return Ri6J;
				default: return RiOther;
			}
		}

		// The daily file currently estimates NQ / MNQ only.
		private double FileRi()
		{
			FileData f = fileData;
			string r = Instrument.MasterInstrument.Name.ToUpperInvariant();
			if (f == null || (r != "NQ" && r != "MNQ")) return 0;
			return f.RiEstimate;
		}
		#endregion

		private string lastPaste;

		private void ReadPastedRi()
		{
			if (PasteLevels == lastPaste) return;
			lastPaste = PasteLevels;
			pastedRi = 0;
			if (string.IsNullOrWhiteSpace(PasteLevels)) return;
			Match m = Regex.Match(PasteLevels, @"(?i)(?:^|[,;\n|])\s*RI\s*[:=]?\s*([0-9][0-9,]*\.?[0-9]*)");
			double v;
			if (m.Success && double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) pastedRi = v;
		}

		protected override void OnBarUpdate()
		{
			ReadPastedRi();
			if (CurrentBar < 1) return;
			DateTime ny = ToZone(Time[0], nyTz);
			DateTime td = TradingDayOf(ny);
			if (td != curDay) RollDay(td, ny);

			// Day high / low / close.
			dayHigh = Math.Max(dayHigh, High[0]); dayLow = Math.Min(dayLow, Low[0]); lastClose = Close[0];

			// Asian range: session start to 07:00 London.
			if (Time[0] <= asiaEndT) { if (!asiaAny) { asiaHigh = High[0]; asiaLow = Low[0]; asiaAny = true; } else { asiaHigh = Math.Max(asiaHigh, High[0]); asiaLow = Math.Min(asiaLow, Low[0]); } }
			else asiaDone = asiaAny;
			// London open price (08:00 London).
			if (!londonOpenSet && Time[0] > londonOpenT) { londonOpenPrice = Open[0]; londonOpenSet = true; }
			// London session range: 07:00 London to 08:00 New York.
			if (Time[0] > asiaEndT && Time[0] <= nyEightT)
			{
				if (!lonRangeAny) { lonRangeHigh = High[0]; lonRangeLow = Low[0]; lonRangeAny = true; }
				else { lonRangeHigh = Math.Max(lonRangeHigh, High[0]); lonRangeLow = Math.Min(lonRangeLow, Low[0]); }
			}
			// New York opening range: 30 minutes from 9:30 (indices) or 8:20 (others).
			if (Time[0] > orStartT && Time[0] <= orEndT)
			{
				if (!orAny) { orHigh = High[0]; orLow = Low[0]; orAny = true; }
				else { orHigh = Math.Max(orHigh, High[0]); orLow = Math.Min(orLow, Low[0]); }
			}
			if (Time[0] >= orEndT && orAny) orDone = true;

			UpdateVwap();
			UpdateSwings();

			bool drawTime = State == State.Realtime || CurrentBar >= Count - 2;
			if (drawTime) DrawAll();
		}

		private void RollDay(DateTime td, DateTime ny)
		{
			if (curDay != DateTime.MinValue && sessionStartBar >= 0)
			{
				// A full day: the first bar came within two hours of the 6 PM open.
				prevValid = dayFirstNy.TimeOfDay >= TimeSpan.FromHours(18) && dayFirstNy.TimeOfDay <= TimeSpan.FromHours(20)
					|| (dayFirstNy.DayOfWeek == DayOfWeek.Sunday && dayFirstNy.TimeOfDay >= TimeSpan.FromHours(18));
				prevHigh = dayHigh; prevLow = dayLow; prevClose = lastClose;
			}
			curDay = td;
			sessionStartBar = CurrentBar;
			dayFirstNy = ny;
			dayHigh = High[0]; dayLow = Low[0];
			DateTime prevDate = td.AddDays(-1);
			asiaEndT = At(td, 7, londonTz);
			londonOpenT = At(td, 8, londonTz);
			nyEightT = At(td, 8, nyTz);
			bool index = NyOpenTime == PslNyOpen.NineThirtyNewYork || (NyOpenTime == PslNyOpen.Auto && IsIndexProduct());
			orStartT = At(td, index ? 9.5 : 8 + 20.0 / 60, nyTz);
			orEndT = orStartT.AddMinutes(30);
			sessionEndT = At(td, 17, nyTz);
			switch (VwapAnchor)
			{
				case PslVwapAnchor.MidnightNewYork: vwapAnchorT = At(td, 0, nyTz); break;
				case PslVwapAnchor.SevenLondon: vwapAnchorT = At(td, 7, londonTz); break;
				case PslVwapAnchor.EightLondon: vwapAnchorT = At(td, 8, londonTz); break;
				case PslVwapAnchor.NineThirtyNewYork: vwapAnchorT = At(td, 9.5, nyTz); break;
				default: vwapAnchorT = At(prevDate, 18, nyTz); break;
			}
			asiaAny = asiaDone = false; londonOpenSet = false; lonRangeAny = false; orAny = orDone = false;
			vwapActive = false; cumPV = 0; cumV = 0;
		}

		private void UpdateVwap()
		{
			if (!vwapActive && Time[0] > vwapAnchorT) { vwapActive = true; cumPV = 0; cumV = 0; vwapStartBar = CurrentBar; }
			if (!vwapActive) { for (int i = 0; i < Values.Length; i++) Values[i].Reset(); return; }
			double typical = (High[0] + Low[0] + Close[0]) / 3.0;
			double v = Math.Max(1, Volume[0]);
			cumPV += typical * v; cumV += v;
			double vwap = cumPV / cumV;
			if (ShowVwap) Values[0][0] = vwap; else Values[0].Reset();

			double manual = ManualRi(), est = FileRi();
			double ri = RiSource == PslRiSource.DailyFileEstimate && est > 0 ? est : manual;
			if (ShowVarisBands && ri > 0)
			{
				if (ShowInnerBands) { Values[1][0] = vwap + 0.5 * ri; Values[2][0] = vwap - 0.5 * ri; } else { Values[1].Reset(); Values[2].Reset(); }
				if (ShowDdBands) { Values[3][0] = vwap + ri; Values[4][0] = vwap - ri; } else { Values[3].Reset(); Values[4].Reset(); }
			}
			else { Values[1].Reset(); Values[2].Reset(); Values[3].Reset(); Values[4].Reset(); }
			if (ShowVarisBands && RiSource == PslRiSource.ShowBoth && est > 0) { Values[5][0] = vwap + est; Values[6][0] = vwap - est; }
			else { Values[5].Reset(); Values[6].Reset(); }
			if (ShowVarisBands && ShowThirdBands && ri > 0)
			{
				Values[7][0] = vwap + ri / 3.0; Values[8][0] = vwap - ri / 3.0;
				Values[9][0] = vwap + 2.0 * ri / 3.0; Values[10][0] = vwap - 2.0 * ri / 3.0;
			}
			else { Values[7].Reset(); Values[8].Reset(); Values[9].Reset(); Values[10].Reset(); }
		}

		private void UpdateSwings()
		{
			int n = Math.Max(2, SwingStrength);
			if (CurrentBar < 2 * n) return;
			// Bar n bars ago is a swing high if it is above the n older bars and not below
			// any of the n newer ones (mirror image for a swing low).
			bool hi = true, lo = true;
			double ph = High[n], pl = Low[n];
			for (int i = 0; i <= 2 * n; i++)
			{
				if (i == n) continue;
				if (i > n) { if (High[i] >= ph) hi = false; if (Low[i] <= pl) lo = false; }
				else { if (High[i] > ph) hi = false; if (Low[i] < pl) lo = false; }
			}
			if (hi) swings.Add(new Swing { Bar = CurrentBar - n, Price = High[n], IsHigh = true });
			if (lo) swings.Add(new Swing { Bar = CurrentBar - n, Price = Low[n], IsHigh = false });
			if (swings.Count > 400) swings.RemoveRange(0, swings.Count - 400);
		}

		#region Drawing
		private void Track(string tag) { drawnNow.Add(tag); }

		private void Level(string key, string label, double price, Brush brush, DashStyleHelper dash, int startBar, string note)
		{
			if (price <= 0 || double.IsNaN(price)) return;
			int startAgo = Math.Max(1, Math.Min(CurrentBar, CurrentBar - startBar));
			string tag = "PSL_" + key;
			Draw.Ray(this, tag, false, startAgo, price, 0, price, brush, dash, 1);
			Track(tag);
			string text = label + "  " + Instrument.MasterInstrument.FormatPrice(price);
			Draw.Text(this, tag + "_t", false, text, 0, price, 7, brush, labelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
			Track(tag + "_t");
		}

		private void DrawAll()
		{
			drawnNow = new HashSet<string>();
			int start = Math.Max(0, sessionStartBar);
			FileData f = fileData;
			List<Lvl> alertables = new List<Lvl>();

			if (ShowSessionLevels)
			{
				if (prevValid)
				{
					Level("PDH", "PD High", prevHigh, SessionBrush, DashStyleHelper.Solid, start, null);
					Level("PDL", "PD Low", prevLow, SessionBrush, DashStyleHelper.Solid, start, null);
					Level("PDC", "PD Close", prevClose, SessionBrush, DashStyleHelper.Dot, start, null);
					alertables.Add(new Lvl { Key = "PDH", Label = "PD High", Price = prevHigh });
					alertables.Add(new Lvl { Key = "PDL", Label = "PD Low", Price = prevLow });
				}
				if (asiaAny)
				{
					string sfx = asiaDone ? "" : " (forming)";
					Level("ASH", "Asia High" + sfx, asiaHigh, SessionBrush, DashStyleHelper.Dash, start, null);
					Level("ASL", "Asia Low" + sfx, asiaLow, SessionBrush, DashStyleHelper.Dash, start, null);
					if (asiaDone) { alertables.Add(new Lvl { Key = "ASH", Label = "Asia High", Price = asiaHigh }); alertables.Add(new Lvl { Key = "ASL", Label = "Asia Low", Price = asiaLow }); }
				}
				if (londonOpenSet) Level("LO", "London open", londonOpenPrice, SessionBrush, DashStyleHelper.Dot, start, null);
			}
			if (ShowLondonRange && lonRangeAny)
			{
				Level("LRH", "London High", lonRangeHigh, SessionBrush, DashStyleHelper.DashDot, start, null);
				Level("LRL", "London Low", lonRangeLow, SessionBrush, DashStyleHelper.DashDot, start, null);
			}
			if (ShowNyOpeningRange && orAny)
			{
				string sfx = orDone ? "" : " (forming)";
				Level("ORH", "NY OR High" + sfx, orHigh, SessionBrush, DashStyleHelper.Solid, start, null);
				Level("ORL", "NY OR Low" + sfx, orLow, SessionBrush, DashStyleHelper.Solid, start, null);
				if (orDone) { alertables.Add(new Lvl { Key = "ORH", Label = "NY OR High", Price = orHigh }); alertables.Add(new Lvl { Key = "ORL", Label = "NY OR Low", Price = orLow }); }
			}
			if (ShowPivots && prevValid)
			{
				double p = (prevHigh + prevLow + prevClose) / 3.0;
				Level("PP", "Pivot", p, FibBrush, DashStyleHelper.Dot, start, null);
				Level("R1", "R1", 2 * p - prevLow, FibBrush, DashStyleHelper.Dot, start, null);
				Level("S1", "S1", 2 * p - prevHigh, FibBrush, DashStyleHelper.Dot, start, null);
				Level("R2", "R2", p + (prevHigh - prevLow), FibBrush, DashStyleHelper.Dot, start, null);
				Level("S2", "S2", p - (prevHigh - prevLow), FibBrush, DashStyleHelper.Dot, start, null);
			}

			// VARIS zones and labels.
			if (ShowVarisBands && vwapActive && vwapStartBar >= 0 && Values[3].IsValidDataPoint(0))
			{
				int ago = Math.Max(1, CurrentBar - vwapStartBar);
				if (ShowZoneShading && ShowInnerBands && ShowDdBands && CurrentBar - vwapStartBar >= 1)
				{
					Draw.Region(this, "PSL_ZU", ago, 0, Values[1], Values[3], Brushes.Transparent, ZoneBrush, 15);
					Draw.Region(this, "PSL_ZL", ago, 0, Values[2], Values[4], Brushes.Transparent, ZoneBrush, 15);
					Track("PSL_ZU"); Track("PSL_ZL");
				}
				if (ShowBandLabels && ShowDdBands)
				{
					Draw.Text(this, "PSL_DDU_t", false, "DD Band  " + Instrument.MasterInstrument.FormatPrice(Values[3][0]), 0, Values[3][0], 7, Plots[3].Brush, labelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
					Draw.Text(this, "PSL_DDL_t", false, "DD Band  " + Instrument.MasterInstrument.FormatPrice(Values[4][0]), 0, Values[4][0], -7, Plots[4].Brush, labelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
					Track("PSL_DDU_t"); Track("PSL_DDL_t");
				}
				if (ShowDdBands)
				{
					alertables.Add(new Lvl { Key = "DDU", Label = "DD Band upper", Price = Values[3][0] });
					alertables.Add(new Lvl { Key = "DDL", Label = "DD Band lower", Price = Values[4][0] });
				}
			}
			ApplyPlotVisibility();

			if (ShowSupportResistance) DrawSupportResistance();
			if (ShowTrendlines) DrawTrendlines();
			if (ShowFib && dayHigh > dayLow) DrawFib(start);

			// Daily file levels (skip the ones the chart computes itself).
			if (ShowFileLevels && f != null && f.TradingDay == curDay.ToString("yyyy-MM-dd"))
			{
				int k = 0;
				foreach (Lvl l in f.Levels)
				{
					if (prevValid && ShowSessionLevels && (l.Label == "PD High" || l.Label == "PD Low" || l.Label == "PD Close")) continue;
					Level("F" + (k++), l.Label, l.Price, FileBrush, DashStyleHelper.Dash, start, l.Note);
					alertables.Add(l);
				}
			}

			// Pasted levels.
			if (ShowPasteLevels && !string.IsNullOrWhiteSpace(PasteLevels))
			{
				int k = 0;
				foreach (Lvl l in ParsePaste(PasteLevels))
				{
					Level("P" + (k++), l.Label, l.Price, PasteBrush, DashStyleHelper.Solid, start, null);
					alertables.Add(l);
				}
			}

			if (ShowNews && f != null) DrawNews(f);
			if (ShowRecap) DrawRecap(f);
			if (EnableAlerts && State == State.Realtime) CheckAlerts(alertables);

			// Remove whatever was drawn last time but not now (features switched off, levels gone).
			foreach (string tag in drawnPrev) if (!drawnNow.Contains(tag)) RemoveDrawObject(tag);
			drawnPrev = drawnNow;
		}

		private void ApplyPlotVisibility()
		{
			if (origPlotBrushes == null) return;
			bool third = ShowVarisBands && ShowThirdBands;
			bool[] on = new bool[] { ShowVwap, ShowVarisBands && ShowInnerBands, ShowVarisBands && ShowInnerBands, ShowVarisBands && ShowDdBands, ShowVarisBands && ShowDdBands, ShowVarisBands && RiSource == PslRiSource.ShowBoth, ShowVarisBands && RiSource == PslRiSource.ShowBoth, third, third, third, third };
			for (int i = 0; i < Plots.Length && i < on.Length; i++)
			{
				Brush want = on[i] ? origPlotBrushes[i] : Brushes.Transparent;
				if (Plots[i].Brush != want) Plots[i].Brush = want;
			}
		}

		private void DrawSupportResistance()
		{
			double atr = ATR(14)[0];
			if (atr <= 0) return;
			double tol = Math.Max(TickSize, SrToleranceAtr * atr);
			int firstBar = CurrentBar - SrLookbackBars;
			List<Swing> pts = swings.Where(x => x.Bar >= firstBar).OrderBy(x => x.Price).ToList();
			// Merge swing points within the tolerance into levels.
			List<List<Swing>> clusters = new List<List<Swing>>();
			foreach (Swing sw in pts)
			{
				if (clusters.Count > 0 && sw.Price - clusters[clusters.Count - 1].Average(x => x.Price) <= tol) clusters[clusters.Count - 1].Add(sw);
				else clusters.Add(new List<Swing> { sw });
			}
			var ranked = clusters
				.Select(c => new { Price = c.Average(x => x.Price), Touches = c.Count, First = c.Min(x => x.Bar), Last = c.Max(x => x.Bar) })
				.OrderByDescending(c => c.Touches * 2 + (double)(c.Last - firstBar) / Math.Max(1, SrLookbackBars))
				.Take(Math.Max(0, SrMaxLevels)).ToList();
			int k = 0;
			foreach (var c in ranked)
			{
				double price = Instrument.MasterInstrument.RoundToTickSize(c.Price);
				string tag = "PSL_SR" + (k++);
				int startAgo = Math.Max(1, CurrentBar - c.First);
				Draw.Ray(this, tag, false, startAgo, price, 0, price, SrBrush, DashStyleHelper.Solid, c.Touches >= 3 ? 2 : 1);
				Draw.Text(this, tag + "_t", false, string.Format("S/R x{0}  {1}", c.Touches, Instrument.MasterInstrument.FormatPrice(price)), 0, price, 7, SrBrush, labelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
				Track(tag); Track(tag + "_t");
			}
		}

		private void DrawTrendlines()
		{
			DrawTrend(false, "PSL_TU");   // through the last two higher lows
			DrawTrend(true, "PSL_TD");    // through the last two lower highs
		}

		private void DrawTrend(bool highs, string tag)
		{
			List<Swing> s = swings.Where(x => x.IsHigh == highs).ToList();
			if (s.Count < 2) return;
			Swing a = s[s.Count - 2], b = s[s.Count - 1];
			if (b.Bar <= a.Bar) return;
			bool valid = highs ? b.Price < a.Price : b.Price > a.Price;
			if (!valid) return;
			double slope = (b.Price - a.Price) / (b.Bar - a.Bar);
			double now = b.Price + slope * (CurrentBar - b.Bar);
			double brk = TrendBreakTicks * TickSize;
			// Broken: a close beyond the line by more than the allowance after the second point.
			for (int bar = b.Bar + 1; bar <= CurrentBar; bar++)
			{
				int ago = CurrentBar - bar;
				double line = b.Price + slope * (bar - b.Bar);
				if (highs ? Close[ago] > line + brk : Close[ago] < line - brk) return;
			}
			Draw.Ray(this, tag, false, CurrentBar - a.Bar, a.Price, CurrentBar - b.Bar, b.Price, TrendBrush, DashStyleHelper.Solid, 2);
			Track(tag);
			Draw.Text(this, tag + "_t", false, (highs ? "Trend (lower highs)  " : "Trend (higher lows)  ") + Instrument.MasterInstrument.FormatPrice(now), 0, now, highs ? 7 : -7, TrendBrush, labelFont, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
			Track(tag + "_t");
		}

		private void DrawFib(int start)
		{
			foreach (string part in (FibLevels ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				double r;
				if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) continue;
				double price = Instrument.MasterInstrument.RoundToTickSize(dayLow + (dayHigh - dayLow) * r);
				Level("FIB" + part, "Fib " + part, price, FibBrush, DashStyleHelper.Dot, start, null);
			}
		}

		private void DrawNews(FileData f)
		{
			int k = 0;
			foreach (FileEvent e in f.Events)
			{
				if (!e.InSession) continue;
				string tag = "PSL_N" + (k++);
				DateTime t = e.Time;
				Draw.RegionHighlightX(this, tag, t.AddMinutes(-NewsShadeMinutes), t.AddMinutes(NewsShadeMinutes), Brushes.Transparent, NewsBrush, e.Impact == "High" ? 25 : 12);
				Track(tag);
			}
		}

		private void DrawRecap(FileData f)
		{
			StringBuilder sb = new StringBuilder();
			DateTime nowChart = State == State.Realtime ? TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, chartTz) : Time[0];
			if (f == null)
			{
				sb.Append(fileError != null ? "Daily file: " + fileError : "Daily file: loading...");
			}
			else
			{
				sb.AppendFormat("Trading day {0}", f.TradingDay);
				if (f.TradingDay != curDay.ToString("yyyy-MM-dd")) sb.Append("  (file is for another day)");
				sb.AppendLine();
				if (!string.IsNullOrWhiteSpace(f.RecapSummary)) sb.AppendLine(Wrap(f.RecapSummary, 70));
				else sb.AppendLine("Recap not written yet (the daily research runs about 6:45 PM New York).");
				foreach (string w in f.Watch.Take(3)) sb.AppendLine("- " + Wrap(w, 68));
				// Next releases in this session, with a countdown.
				var next = f.Events.Where(e => e.InSession && e.Time > nowChart.AddMinutes(-NewsShadeMinutes)).Take(3).ToList();
				if (next.Count > 0)
				{
					sb.AppendLine("News:");
					foreach (FileEvent e in next)
					{
						TimeSpan left = e.Time - nowChart;
						string when = left.TotalMinutes < 0 ? "now" : left.TotalHours >= 1 ? string.Format("in {0}h {1:00}m", (int)left.TotalHours, left.Minutes) : string.Format("in {0}m", (int)left.TotalMinutes);
						sb.AppendFormat("  {0} {1} {2} ({3}, {4})", ToZone(e.Time, nyTz).ToString("h:mm tt", CultureInfo.InvariantCulture), e.Currency, e.Title, e.Impact, when);
						sb.AppendLine();
					}
				}
				var notes = f.Levels.Where(l => !string.IsNullOrWhiteSpace(l.Note)).Take(4).ToList();
				foreach (Lvl l in notes) sb.AppendLine(string.Format("{0} {1}: {2}", l.Label, Instrument.MasterInstrument.FormatPrice(l.Price), Wrap(l.Note, 60)));
				double est = FileRi();
				if (est > 0) sb.AppendLine(string.Format("RI: in use {0}{1} / file: his last {2} ({3}), est. {4}", ManualRi(), pastedRi > 0 ? " (pasted)" : "", f.RiManual, f.RiManualDate ?? "?", est));
			}
			Draw.TextFixed(this, "PSL_RECAP", sb.ToString().TrimEnd(), TextPosition.TopLeft, Brushes.White, recapFont, Brushes.Transparent, RecapBackBrush, 70);
			Track("PSL_RECAP");
		}

		private static string Wrap(string s, int width)
		{
			if (string.IsNullOrEmpty(s) || s.Length <= width) return s;
			StringBuilder sb = new StringBuilder(); int line = 0;
			foreach (string w in s.Split(' '))
			{
				if (line + w.Length > width && line > 0) { sb.AppendLine(); line = 0; }
				else if (line > 0) { sb.Append(' '); line++; }
				sb.Append(w); line += w.Length;
			}
			return sb.ToString();
		}

		private void CheckAlerts(List<Lvl> levels)
		{
			double near = AlertTicks * TickSize;
			string sound = Path.Combine(Core.Globals.InstallDir, "sounds", AlertSound ?? "Alert2.wav");
			foreach (Lvl l in levels)
			{
				if (l.Price <= 0 || Math.Abs(Close[0] - l.Price) > near) continue;
				string msg = string.Format("{0} near {1} {2}{3}", Instrument.MasterInstrument.Name, l.Label, Instrument.MasterInstrument.FormatPrice(l.Price), string.IsNullOrWhiteSpace(l.Note) ? "" : ": " + l.Note);
				Alert("PSL_" + (l.Key ?? l.Label), Priority.Medium, msg, sound, 300, Brushes.Black, Brushes.Gold);
			}
		}

		private List<Lvl> ParsePaste(string text)
		{
			List<Lvl> list = new List<Lvl>();
			text = Regex.Replace(text, @"(?<=\d),(?=\d{3}(\D|$))", "");     // 26,807.91 -> 26807.91
			foreach (string raw in text.Split(new[] { ',', ';', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string item = raw.Trim();
				if (item.Length == 0) continue;
				string[] parts = item.Split(new[] { ' ', '\t', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
				double price;
				if (parts.Length == 0 || !double.TryParse(parts[parts.Length - 1].Replace("$", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out price))
				{
					Print("PropSim Levels: could not read pasted level '" + item + "' (use 'Label 26807.25').");
					continue;
				}
				string label = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : "Level";
				if (label.Equals("RI", StringComparison.OrdinalIgnoreCase)) continue;     // the risk interval, not a level
				list.Add(new Lvl { Key = "P_" + label, Label = label, Price = price });
			}
			return list;
		}
		#endregion

		#region Daily file
		private void StartFileTimer()
		{
			if (!(ShowFileLevels || ShowRecap || ShowNews) || string.IsNullOrWhiteSpace(FileUrl)) return;
			int ms = Math.Max(1, RefreshMinutes) * 60000;
			fileTimer = new System.Threading.Timer(o => LoadFile(), null, 0, ms);
		}

		private void LoadFile()
		{
			if (Interlocked.Exchange(ref downloading, 1) == 1) return;
			try
			{
				string json;
				if (FileUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
					using (WebClient wc = new WebClient())
					{
						wc.Encoding = Encoding.UTF8;
						wc.Headers.Add("User-Agent", "PropSimLevels/1.0");
						json = wc.DownloadString(FileUrl + (FileUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);
					}
				}
				else json = File.ReadAllText(FileUrl);
				fileData = ParseFile(json);
				fileError = null;
			}
			catch (Exception e)
			{
				fileError = e.Message;
				Print("PropSim Levels: could not load the daily file: " + e.Message);
			}
			finally { Interlocked.Exchange(ref downloading, 0); }
			Redraw();
		}

		private FileData ParseFile(string json)
		{
			Dictionary<string, object> root = MiniJson.Parse(json) as Dictionary<string, object>;
			if (root == null) throw new Exception("not a JSON object");
			FileData f = new FileData();
			Dictionary<string, object> session = Get(root, "session") as Dictionary<string, object>;
			f.TradingDay = session != null ? Get(session, "tradingDay") as string : null;
			f.GeneratedAt = Get(root, "generatedAt") as string;
			List<object> levels = Get(root, "levels") as List<object>;
			if (levels != null)
				foreach (object o in levels)
				{
					Dictionary<string, object> l = o as Dictionary<string, object>;
					if (l == null) continue;
					double p = ToD(Get(l, "price"));
					if (p <= 0) continue;
					string label = Get(l, "label") as string ?? "Level";
					f.Levels.Add(new Lvl { Key = "F_" + label, Label = label, Price = p, Note = Get(l, "note") as string });
				}
			List<object> events = Get(root, "events") as List<object>;
			if (events != null)
				foreach (object o in events)
				{
					Dictionary<string, object> e = o as Dictionary<string, object>;
					if (e == null) continue;
					DateTimeOffset dto;
					if (!DateTimeOffset.TryParse(Get(e, "time") as string, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto)) continue;
					DateTime chartTime = TimeZoneInfo.ConvertTime(dto.UtcDateTime, TimeZoneInfo.Utc, chartTz);
					f.Events.Add(new FileEvent
					{
						Time = chartTime, Title = Get(e, "title") as string, Currency = Get(e, "currency") as string,
						Impact = Get(e, "impact") as string, Forecast = Get(e, "forecast") as string,
						InSession = Get(e, "inSession") is bool && (bool)Get(e, "inSession"),
					});
				}
			Dictionary<string, object> ri = Get(root, "riskInterval") as Dictionary<string, object>;
			if (ri != null) { f.RiEstimate = ToD(Get(ri, "estimate")); f.RiManual = ToD(Get(ri, "manual")); f.RiManualDate = Get(ri, "manualDate") as string; }
			Dictionary<string, object> recap = Get(root, "recap") as Dictionary<string, object>;
			if (recap != null)
			{
				f.RecapSummary = Get(recap, "summary") as string;
				f.RecapWrittenAt = Get(recap, "writtenAt") as string;
				List<object> watch = Get(recap, "watch") as List<object>;
				if (watch != null) foreach (object w in watch) if (w is string) f.Watch.Add((string)w);
			}
			return f;
		}

		private static object Get(Dictionary<string, object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
		private static double ToD(object o) { return o is double ? (double)o : 0; }

		// Redraw from a timer or a button click, on the indicator's own thread.
		private void Redraw()
		{
			try
			{
				if (State == State.Realtime)
					TriggerCustomEvent(o => { if (CurrentBar > 0) DrawAll(); ForceRefresh(); }, null);
			}
			catch (Exception e) { Print("PropSim Levels: redraw failed: " + e.Message); }
		}

		// Minimal JSON reader (objects, arrays, strings, numbers, true / false / null).
		private static class MiniJson
		{
			public static object Parse(string s) { int i = 0; object v = Value(s, ref i); return v; }
			private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
			private static object Value(string s, ref int i)
			{
				Ws(s, ref i);
				if (i >= s.Length) throw new FormatException("unexpected end");
				char c = s[i];
				if (c == '{') return Obj(s, ref i);
				if (c == '[') return Arr(s, ref i);
				if (c == '"') return Str(s, ref i);
				if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
				if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
				if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
				int st = i;
				while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
				return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
			}
			private static Dictionary<string, object> Obj(string s, ref int i)
			{
				Dictionary<string, object> d = new Dictionary<string, object>(); i++;
				Ws(s, ref i);
				if (s[i] == '}') { i++; return d; }
				while (true)
				{
					Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i);
					if (s[i] != ':') throw new FormatException("expected :"); i++;
					d[k] = Value(s, ref i); Ws(s, ref i);
					if (s[i] == ',') { i++; continue; }
					if (s[i] == '}') { i++; return d; }
					throw new FormatException("expected , or }");
				}
			}
			private static List<object> Arr(string s, ref int i)
			{
				List<object> a = new List<object>(); i++;
				Ws(s, ref i);
				if (s[i] == ']') { i++; return a; }
				while (true)
				{
					a.Add(Value(s, ref i)); Ws(s, ref i);
					if (s[i] == ',') { i++; continue; }
					if (s[i] == ']') { i++; return a; }
					throw new FormatException("expected , or ]");
				}
			}
			private static string Str(string s, ref int i)
			{
				if (s[i] != '"') throw new FormatException("expected string");
				i++; StringBuilder sb = new StringBuilder();
				while (i < s.Length && s[i] != '"')
				{
					if (s[i] == '\\')
					{
						i++; char e = s[i];
						if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r');
						else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
						else sb.Append(e);
					}
					else sb.Append(s[i]);
					i++;
				}
				i++;
				return sb.ToString();
			}
		}
		#endregion

		#region Chart buttons
		private void AddButtons()
		{
			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				try
				{
					chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
					if (chartWindow == null || buttonBar != null) return;
					buttonBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
					AddToggle("Levels", () => ShowSessionLevels, v => ShowSessionLevels = v);
					AddToggle("VARIS", () => ShowVarisBands, v => ShowVarisBands = v);
					AddToggle("S/R", () => ShowSupportResistance, v => ShowSupportResistance = v);
					AddToggle("Trend", () => ShowTrendlines, v => ShowTrendlines = v);
					AddToggle("Fib", () => ShowFib, v => ShowFib = v);
					AddToggle("File", () => ShowFileLevels, v => ShowFileLevels = v);
					AddToggle("Recap", () => ShowRecap, v => ShowRecap = v);
					AddToggle("News", () => ShowNews, v => ShowNews = v);
					AddToggle("Paste", () => ShowPasteLevels, v => ShowPasteLevels = v);
					chartWindow.MainMenu.Add(buttonBar);
				}
				catch (Exception e) { Print("PropSim Levels: chart buttons unavailable: " + e.Message); }
			});
		}

		private void AddToggle(string label, Func<bool> get, Action<bool> set)
		{
			System.Windows.Controls.Button b = new System.Windows.Controls.Button
			{
				Content = label, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(6, 1, 6, 1), FontSize = 11,
				ToolTip = "Show / hide " + label + " (PropSim Levels)",
			};
			Action look = () => { b.Opacity = get() ? 1.0 : 0.45; };
			look();
			b.Click += (s, e) => { set(!get()); look(); Redraw(); if (State != State.Realtime && ChartControl != null) ChartControl.InvalidateVisual(); };
			buttonBar.Children.Add(b);
		}

		private void RemoveButtons()
		{
			if (ChartControl == null || buttonBar == null || chartWindow == null) return;
			System.Windows.Controls.StackPanel bar = buttonBar;
			NinjaTrader.Gui.Chart.Chart win = chartWindow;
			buttonBar = null;
			ChartControl.Dispatcher.InvokeAsync(() => { try { win.MainMenu.Remove(bar); } catch { } });
		}
		#endregion

		#region Properties
		// 1. Features
		[Display(Name = "Session levels (PD H/L/C, Asia, London open)", Order = 1, GroupName = "1. Features")] public bool ShowSessionLevels { get; set; }
		[Display(Name = "London session range", Order = 2, GroupName = "1. Features")] public bool ShowLondonRange { get; set; }
		[Display(Name = "New York opening range", Order = 3, GroupName = "1. Features")] public bool ShowNyOpeningRange { get; set; }
		[Display(Name = "Session VWAP", Order = 4, GroupName = "1. Features")] public bool ShowVwap { get; set; }
		[Display(Name = "VARIS bands", Order = 5, GroupName = "1. Features")] public bool ShowVarisBands { get; set; }
		[Display(Name = "Daily pivots", Order = 6, GroupName = "1. Features")] public bool ShowPivots { get; set; }
		[Display(Name = "Support / resistance", Order = 7, GroupName = "1. Features")] public bool ShowSupportResistance { get; set; }
		[Display(Name = "Trendlines", Order = 8, GroupName = "1. Features")] public bool ShowTrendlines { get; set; }
		[Display(Name = "Session fib", Order = 9, GroupName = "1. Features")] public bool ShowFib { get; set; }
		[Display(Name = "Daily file levels", Order = 10, GroupName = "1. Features")] public bool ShowFileLevels { get; set; }
		[Display(Name = "Recap box", Order = 11, GroupName = "1. Features")] public bool ShowRecap { get; set; }
		[Display(Name = "News shading", Order = 12, GroupName = "1. Features")] public bool ShowNews { get; set; }
		[Display(Name = "Pasted levels", Order = 13, GroupName = "1. Features")] public bool ShowPasteLevels { get; set; }
		[Display(Name = "Alerts near levels", Order = 14, GroupName = "1. Features")] public bool EnableAlerts { get; set; }
		[Display(Name = "Chart toggle buttons", Order = 15, GroupName = "1. Features")] public bool ShowChartButtons { get; set; }

		// 2. VARIS: risk interval by product family
		[Display(Name = "NQ / MNQ (Nasdaq 100)", Order = 1, GroupName = "2. VARIS - Risk interval by product family")] public double RiNq { get; set; }
		[Display(Name = "ES / MES (S&P 500)", Order = 2, GroupName = "2. VARIS - Risk interval by product family")] public double RiEs { get; set; }
		[Display(Name = "GC / MGC (Gold)", Order = 3, GroupName = "2. VARIS - Risk interval by product family")] public double RiGc { get; set; }
		[Display(Name = "CL / MCL (Crude Oil)", Order = 4, GroupName = "2. VARIS - Risk interval by product family")] public double RiCl { get; set; }
		[Display(Name = "SI / SIL (Silver)", Order = 5, GroupName = "2. VARIS - Risk interval by product family")] public double RiSi { get; set; }
		[Display(Name = "VIX (Volatility Index)", Order = 6, GroupName = "2. VARIS - Risk interval by product family")] public double RiVix { get; set; }
		[Display(Name = "YM / MYM (Dow Jones)", Order = 7, GroupName = "2. VARIS - Risk interval by product family")] public double RiYm { get; set; }
		[Display(Name = "RTY / M2K (Russell 2000)", Order = 8, GroupName = "2. VARIS - Risk interval by product family")] public double RiRty { get; set; }
		[Display(Name = "BTC (Bitcoin)", Order = 9, GroupName = "2. VARIS - Risk interval by product family")] public double RiBtc { get; set; }
		[Display(Name = "NG (Natural Gas)", Order = 10, GroupName = "2. VARIS - Risk interval by product family")] public double RiNg { get; set; }
		[Display(Name = "6E / M6E (Euro)", Order = 11, GroupName = "2. VARIS - Risk interval by product family")] public double Ri6E { get; set; }
		[Display(Name = "6B / M6B (Pound)", Order = 12, GroupName = "2. VARIS - Risk interval by product family")] public double Ri6B { get; set; }
		[Display(Name = "6J / MJY (Yen)", Order = 13, GroupName = "2. VARIS - Risk interval by product family")] public double Ri6J { get; set; }
		[Display(Name = "Other instrument - risk interval (points)", Order = 14, GroupName = "2. VARIS - Risk interval by product family")] public double RiOther { get; set; }
		[Display(Name = "Force use of 'Other' RI regardless of detected symbol", Order = 15, GroupName = "2. VARIS - Risk interval by product family")] public bool ForceOther { get; set; }

		[Display(Name = "VIX futures symbol (front month)", Description = "Stored only; not used in any calculation yet.", Order = 1, GroupName = "3. VARIS - Volatility reference")] public string VixSymbol { get; set; }
		[Display(Name = "VWAP anchor", Order = 1, GroupName = "4. VARIS - Session anchor")] public PslVwapAnchor VwapAnchor { get; set; }

		[Display(Name = "Half bands (0.5 RI)", Order = 1, GroupName = "5. VARIS - Band visibility")] public bool ShowInnerBands { get; set; }
		[Display(Name = "DD bands (1.0 RI)", Order = 2, GroupName = "5. VARIS - Band visibility")] public bool ShowDdBands { get; set; }
		[Display(Name = "Shaded zone (0.5 to 1.0 RI)", Order = 3, GroupName = "5. VARIS - Band visibility")] public bool ShowZoneShading { get; set; }
		[Display(Name = "1/3 and 2/3 bands", Order = 6, GroupName = "5. VARIS - Band visibility")] public bool ShowThirdBands { get; set; }
		[Display(Name = "Band price labels", Order = 4, GroupName = "5. VARIS - Band visibility")] public bool ShowBandLabels { get; set; }
		[Display(Name = "RI source", Description = "Manual = your numbers. Daily file estimate = calibrated estimate from the daily file (NQ / MNQ). Show both = estimate drawn dashed beside yours.", Order = 5, GroupName = "5. VARIS - Band visibility")] public PslRiSource RiSource { get; set; }

		[Display(Name = "New York opening range start", Order = 1, GroupName = "6. Levels")] public PslNyOpen NyOpenTime { get; set; }
		[Display(Name = "Paste levels", Description = "e.g. RI 291.75, MHP 26684.28, WHP 26807.91 (label then price; separate with commas, semicolons or new lines). 'RI' sets the VARIS risk interval for this chart instead of drawing a line.", Order = 2, GroupName = "6. Levels")] public string PasteLevels { get; set; }

		[Range(2, 50)][Display(Name = "Swing strength (bars each side)", Order = 1, GroupName = "7. Support / resistance and trendlines")] public int SwingStrength { get; set; }
		[Range(0, 30)][Display(Name = "Max S/R levels", Order = 2, GroupName = "7. Support / resistance and trendlines")] public int SrMaxLevels { get; set; }
		[Range(0.01, 5)][Display(Name = "Merge tolerance (x ATR)", Order = 3, GroupName = "7. Support / resistance and trendlines")] public double SrToleranceAtr { get; set; }
		[Range(50, 100000)][Display(Name = "Lookback (bars)", Order = 4, GroupName = "7. Support / resistance and trendlines")] public int SrLookbackBars { get; set; }
		[Range(0, 1000)][Display(Name = "Trendline break allowance (ticks)", Order = 5, GroupName = "7. Support / resistance and trendlines")] public int TrendBreakTicks { get; set; }
		[Display(Name = "Fib levels", Description = "Comma-separated ratios of today's session range, e.g. 0, 0.5, 1", Order = 6, GroupName = "7. Support / resistance and trendlines")] public string FibLevels { get; set; }

		[Display(Name = "Daily file URL or path", Order = 1, GroupName = "8. Daily file")] public string FileUrl { get; set; }
		[Range(1, 240)][Display(Name = "Refresh every (minutes)", Order = 2, GroupName = "8. Daily file")] public int RefreshMinutes { get; set; }
		[Range(0, 120)][Display(Name = "News shading (minutes each side)", Order = 3, GroupName = "8. Daily file")] public int NewsShadeMinutes { get; set; }
		[Range(1, 1000)][Display(Name = "Alert distance (ticks)", Order = 4, GroupName = "8. Daily file")] public int AlertTicks { get; set; }
		[Display(Name = "Alert sound file", Order = 5, GroupName = "8. Daily file")] public string AlertSound { get; set; }

		[XmlIgnore][Display(Name = "Session levels", Order = 1, GroupName = "9. Colours")] public Brush SessionBrush { get; set; }
		[Browsable(false)] public string SessionBrushSerialize { get { return Serialize.BrushToString(SessionBrush); } set { SessionBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Daily file levels", Order = 2, GroupName = "9. Colours")] public Brush FileBrush { get; set; }
		[Browsable(false)] public string FileBrushSerialize { get { return Serialize.BrushToString(FileBrush); } set { FileBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Pasted levels", Order = 3, GroupName = "9. Colours")] public Brush PasteBrush { get; set; }
		[Browsable(false)] public string PasteBrushSerialize { get { return Serialize.BrushToString(PasteBrush); } set { PasteBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Support / resistance", Order = 4, GroupName = "9. Colours")] public Brush SrBrush { get; set; }
		[Browsable(false)] public string SrBrushSerialize { get { return Serialize.BrushToString(SrBrush); } set { SrBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Trendlines", Order = 5, GroupName = "9. Colours")] public Brush TrendBrush { get; set; }
		[Browsable(false)] public string TrendBrushSerialize { get { return Serialize.BrushToString(TrendBrush); } set { TrendBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Fib and pivots", Order = 6, GroupName = "9. Colours")] public Brush FibBrush { get; set; }
		[Browsable(false)] public string FibBrushSerialize { get { return Serialize.BrushToString(FibBrush); } set { FibBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "News shading", Order = 7, GroupName = "9. Colours")] public Brush NewsBrush { get; set; }
		[Browsable(false)] public string NewsBrushSerialize { get { return Serialize.BrushToString(NewsBrush); } set { NewsBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "VARIS zone shading", Order = 8, GroupName = "9. Colours")] public Brush ZoneBrush { get; set; }
		[Browsable(false)] public string ZoneBrushSerialize { get { return Serialize.BrushToString(ZoneBrush); } set { ZoneBrush = Serialize.StringToBrush(value); } }
		[XmlIgnore][Display(Name = "Recap box background", Order = 9, GroupName = "9. Colours")] public Brush RecapBackBrush { get; set; }
		[Browsable(false)] public string RecapBackBrushSerialize { get { return Serialize.BrushToString(RecapBackBrush); } set { RecapBackBrush = Serialize.StringToBrush(value); } }

		[Browsable(false)][XmlIgnore] public Series<double> VwapSeries { get { return Values[0]; } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PropSimLevels[] cachePropSimLevels;
		public PropSimLevels PropSimLevels()
		{
			return PropSimLevels(Input);
		}

		public PropSimLevels PropSimLevels(ISeries<double> input)
		{
			if (cachePropSimLevels != null)
				for (int idx = 0; idx < cachePropSimLevels.Length; idx++)
					if (cachePropSimLevels[idx] != null && cachePropSimLevels[idx].EqualsInput(input))
						return cachePropSimLevels[idx];
			return CacheIndicator<PropSimLevels>(new PropSimLevels(), input, ref cachePropSimLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PropSimLevels PropSimLevels()
		{
			return indicator.PropSimLevels(Input);
		}

		public Indicators.PropSimLevels PropSimLevels(ISeries<double> input)
		{
			return indicator.PropSimLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PropSimLevels PropSimLevels()
		{
			return indicator.PropSimLevels(Input);
		}

		public Indicators.PropSimLevels PropSimLevels(ISeries<double> input)
		{
			return indicator.PropSimLevels(input);
		}
	}
}

#endregion
