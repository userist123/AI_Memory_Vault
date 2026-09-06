import { useState, useEffect, useCallback, useRef } from "react";

// ═══════════════════════════════════════════════════════════════
// COLORS
// ═══════════════════════════════════════════════════════════════
const C = {
  bg: "#0a0e17", card: "#111827", border: "#1e2a3a", text: "#e2e8f0", dim: "#64748b",
  buy: "#10b981", sell: "#ef4444", wait: "#f59e0b", accent: "#3b82f6", hover: "#1a2332",
};

// ═══════════════════════════════════════════════════════════════
// CRYPTO IDS for CoinGecko
// ═══════════════════════════════════════════════════════════════
const CRYPTO_MAP = {
  bitcoin:"Bitcoin", ethereum:"Ethereum", binancecoin:"BNB", solana:"Solana",
  ripple:"XRP", cardano:"Cardano", "avalanche-2":"Avalanche", polkadot:"Polkadot",
  "matic-network":"Polygon", chainlink:"Chainlink", uniswap:"Uniswap", litecoin:"Litecoin",
  dogecoin:"Dogecoin", "shiba-inu":"Shiba Inu", tron:"TRON", stellar:"Stellar",
  cosmos:"Cosmos", monero:"Monero", filecoin:"Filecoin", "internet-computer":"Internet Computer",
  hedera:"Hedera", vechain:"VeChain", algorand:"Algorand", fantom:"Fantom",
  "near":"NEAR Protocol",
};

// ═══════════════════════════════════════════════════════════════
// SCORING — identical to Python script
// ═══════════════════════════════════════════════════════════════
function calcSignal(rsi, macdCross, maCross, rvol) {
  let score = 0;
  if (rsi < 35) score += 2; else if (rsi < 45) score += 1;
  else if (rsi > 75) score -= 2; else if (rsi > 65) score -= 1;
  const mc = (macdCross || "").toLowerCase();
  if (mc.includes("pozitiv nou")) score += 2;
  else if (mc.includes("pozitiv activ")) score += 1;
  else if (mc.includes("negativ nou")) score -= 2;
  else if (mc.includes("negativ activ")) score -= 1;
  const mx = (maCross || "").toLowerCase();
  if (mx.includes("golden")) score += 2; else if (mx.includes("death")) score -= 2;
  if (rvol > 1.5) score += 1; else if (rvol < 0.6) score -= 1;
  const confluente = Math.min(Math.abs(score), 5);
  const semnal = score >= 3 ? "BUY" : score <= -3 ? "SELL" : "WAIT";
  const prob = Math.min(90, 35 + confluente * 10 + (rvol > 1.2 ? 5 : 0));
  return { semnal, score, confluente, prob };
}

// ═══════════════════════════════════════════════════════════════
// COMPONENTS
// ═══════════════════════════════════════════════════════════════
function Badge({ signal, big }) {
  const c = signal === "BUY" ? C.buy : signal === "SELL" ? C.sell : C.wait;
  const icon = signal === "BUY" ? "▲" : signal === "SELL" ? "▼" : "◆";
  return (
    <span style={{ background: c + "22", color: c, padding: big ? "6px 14px" : "2px 8px",
      borderRadius: 4, fontWeight: 700, fontSize: big ? 14 : 11, border: `1px solid ${c}44`,
      display: "inline-flex", alignItems: "center", gap: 4 }}>
      {icon} {signal}
    </span>
  );
}

function Delta({ value }) {
  if (value == null || isNaN(value)) return <span style={{ color: C.dim }}>—</span>;
  const c = value > 0 ? C.buy : value < 0 ? C.sell : C.dim;
  return <span style={{ color: c, fontWeight: 600 }}>{value > 0 ? "+" : ""}{value.toFixed(2)}%</span>;
}

function Gauge({ value, max = 100, label }) {
  const pct = Math.min(value / max, 1);
  const c = value < 25 ? "#ef4444" : value < 40 ? "#f97316" : value < 60 ? "#eab308" : value < 75 ? "#22c55e" : "#16a34a";
  return (
    <div style={{ textAlign: "center" }}>
      <div style={{ fontSize: 10, color: C.dim, marginBottom: 4 }}>{label}</div>
      <div style={{ position: "relative", height: 8, background: C.border, borderRadius: 4, overflow: "hidden" }}>
        <div style={{ height: "100%", width: `${pct * 100}%`, background: c, borderRadius: 4, transition: "width .5s" }} />
      </div>
      <div style={{ fontSize: 18, fontWeight: 800, color: c, marginTop: 4 }}>{value}</div>
    </div>
  );
}

function Loader({ msg }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 10, padding: 16, color: C.dim, fontSize: 13 }}>
      <div style={{ width: 16, height: 16, border: `2px solid ${C.accent}`, borderTopColor: "transparent",
        borderRadius: "50%", animation: "spin 1s linear infinite" }} />
      {msg}
      <style>{`@keyframes spin { to { transform: rotate(360deg) } }`}</style>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════
// FETCH HELPERS
// ═══════════════════════════════════════════════════════════════
async function fetchCrypto() {
  const ids = Object.keys(CRYPTO_MAP).join(",");
  const url = `https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&ids=${ids}&order=market_cap_desc&sparkline=true&price_change_percentage=1h,24h,7d,30d`;
  const res = await fetch(url);
  if (!res.ok) throw new Error("CoinGecko error");
  const data = await res.json();
  return data.map(coin => {
    const name = CRYPTO_MAP[coin.id] || coin.name;
    const price = coin.current_price;
    const varZi = coin.price_change_percentage_24h || 0;
    const varSapt = coin.price_change_percentage_7d_in_currency || 0;
    const varLuna = coin.price_change_percentage_30d_in_currency || 0;
    const ath = coin.ath || price;
    const sparkline = coin.sparkline_in_7d?.price || [];

    // Calculate RSI from sparkline
    let rsi = 50;
    if (sparkline.length > 15) {
      const closes = sparkline.slice(-30);
      let gains = 0, losses = 0, count = 0;
      for (let i = 1; i < closes.length; i++) {
        const d = closes[i] - closes[i - 1];
        if (d > 0) gains += d; else losses -= d;
        count++;
      }
      const avgG = gains / count, avgL = losses / count || 0.0001;
      rsi = 100 - 100 / (1 + avgG / avgL);
    }

    // Simplified MACD cross from recent trend
    const recent = sparkline.slice(-10);
    const longTrend = sparkline.length > 20 ? sparkline.slice(-26) : recent;
    const shortAvg = recent.length ? recent.reduce((a, b) => a + b, 0) / recent.length : price;
    const longAvg = longTrend.length ? longTrend.reduce((a, b) => a + b, 0) / longTrend.length : price;
    const macdCross = shortAvg > longAvg ?
      (varZi > 2 ? "Impuls pozitiv nou" : "Impuls pozitiv activ") :
      (varZi < -2 ? "Impuls negativ nou" : "Impuls negativ activ");

    // MA cross approximation
    const ma7 = sparkline.length >= 7 ? sparkline.slice(-7).reduce((a, b) => a + b, 0) / 7 : price;
    const ma30 = sparkline.length >= 30 ? sparkline.slice(-30).reduce((a, b) => a + b, 0) / 30 : price;
    const maCross = ma7 > ma30 ? "Golden Cross" : "Death Cross";

    const rvol = 0.8 + Math.abs(varZi) * 0.15;
    const { semnal, score, confluente, prob } = calcSignal(rsi, macdCross, maCross, rvol);
    const atr = price * 0.025;
    const trend = price > ma7 * 1.01 ? "Bullish" : price < ma7 * 0.99 ? "Bearish" : "Sideways";

    return {
      name, ticker: coin.symbol.toUpperCase(), category: "CRYPTO", price,
      var_zi: varZi, var_sapt: varSapt, var_luna: varLuna,
      rsi: +rsi.toFixed(1), macdCross, maCross, rvol: +rvol.toFixed(2),
      semnal, score, confluente, prob, trend,
      sl: semnal === "BUY" ? +(price - 1.5 * atr).toFixed(4) : semnal === "SELL" ? +(price + 1.5 * atr).toFixed(4) : null,
      tp: semnal === "BUY" ? +(price + 3 * atr).toFixed(4) : semnal === "SELL" ? +(price - 3 * atr).toFixed(4) : null,
      mcap: coin.market_cap, sparkline,
      img: coin.image,
    };
  });
}

async function fetchViaAI(prompt) {
  try {
    const res = await fetch("https://api.anthropic.com/v1/messages", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        model: "claude-sonnet-4-20250514",
        max_tokens: 1000,
        tools: [{ type: "web_search_20250305", name: "web_search" }],
        messages: [{ role: "user", content: prompt }],
      }),
    });
    const data = await res.json();
    const text = data.content?.filter(b => b.type === "text").map(b => b.text).join("\n") || "";
    return text;
  } catch (e) {
    console.error("AI fetch error:", e);
    return null;
  }
}

async function fetchMacroData() {
  const text = await fetchViaAI(
    `Return ONLY a JSON object (no markdown, no backticks, no explanation) with current market data as of right now. Format:
{"vix":NUMBER,"yield10y":NUMBER,"yield2y":NUMBER,"usdIndex":NUMBER,"fearGreed":NUMBER,"fearGreedLabel":"TEXT","sp500":NUMBER,"sp500Chg":NUMBER,"nasdaq":NUMBER,"nasdaqChg":NUMBER,"dowjones":NUMBER,"dowjonesChg":NUMBER,"dax":NUMBER,"daxChg":NUMBER,"gold":NUMBER,"goldChg":NUMBER,"oil":NUMBER,"oilChg":NUMBER,"eurusd":NUMBER,"eurusdChg":NUMBER,"gbpusd":NUMBER,"btcDominance":NUMBER}
Use today's real values. All Chg values are 24h percent change. Numbers only, no $ signs.`
  );
  if (!text) return null;
  try {
    const clean = text.replace(/```json|```/g, "").trim();
    const match = clean.match(/\{[\s\S]*\}/);
    return match ? JSON.parse(match[0]) : null;
  } catch { return null; }
}

async function fetchStocksData() {
  const text = await fetchViaAI(
    `Return ONLY a JSON array (no markdown, no backticks) of current stock data for these tickers: AAPL,MSFT,NVDA,GOOGL,AMZN,META,TSLA,JPM,V,NFLX,AMD,PLTR,COIN,AVGO,CRM,TSMC,SPY
Format each object: {"t":"TICKER","n":"Name","p":PRICE,"d":24H_PCT_CHANGE,"w":7D_PCT_CHANGE}
Use today's real prices. Numbers only.`
  );
  if (!text) return [];
  try {
    const clean = text.replace(/```json|```/g, "").trim();
    const match = clean.match(/\[[\s\S]*\]/);
    return match ? JSON.parse(match[0]) : [];
  } catch { return []; }
}

// ═══════════════════════════════════════════════════════════════
// MAIN APP
// ═══════════════════════════════════════════════════════════════
export default function App() {
  const [crypto, setCrypto] = useState([]);
  const [macro, setMacro] = useState(null);
  const [stocks, setStocks] = useState([]);
  const [loading, setLoading] = useState({ crypto: true, macro: true, stocks: true });
  const [error, setError] = useState({});
  const [lastUpdate, setLastUpdate] = useState(null);
  const [tab, setTab] = useState("DASHBOARD");
  const [filter, setFilter] = useState("TOATE");
  const [signalFilter, setSignalFilter] = useState(null);
  const [search, setSearch] = useState("");
  const [sortCol, setSortCol] = useState(null);
  const [sortDir, setSortDir] = useState(-1);
  const [selected, setSelected] = useState(null);
  const refreshRef = useRef(null);

  const loadData = useCallback(async () => {
    setLoading({ crypto: true, macro: true, stocks: true });
    setError({});

    // Crypto — CoinGecko (fast, free)
    try {
      const c = await fetchCrypto();
      setCrypto(c);
    } catch (e) { setError(prev => ({ ...prev, crypto: e.message })); }
    setLoading(prev => ({ ...prev, crypto: false }));

    // Macro — AI web search
    try {
      const m = await fetchMacroData();
      if (m) setMacro(m);
      else setError(prev => ({ ...prev, macro: "Nu s-au putut prelua datele macro" }));
    } catch (e) { setError(prev => ({ ...prev, macro: e.message })); }
    setLoading(prev => ({ ...prev, macro: false }));

    // Stocks — AI web search
    try {
      const s = await fetchStocksData();
      setStocks(s);
    } catch (e) { setError(prev => ({ ...prev, stocks: e.message })); }
    setLoading(prev => ({ ...prev, stocks: false }));

    setLastUpdate(new Date());
  }, []);

  useEffect(() => { loadData(); }, []);

  // Auto-refresh every 5 minutes
  useEffect(() => {
    refreshRef.current = setInterval(loadData, 300000);
    return () => clearInterval(refreshRef.current);
  }, [loadData]);

  // Process stocks into same format as crypto
  const processedStocks = stocks.map(s => {
    const price = s.p;
    const varZi = s.d || 0;
    const varSapt = s.w || 0;
    const rsi = 50 + varZi * 2.5 + varSapt * 0.8; // simplified
    const clampedRsi = Math.max(10, Math.min(90, rsi));
    const macdCross = varZi > 1 ? "Impuls pozitiv activ" : varZi < -1 ? "Impuls negativ activ" :
      varZi > 0 ? "Impuls pozitiv activ" : "Impuls negativ activ";
    const maCross = varSapt > 0 ? "Golden Cross" : "Death Cross";
    const rvol = 0.8 + Math.abs(varZi) * 0.2;
    const { semnal, score, confluente, prob } = calcSignal(clampedRsi, macdCross, maCross, rvol);
    const trend = varZi > 0.5 ? "Bullish" : varZi < -0.5 ? "Bearish" : "Sideways";
    const atr = price * 0.018;
    return {
      name: s.n, ticker: s.t, category: "ACTIUNI", price,
      var_zi: varZi, var_sapt: varSapt, var_luna: null,
      rsi: +clampedRsi.toFixed(1), macdCross, maCross, rvol: +rvol.toFixed(2),
      semnal, score, confluente, prob, trend,
      sl: semnal === "BUY" ? +(price - 1.5 * atr).toFixed(2) : semnal === "SELL" ? +(price + 1.5 * atr).toFixed(2) : null,
      tp: semnal === "BUY" ? +(price + 3 * atr).toFixed(2) : semnal === "SELL" ? +(price - 3 * atr).toFixed(2) : null,
    };
  });

  // Macro-derived index entries
  const macroAssets = macro ? [
    { name: "S&P 500", ticker: "^GSPC", category: "INDICI", price: macro.sp500, var_zi: macro.sp500Chg },
    { name: "NASDAQ", ticker: "^IXIC", category: "INDICI", price: macro.nasdaq, var_zi: macro.nasdaqChg },
    { name: "Dow Jones", ticker: "^DJI", category: "INDICI", price: macro.dowjones, var_zi: macro.dowjonesChg },
    { name: "DAX", ticker: "^GDAXI", category: "INDICI", price: macro.dax, var_zi: macro.daxChg },
    { name: "Gold", ticker: "GC=F", category: "MATERII", price: macro.gold, var_zi: macro.goldChg },
    { name: "Oil WTI", ticker: "CL=F", category: "MATERII", price: macro.oil, var_zi: macro.oilChg },
    { name: "EUR/USD", ticker: "EURUSD", category: "VALUTE", price: macro.eurusd, var_zi: macro.eurusdChg },
    { name: "GBP/USD", ticker: "GBPUSD", category: "VALUTE", price: macro.gbpusd, var_zi: 0 },
  ].filter(a => a.price).map(a => {
    const rsi = Math.max(15, Math.min(85, 50 + (a.var_zi || 0) * 3));
    const macdCross = (a.var_zi || 0) > 0 ? "Impuls pozitiv activ" : "Impuls negativ activ";
    const maCross = (a.var_zi || 0) > 0 ? "Golden Cross" : "Death Cross";
    const rvol = 1.0;
    const { semnal, score, confluente, prob } = calcSignal(rsi, macdCross, maCross, rvol);
    const trend = (a.var_zi || 0) > 0.3 ? "Bullish" : (a.var_zi || 0) < -0.3 ? "Bearish" : "Sideways";
    return { ...a, rsi: +rsi.toFixed(1), macdCross, maCross, rvol, semnal, score, confluente, prob, trend, var_sapt: null, var_luna: null };
  }) : [];

  const allAssets = [...macroAssets, ...processedStocks, ...crypto];

  const stats = {
    buy: allAssets.filter(a => a.semnal === "BUY").length,
    sell: allAssets.filter(a => a.semnal === "SELL").length,
    wait: allAssets.filter(a => a.semnal === "WAIT").length,
    total: allAssets.length,
  };
  const trendGen = stats.buy > stats.total * 0.55 ? "Bullish" : stats.sell > stats.total * 0.55 ? "Bearish" : "Mixt";
  const best = [...allAssets].sort((a, b) => (b.score + (b.prob || 0) * 0.1) - (a.score + (a.prob || 0) * 0.1))[0];
  const topBuy = allAssets.filter(a => a.semnal === "BUY").sort((a, b) => b.score - a.score).slice(0, 5);
  const topSell = allAssets.filter(a => a.semnal === "SELL").sort((a, b) => a.score - b.score).slice(0, 5);

  const cats = ["TOATE", "INDICI", "ACTIUNI", "CRYPTO", "MATERII", "VALUTE"];
  const filtered = allAssets.filter(a => {
    if (filter !== "TOATE" && !a.category.startsWith(filter)) return false;
    if (signalFilter && a.semnal !== signalFilter) return false;
    if (search && !a.name.toLowerCase().includes(search.toLowerCase()) && !a.ticker.toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  }).sort((a, b) => {
    if (!sortCol) return 0;
    const va = a[sortCol], vb = b[sortCol];
    if (va == null) return 1; if (vb == null) return -1;
    return (va > vb ? 1 : -1) * sortDir;
  });

  const isLoading = loading.crypto || loading.macro || loading.stocks;
  const rr = (e, s, t) => { if (!s || !t || !e) return "—"; const r = Math.abs(e - s); return r === 0 ? "—" : (Math.abs(t - e) / r).toFixed(2) + "x"; };

  // Sparkline mini chart
  const Spark = ({ data, w = 80, h = 24 }) => {
    if (!data || data.length < 2) return null;
    const min = Math.min(...data), max = Math.max(...data), range = max - min || 1;
    const pts = data.map((v, i) => `${(i / (data.length - 1)) * w},${h - ((v - min) / range) * h}`).join(" ");
    const c = data[data.length - 1] >= data[0] ? C.buy : C.sell;
    return <svg width={w} height={h}><polyline points={pts} fill="none" stroke={c} strokeWidth="1.5" /></svg>;
  };

  const tabStyle = (active) => ({
    padding: "8px 16px", background: active ? C.accent + "22" : "transparent",
    color: active ? C.accent : C.dim, border: "none", cursor: "pointer",
    fontSize: 12, fontWeight: active ? 700 : 500, borderBottom: active ? `2px solid ${C.accent}` : "2px solid transparent",
    fontFamily: "inherit", transition: "all .15s",
  });

  const chip = (active) => ({
    padding: "4px 10px", borderRadius: 4, cursor: "pointer", fontSize: 11, fontWeight: 600,
    border: "none", background: active ? C.accent : C.card, color: active ? "#fff" : C.dim,
    fontFamily: "inherit",
  });

  return (
    <div style={{ background: C.bg, color: C.text, minHeight: "100vh",
      fontFamily: "'SF Mono', 'Cascadia Code', 'Fira Code', monospace", fontSize: 13 }}>

      {/* HEADER */}
      <div style={{ display: "flex", alignItems: "center", padding: "12px 20px",
        borderBottom: `1px solid ${C.border}`, gap: 16 }}>
        <div style={{ fontWeight: 800, fontSize: 16, color: C.accent }}>📊 MarketPro <span style={{ fontWeight: 400, fontSize: 11, color: C.dim }}>MVP LIVE</span></div>
        <div style={{ display: "flex", gap: 0 }}>
          {["DASHBOARD", "SCREENER", "DETALIU"].map(t => (
            <button key={t} style={tabStyle(tab === t)} onClick={() => setTab(t)}>{t}</button>
          ))}
        </div>
        <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 12 }}>
          {isLoading && <Loader msg="Se actualizează..." />}
          {lastUpdate && !isLoading && (
            <span style={{ fontSize: 10, color: C.dim }}>
              ● Live — {lastUpdate.toLocaleTimeString("ro-RO")}
            </span>
          )}
          <button onClick={loadData} disabled={isLoading}
            style={{ padding: "5px 12px", background: C.accent, color: "#fff", border: "none",
              borderRadius: 4, cursor: "pointer", fontSize: 11, fontWeight: 700, fontFamily: "inherit",
              opacity: isLoading ? 0.5 : 1 }}>
            ↻ Refresh
          </button>
        </div>
      </div>

      <div style={{ padding: 20 }}>

        {/* ════════ DASHBOARD ════════ */}
        {tab === "DASHBOARD" && (
          <div>
            {/* Stats bar */}
            <div style={{ display: "flex", gap: 10, marginBottom: 16, flexWrap: "wrap" }}>
              {[
                { label: "BUY", val: stats.buy, c: C.buy },
                { label: "SELL", val: stats.sell, c: C.sell },
                { label: "WAIT", val: stats.wait, c: C.wait },
              ].map(s => (
                <div key={s.label} style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 6,
                  padding: "10px 16px", display: "flex", alignItems: "center", gap: 10, minWidth: 120 }}>
                  <div style={{ fontSize: 24, fontWeight: 800, color: s.c }}>{s.val}</div>
                  <div>
                    <div style={{ color: s.c, fontWeight: 700, fontSize: 12 }}>{s.label}</div>
                    <div style={{ height: 4, width: 60, background: C.border, borderRadius: 2, marginTop: 3 }}>
                      <div style={{ height: "100%", width: `${stats.total ? (s.val / stats.total * 100) : 0}%`, background: s.c, borderRadius: 2 }} />
                    </div>
                  </div>
                </div>
              ))}
              <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 6, padding: "10px 16px" }}>
                <div style={{ fontSize: 10, color: C.dim }}>Trend</div>
                <Badge signal={trendGen === "Bullish" ? "BUY" : trendGen === "Bearish" ? "SELL" : "WAIT"} />
              </div>
              <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 6, padding: "10px 16px" }}>
                <div style={{ fontSize: 10, color: C.dim }}>Active monitorizate</div>
                <div style={{ fontSize: 20, fontWeight: 800 }}>{stats.total}</div>
              </div>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, marginBottom: 14 }}>
              {/* Best signal */}
              {best && (
                <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16, cursor: "pointer" }}
                  onClick={() => { setSelected(best); setTab("DETALIU"); }}>
                  <div style={{ fontSize: 10, color: C.dim, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>Semnal Principal</div>
                  <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
                    <Badge signal={best.semnal} big />
                    <div>
                      <div style={{ fontSize: 16, fontWeight: 800 }}>{best.name}</div>
                      <div style={{ fontSize: 11, color: C.dim }}>{best.ticker}</div>
                    </div>
                    <div style={{ marginLeft: "auto", textAlign: "right" }}>
                      <div style={{ fontSize: 18, fontWeight: 700 }}>${best.price?.toLocaleString(undefined, { maximumFractionDigits: 2 })}</div>
                      <Delta value={best.var_zi} />
                    </div>
                  </div>
                  <div style={{ display: "flex", gap: 16, fontSize: 11 }}>
                    {[["Score", best.score], ["Prob", best.prob + "%"], ["SL", best.sl?.toFixed(2)], ["TP", best.tp?.toFixed(2)], ["R/R", rr(best.price, best.sl, best.tp)]].map(([l, v]) => (
                      <div key={l}><span style={{ color: C.dim }}>{l}:</span> <b>{v || "—"}</b></div>
                    ))}
                  </div>
                </div>
              )}

              {/* Macro gauges */}
              <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16 }}>
                <div style={{ fontSize: 10, color: C.dim, textTransform: "uppercase", letterSpacing: 1, marginBottom: 10 }}>Macro</div>
                {loading.macro ? <Loader msg="Preluare date macro..." /> : macro ? (
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr 1fr", gap: 12 }}>
                    <Gauge value={macro.fearGreed || 50} label="Fear & Greed" />
                    <div style={{ textAlign: "center" }}>
                      <div style={{ fontSize: 10, color: C.dim }}>VIX</div>
                      <div style={{ fontSize: 20, fontWeight: 800, color: (macro.vix || 0) > 25 ? C.sell : (macro.vix || 0) > 18 ? C.wait : C.buy }}>{macro.vix}</div>
                    </div>
                    <div style={{ textAlign: "center" }}>
                      <div style={{ fontSize: 10, color: C.dim }}>10Y Yield</div>
                      <div style={{ fontSize: 20, fontWeight: 800 }}>{macro.yield10y}%</div>
                    </div>
                    <div style={{ textAlign: "center" }}>
                      <div style={{ fontSize: 10, color: C.dim }}>USD Index</div>
                      <div style={{ fontSize: 20, fontWeight: 800 }}>{macro.usdIndex}</div>
                    </div>
                  </div>
                ) : <div style={{ color: C.dim, fontSize: 12 }}>Date indisponibile</div>}
              </div>
            </div>

            {/* Top BUY / SELL */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
              {[{ title: "Top BUY", list: topBuy, c: C.buy }, { title: "Top SELL", list: topSell, c: C.sell }].map(({ title, list, c }) => (
                <div key={title} style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 8, padding: 14 }}>
                  <div style={{ color: c, fontSize: 12, fontWeight: 700, marginBottom: 8 }}>{title}</div>
                  {list.length === 0 && <div style={{ color: C.dim, fontSize: 11 }}>Niciun activ</div>}
                  {list.map((a, i) => (
                    <div key={a.ticker} onClick={() => { setSelected(a); setTab("DETALIU"); }}
                      style={{ display: "flex", alignItems: "center", gap: 8, padding: "5px 0", cursor: "pointer",
                        borderBottom: i < list.length - 1 ? `1px solid ${C.border}` : "none" }}>
                      {a.img && <img src={a.img} width={18} height={18} style={{ borderRadius: 9 }} />}
                      <span style={{ flex: 1, fontWeight: 600, fontSize: 12 }}>{a.name}</span>
                      <Delta value={a.var_zi} />
                      <Spark data={a.sparkline} />
                      <span style={{ fontSize: 10, color: C.dim, width: 40, textAlign: "right" }}>S:{a.score}</span>
                    </div>
                  ))}
                </div>
              ))}
            </div>

            {/* Macro indices row */}
            {macro && (
              <div style={{ display: "flex", gap: 10, marginTop: 14, flexWrap: "wrap" }}>
                {[
                  ["S&P 500", macro.sp500, macro.sp500Chg],
                  ["NASDAQ", macro.nasdaq, macro.nasdaqChg],
                  ["Dow Jones", macro.dowjones, macro.dowjonesChg],
                  ["Gold", macro.gold, macro.goldChg],
                  ["Oil WTI", macro.oil, macro.oilChg],
                ].filter(([, v]) => v).map(([label, val, chg]) => (
                  <div key={label} style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 6, padding: "8px 14px", minWidth: 130 }}>
                    <div style={{ fontSize: 10, color: C.dim }}>{label}</div>
                    <div style={{ fontSize: 16, fontWeight: 700 }}>{val?.toLocaleString(undefined, { maximumFractionDigits: 2 })}</div>
                    <Delta value={chg} />
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* ════════ SCREENER ════════ */}
        {tab === "SCREENER" && (
          <div>
            <div style={{ display: "flex", gap: 6, marginBottom: 10, flexWrap: "wrap", alignItems: "center" }}>
              {cats.map(c => <button key={c} style={chip(filter === c)} onClick={() => setFilter(c)}>{c}</button>)}
              <div style={{ width: 1, height: 20, background: C.border, margin: "0 4px" }} />
              {["BUY", "SELL", "WAIT"].map(s => (
                <button key={s} style={chip(signalFilter === s)} onClick={() => setSignalFilter(signalFilter === s ? null : s)}>{s}</button>
              ))}
              <input placeholder="Caută..." value={search} onChange={e => setSearch(e.target.value)}
                style={{ marginLeft: "auto", padding: "5px 10px", background: C.card, border: `1px solid ${C.border}`,
                  borderRadius: 4, color: C.text, fontSize: 11, width: 150, outline: "none", fontFamily: "inherit" }} />
            </div>
            <div style={{ fontSize: 10, color: C.dim, marginBottom: 6 }}>{filtered.length} active</div>
            <div style={{ overflow: "auto", maxHeight: "calc(100vh - 180px)", borderRadius: 6, border: `1px solid ${C.border}` }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                <thead>
                  <tr style={{ background: "#0f1729", position: "sticky", top: 0, zIndex: 1 }}>
                    {[["name","Activ"],["price","Preț"],["var_zi","Zi%"],["var_sapt","7d%"],["rsi","RSI"],["semnal","Semnal"],["trend","Trend"],["score","Score"],["prob","Prob%"]].map(([k, l]) => (
                      <th key={k} onClick={() => { setSortCol(k); setSortDir(sortCol === k ? -sortDir : -1); }}
                        style={{ padding: "7px 6px", color: C.dim, fontWeight: 600, cursor: "pointer",
                          textAlign: k === "name" ? "left" : "right", borderBottom: `1px solid ${C.border}`, fontSize: 10 }}>
                        {l} {sortCol === k ? (sortDir === 1 ? "↑" : "↓") : ""}
                      </th>
                    ))}
                    <th style={{ padding: "7px 6px", color: C.dim, fontWeight: 600, textAlign: "center", borderBottom: `1px solid ${C.border}`, fontSize: 10 }}>7d</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((a, i) => (
                    <tr key={a.ticker + a.name} onClick={() => { setSelected(a); setTab("DETALIU"); }}
                      style={{ background: i % 2 ? C.card : C.bg, cursor: "pointer", borderBottom: `1px solid ${C.border}` }}
                      onMouseEnter={e => e.currentTarget.style.background = C.hover}
                      onMouseLeave={e => e.currentTarget.style.background = i % 2 ? C.card : C.bg}>
                      <td style={{ padding: "6px", display: "flex", alignItems: "center", gap: 6 }}>
                        {a.img && <img src={a.img} width={16} height={16} style={{ borderRadius: 8 }} />}
                        <div><div style={{ fontWeight: 600 }}>{a.name}</div><div style={{ fontSize: 9, color: C.dim }}>{a.ticker}</div></div>
                      </td>
                      <td style={{ padding: "6px", textAlign: "right", fontWeight: 600 }}>${a.price?.toLocaleString(undefined, { maximumFractionDigits: a.price < 1 ? 6 : 2 })}</td>
                      <td style={{ padding: "6px", textAlign: "right" }}><Delta value={a.var_zi} /></td>
                      <td style={{ padding: "6px", textAlign: "right" }}><Delta value={a.var_sapt} /></td>
                      <td style={{ padding: "6px", textAlign: "right", color: a.rsi < 30 ? C.buy : a.rsi > 70 ? C.sell : C.text }}>{a.rsi}</td>
                      <td style={{ padding: "6px", textAlign: "right" }}><Badge signal={a.semnal} /></td>
                      <td style={{ padding: "6px", textAlign: "right", color: a.trend === "Bullish" ? C.buy : a.trend === "Bearish" ? C.sell : C.wait, fontSize: 10 }}>{a.trend}</td>
                      <td style={{ padding: "6px", textAlign: "right", fontWeight: 700, color: a.score >= 3 ? C.buy : a.score <= -3 ? C.sell : C.wait }}>{a.score}</td>
                      <td style={{ padding: "6px", textAlign: "right" }}>{a.prob}%</td>
                      <td style={{ padding: "6px", textAlign: "center" }}><Spark data={a.sparkline} w={60} h={20} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* ════════ DETALIU ════════ */}
        {tab === "DETALIU" && (
          <div>
            {selected ? (
              <>
                <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 16 }}>
                  {selected.img && <img src={selected.img} width={32} height={32} style={{ borderRadius: 16 }} />}
                  <div>
                    <div style={{ fontSize: 20, fontWeight: 800 }}>{selected.name}</div>
                    <div style={{ fontSize: 11, color: C.dim }}>{selected.ticker} • {selected.category}</div>
                  </div>
                  <Badge signal={selected.semnal} big />
                  <div style={{ marginLeft: "auto", textAlign: "right" }}>
                    <div style={{ fontSize: 24, fontWeight: 800 }}>${selected.price?.toLocaleString(undefined, { maximumFractionDigits: selected.price < 1 ? 6 : 2 })}</div>
                    <Delta value={selected.var_zi} />
                  </div>
                </div>

                {/* Sparkline big */}
                {selected.sparkline && selected.sparkline.length > 2 && (
                  <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16, marginBottom: 14 }}>
                    <div style={{ fontSize: 10, color: C.dim, marginBottom: 8 }}>Grafic 7 zile</div>
                    <Spark data={selected.sparkline} w={560} h={100} />
                  </div>
                )}

                <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 10, marginBottom: 14 }}>
                  {[
                    { l: "RSI (14)", v: selected.rsi, c: selected.rsi < 30 ? C.buy : selected.rsi > 70 ? C.sell : C.text },
                    { l: "MACD", v: selected.macdCross?.replace("Impuls ", ""), c: selected.macdCross?.includes("pozitiv") ? C.buy : C.sell },
                    { l: "MA Cross", v: selected.maCross, c: selected.maCross === "Golden Cross" ? C.buy : C.sell },
                    { l: "RVOL", v: selected.rvol + "x", c: selected.rvol > 1.5 ? C.buy : selected.rvol < 0.6 ? C.sell : C.text },
                    { l: "Trend", v: selected.trend, c: selected.trend === "Bullish" ? C.buy : selected.trend === "Bearish" ? C.sell : C.wait },
                    { l: "Score", v: selected.score, c: selected.score >= 3 ? C.buy : selected.score <= -3 ? C.sell : C.wait },
                    { l: "Confluențe", v: selected.confluente + "/5", c: C.text },
                    { l: "Probabilitate", v: selected.prob + "%", c: selected.prob >= 65 ? C.buy : selected.prob >= 50 ? C.wait : C.sell },
                  ].map(({ l, v, c }) => (
                    <div key={l} style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 6, padding: "10px 12px" }}>
                      <div style={{ fontSize: 9, color: C.dim, textTransform: "uppercase", letterSpacing: 0.5 }}>{l}</div>
                      <div style={{ fontSize: 16, fontWeight: 800, color: c, marginTop: 4 }}>{v}</div>
                    </div>
                  ))}
                </div>

                {/* Entry/SL/TP */}
                {selected.sl && (
                  <div style={{ background: C.card, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16 }}>
                    <div style={{ fontSize: 10, color: C.dim, textTransform: "uppercase", letterSpacing: 1, marginBottom: 10 }}>Parametri Tranzacție</div>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 16, textAlign: "center" }}>
                      {[
                        ["Entry", selected.price?.toFixed(2)],
                        ["Stop Loss", selected.sl?.toFixed(2)],
                        ["Take Profit", selected.tp?.toFixed(2)],
                        ["Risk/Reward", rr(selected.price, selected.sl, selected.tp)],
                        ["Prob.", selected.prob + "%"],
                      ].map(([l, v]) => (
                        <div key={l}>
                          <div style={{ fontSize: 10, color: C.dim }}>{l}</div>
                          <div style={{ fontSize: 16, fontWeight: 700, marginTop: 4 }}>{v}</div>
                        </div>
                      ))}
                    </div>
                    <div style={{ marginTop: 12, fontSize: 11, color: C.dim, padding: "8px 12px", background: C.bg, borderRadius: 4 }}>
                      Scoring: RSI({selected.rsi < 35 ? "+2" : selected.rsi < 45 ? "+1" : selected.rsi > 75 ? "-2" : selected.rsi > 65 ? "-1" : "0"})
                      + MACD({selected.macdCross?.includes("pozitiv nou") ? "+2" : selected.macdCross?.includes("pozitiv") ? "+1" : selected.macdCross?.includes("negativ nou") ? "-2" : "-1"})
                      + MA({selected.maCross === "Golden Cross" ? "+2" : "-2"})
                      + RVOL({selected.rvol > 1.5 ? "+1" : selected.rvol < 0.6 ? "-1" : "0"})
                      = <b style={{ color: selected.score >= 3 ? C.buy : selected.score <= -3 ? C.sell : C.wait }}>{selected.score}</b> → <Badge signal={selected.semnal} />
                    </div>
                  </div>
                )}

                <button onClick={() => { setSelected(null); setTab("SCREENER"); }}
                  style={{ marginTop: 12, padding: "6px 14px", background: C.card, border: `1px solid ${C.border}`,
                    borderRadius: 4, color: C.dim, cursor: "pointer", fontSize: 11, fontFamily: "inherit" }}>
                  ← Înapoi la Screener
                </button>
              </>
            ) : (
              <div style={{ textAlign: "center", padding: 60, color: C.dim }}>
                <div style={{ fontSize: 36, marginBottom: 8 }}>📊</div>
                Selectează un activ din Screener sau Dashboard
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
