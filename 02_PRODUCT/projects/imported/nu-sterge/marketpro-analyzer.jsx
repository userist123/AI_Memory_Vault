import { useState, useMemo, useEffect, useCallback, useRef } from "react";
import * as d3 from "d3";

// ═══════════════════════════════════════════════════════════════
// ASSET DATA — all 95 assets from the Python script
// ═══════════════════════════════════════════════════════════════
const INDICI = {
  "S&P 500":"^GSPC","NASDAQ 100":"^NDX","NASDAQ Comp.":"^IXIC","Dow Jones":"^DJI",
  "Russell 2000":"^RUT","DAX Germany":"^GDAXI","FTSE 100":"^FTSE","CAC 40":"^FCHI",
  "Nikkei 225":"^N225","Hang Seng":"^HSI","Shanghai":"000001.SS","MSCI World":"URTH",
  "MSCI EM":"EEM","BET Romania":"BET.RO"
};
const ACTIUNI = {
  "Apple":"AAPL","Microsoft":"MSFT","NVIDIA":"NVDA","Alphabet":"GOOGL","Amazon":"AMZN",
  "Meta":"META","Tesla":"TSLA","Berkshire B":"BRK-B","JPMorgan":"JPM","Visa":"V",
  "UnitedHealth":"UNH","Exxon Mobil":"XOM","Johnson&Johnson":"JNJ","Procter&Gamble":"PG",
  "ASML":"ASML","Samsung":"005930.KS","TSMC":"TSM","Netflix":"NFLX","Adobe":"ADBE",
  "Salesforce":"CRM","Palantir":"PLTR","AMD":"AMD","Intel":"INTC","Broadcom":"AVGO",
  "Qualcomm":"QCOM","PayPal":"PYPL","Coinbase":"COIN","Robinhood":"HOOD",
  "Cathie Wood ARK":"ARKK","SPY ETF":"SPY"
};
const CRYPTO = {
  "Bitcoin":"BTC-USD","Ethereum":"ETH-USD","BNB":"BNB-USD","Solana":"SOL-USD",
  "XRP":"XRP-USD","Cardano":"ADA-USD","Avalanche":"AVAX-USD","Polkadot":"DOT-USD",
  "Polygon":"MATIC-USD","Chainlink":"LINK-USD","Uniswap":"UNI-USD","Litecoin":"LTC-USD",
  "Dogecoin":"DOGE-USD","Shiba Inu":"SHIB-USD","TRON":"TRX-USD","Stellar":"XLM-USD",
  "Cosmos":"ATOM-USD","Monero":"XMR-USD","Filecoin":"FIL-USD","Internet Computer":"ICP-USD",
  "Hedera":"HBAR-USD","VeChain":"VET-USD","Algorand":"ALGO-USD","Fantom":"FTM-USD",
  "NEAR Protocol":"NEAR-USD"
};
const VALUTE = {
  "EUR/USD":"EURUSD=X","GBP/USD":"GBPUSD=X","USD/JPY":"USDJPY=X","USD/CHF":"USDCHF=X",
  "AUD/USD":"AUDUSD=X","USD/CAD":"USDCAD=X","NZD/USD":"NZDUSD=X","EUR/GBP":"EURGBP=X",
  "EUR/JPY":"EURJPY=X","USD/CNY":"USDCNY=X","USD/HUF":"USDHUF=X","USD/TRY":"USDTRY=X"
};
const MATERII_PRIME = {
  "Gold":"GC=F","Silver":"SI=F","Oil WTI":"CL=F","Oil Brent":"BZ=F","Natural Gas":"NG=F",
  "Copper":"HG=F","Platinum":"PL=F","Palladium":"PA=F","Corn":"ZC=F","Wheat":"ZW=F",
  "Soybeans":"ZS=F","Coffee":"KC=F","Sugar":"SB=F","Cotton":"CT=F"
};

const CATEGORIES = [
  { label: "INDICI", dict: INDICI },
  { label: "ACTIUNI", dict: ACTIUNI },
  { label: "CRYPTO", dict: CRYPTO },
  { label: "VALUTE", dict: VALUTE },
  { label: "MATERII_PRIME", dict: MATERII_PRIME },
];

const RISK_LIBRARY = {
  INDICI:[
    {ID:"R-I-01",Tip:"Sistemic",Descriere:"Recesiune globala / contractie PIB",Impact:5,Probabilitate:30,Orizont:"6-12 luni"},
    {ID:"R-I-02",Tip:"Macro",Descriere:"Crestere agresiva rate dobanda FED",Impact:4,Probabilitate:35,Orizont:"3-6 luni"},
    {ID:"R-I-03",Tip:"Geopolit.",Descriere:"Conflict armat major / tensiuni globale",Impact:4,Probabilitate:25,Orizont:"0-3 luni"},
    {ID:"R-I-04",Tip:"Sectorial",Descriere:"Criza bancara sistemica",Impact:5,Probabilitate:20,Orizont:"3-12 luni"},
    {ID:"R-I-05",Tip:"Tehnic",Descriere:"Spargere suport major / Death Cross",Impact:3,Probabilitate:40,Orizont:"1-3 luni"},
  ],
  ACTIUNI:[
    {ID:"R-A-01",Tip:"Earnings",Descriere:"Rezultate financiare sub asteptari",Impact:3,Probabilitate:45,Orizont:"0-1 luni"},
    {ID:"R-A-02",Tip:"Macro",Descriere:"Stagflatie / crestere costuri",Impact:4,Probabilitate:30,Orizont:"3-9 luni"},
    {ID:"R-A-03",Tip:"Reglem.",Descriere:"Reglementari antitrust",Impact:3,Probabilitate:25,Orizont:"6-18 luni"},
    {ID:"R-A-04",Tip:"Tehnic",Descriere:"RSI supraextins / divergenta bearish",Impact:2,Probabilitate:50,Orizont:"0-1 luni"},
  ],
  CRYPTO:[
    {ID:"R-C-01",Tip:"Reglementar",Descriere:"Interdictie legala crypto",Impact:5,Probabilitate:20,Orizont:"0-6 luni"},
    {ID:"R-C-02",Tip:"Tehnic",Descriere:"Spargere suport / bear market",Impact:4,Probabilitate:40,Orizont:"1-3 luni"},
    {ID:"R-C-03",Tip:"Hack",Descriere:"Exploit exchange / protocol",Impact:5,Probabilitate:15,Orizont:"0-1 luni"},
  ],
  VALUTE:[
    {ID:"R-V-01",Tip:"Macro",Descriere:"Divergenta politici FED/BCE",Impact:4,Probabilitate:40,Orizont:"3-6 luni"},
    {ID:"R-V-02",Tip:"Geopolit.",Descriere:"Sanctiuni comerciale",Impact:3,Probabilitate:25,Orizont:"0-3 luni"},
  ],
  MATERII_PRIME:[
    {ID:"R-M-01",Tip:"Geopolit.",Descriere:"Conflict OPEC+ / embargo",Impact:5,Probabilitate:25,Orizont:"0-3 luni"},
    {ID:"R-M-02",Tip:"Macro",Descriere:"Incetinire economica China",Impact:4,Probabilitate:35,Orizont:"3-12 luni"},
  ],
};

const CALENDAR_LIBRARY = {
  INDICI:["FOMC","NFP","CPI","GDP","PMI","Earnings Season"],
  ACTIUNI:["Earnings Report","FOMC","CPI","NFP","PCE","Retail Sales"],
  CRYPTO:["Bitcoin Halving","FOMC","SEC Ruling","CPI","ETH Upgrade"],
  VALUTE:["FOMC","ECB","BOE","BOJ","CPI SUA","NFP"],
  MATERII_PRIME:["OPEC+","EIA Crude","FOMC","China PMI","USD Index"],
};

// ═══════════════════════════════════════════════════════════════
// TECHNICAL INDICATOR SIMULATION (identical scoring to Python)
// ═══════════════════════════════════════════════════════════════
const seed = (s) => { let h = 0; for(let i=0;i<s.length;i++){h=Math.imul(31,h)+s.charCodeAt(i)|0;} return h; };
const prng = (s) => { let v = seed(s); return () => { v ^= v<<13; v ^= v>>17; v ^= v<<5; return ((v>>>0)/4294967296); }; };

function generateAssetData(name, ticker, category) {
  const r = prng(ticker + "2026");
  const basePrice = category === "CRYPTO" ? (name === "Bitcoin" ? 95000 + r()*10000 :
    name === "Ethereum" ? 3200 + r()*800 : r()*500+1) :
    category === "VALUTE" ? 0.5 + r()*150 :
    category === "MATERII_PRIME" ? (name === "Gold" ? 2300+r()*200 : 10+r()*90) :
    category === "INDICI" ? 5000 + r()*40000 :
    50 + r()*500;

  const price = +(basePrice * (0.97 + r()*0.06)).toFixed(4);
  const rsi = +(20 + r()*60).toFixed(2);
  const varZi = +((r()-0.5)*4).toFixed(2);
  const varSapt = +((r()-0.5)*8).toFixed(2);
  const varLuna = +((r()-0.5)*15).toFixed(2);
  const rvol = +(0.4 + r()*2).toFixed(2);
  const atr = +(price * (0.005 + r()*0.03)).toFixed(4);
  const ma20 = +(price * (0.98 + r()*0.04)).toFixed(4);
  const ma50 = +(price * (0.96 + r()*0.08)).toFixed(4);
  const ma200 = +(price * (0.90 + r()*0.20)).toFixed(4);
  const macross = ma50 > ma200 ? "Golden Cross" : "Death Cross";
  const macdVal = +((r()-0.5)*price*0.01).toFixed(6);
  const macdSig = +((r()-0.5)*price*0.008).toFixed(6);
  const macdHist = +(macdVal - macdSig).toFixed(6);
  const macdCross = macdHist > 0 ? (r()>0.5 ? "Impuls pozitiv nou" : "Impuls pozitiv activ") :
    (r()>0.5 ? "Impuls negativ nou" : "Impuls negativ activ");
  const stochK = +(10 + r()*80).toFixed(2);
  const stochD = +(stochK + (r()-0.5)*10).toFixed(2);
  const bbSup = +(price * 1.03).toFixed(4);
  const bbInf = +(price * 0.97).toFixed(4);
  const momentum = +((r()-0.5)*10).toFixed(2);
  const trend = price > ma50*1.01 ? "Bullish" : price < ma50*0.99 ? "Bearish" : "Sideways";

  // Scoring — IDENTICAL to Python
  let score = 0;
  if (rsi<35) score+=2; else if(rsi<45) score+=1; else if(rsi>75) score-=2; else if(rsi>65) score-=1;
  const mc = macdCross.toLowerCase();
  if(mc.includes("impuls pozitiv nou")) score+=2;
  else if(mc.includes("impuls pozitiv activ")) score+=1;
  else if(mc.includes("impuls negativ nou")) score-=2;
  else if(mc.includes("impuls negativ activ")) score-=1;
  if(macross==="Golden Cross") score+=2; else if(macross==="Death Cross") score-=2;
  if(rvol>1.5) score+=1; else if(rvol<0.6) score-=1;

  const confluente = Math.min(Math.abs(score), 5);
  const semnal = score>=3 ? "BUY" : score<=-3 ? "SELL" : "WAIT";
  const sl = semnal==="BUY" ? +(price-1.5*atr).toFixed(4) : semnal==="SELL" ? +(price+1.5*atr).toFixed(4) : null;
  const tp = semnal==="BUY" ? +(price+3*atr).toFixed(4) : semnal==="SELL" ? +(price-3*atr).toFixed(4) : null;
  const prob = Math.min(90, 35 + confluente*10 + (rvol>1.2?5:0));
  const support = +(price*0.96).toFixed(4);
  const resistance = +(price*1.04).toFixed(4);
  const rsiStatus = rsi<30?"Presiune excesiva vanzare":rsi<45?"Presiune moderata vanzare":rsi<=55?"Echilibru":rsi<=70?"Momentum ascendent":"Presiune excesiva cumparare";

  return {
    name, ticker, category,
    data: "08.04.2026",
    timestamp: "08.04.2026 14:30",
    deschidere: +(price*0.998).toFixed(4),
    maxim: +(price*1.012).toFixed(4),
    minim: +(price*0.988).toFixed(4),
    inchidere: price,
    var_zi_pct: varZi, var_sapt_pct: varSapt, var_luna_pct: varLuna,
    volum: Math.floor(r()*50000000),
    avg_vol_20: Math.floor(r()*40000000),
    rvol,
    rsi, rsi_status: rsiStatus,
    macd: macdVal, macd_signal: macdSig, macd_hist: macdHist, macd_cross: macdCross,
    ma20, ma50, ma200, macross,
    bb_sup: bbSup, bb_inf: bbInf, bb_width: +(bbSup-bbInf).toFixed(4),
    atr, stoch_k: stochK, stoch_d: stochD,
    momentum_10z: momentum,
    trend, semnal, score, confluente,
    sl, tp, probabilitate: prob,
    support, resistance,
  };
}

function generateAllData() {
  const all = [];
  for (const {label, dict} of CATEGORIES) {
    for (const [name, ticker] of Object.entries(dict)) {
      all.push(generateAssetData(name, ticker, label));
    }
  }
  return all;
}

// ═══════════════════════════════════════════════════════════════
// MACRO DATA
// ═══════════════════════════════════════════════════════════════
const MACRO = {
  vix: { value: 18.42, prev: 19.10, label: "VIX" },
  yield10y: { value: 4.32, prev: 4.28, label: "Yield 10Y US" },
  yield2y: { value: 4.78, prev: 4.72, label: "Yield 2Y US" },
  usdIndex: { value: 104.25, prev: 103.90, label: "USD Index" },
  fearGreed: { value: 52, classification: "Neutral", label: "Fear & Greed" },
  fedRate: { value: 5.33, prev: 5.33, label: "Rata Dobânzii FED" },
  cpi: { value: 314.2, prev: 313.0, label: "CPI" },
  unemployment: { value: 3.8, prev: 3.7, label: "Șomaj" },
};

// ═══════════════════════════════════════════════════════════════
// STYLES
// ═══════════════════════════════════════════════════════════════
const C = {
  bg: "#0D1117", bgCard: "#161B22", bgSidebar: "#1F2937", border: "#30363D",
  textPri: "#E6EDF3", textSec: "#8B949E",
  buyBg: "#1E6B3C", buyText: "#3FB950",
  sellBg: "#8B0000", sellText: "#F85149",
  waitBg: "#7D5A00", waitText: "#E3B341",
  accent: "#1F4E79", header: "#0D2137", info: "#DEEAF1",
};

// ═══════════════════════════════════════════════════════════════
// COMPONENTS
// ═══════════════════════════════════════════════════════════════
function SignalBadge({ signal, size = "md" }) {
  const cfg = signal === "BUY" ? { bg: C.buyBg, text: "#fff", icon: "▲", label: "BUY" } :
    signal === "SELL" ? { bg: C.sellBg, text: "#fff", icon: "▼", label: "SELL" } :
    { bg: C.waitBg, text: "#fff", icon: "◆", label: "WAIT" };
  const fs = size === "lg" ? "16px" : size === "sm" ? "10px" : "12px";
  const pad = size === "lg" ? "8px 16px" : size === "sm" ? "2px 6px" : "4px 10px";
  return (
    <span style={{ background: cfg.bg, color: cfg.text, padding: pad, borderRadius: 4,
      fontWeight: 700, fontSize: fs, display: "inline-flex", alignItems: "center", gap: 4, letterSpacing: 0.5 }}>
      {cfg.icon} {cfg.label}
    </span>
  );
}

function PriceChange({ value }) {
  if (value == null) return <span style={{ color: C.textSec }}>N/A</span>;
  const color = value > 0 ? C.buyText : value < 0 ? C.sellText : C.textSec;
  const arrow = value > 0 ? "▲" : value < 0 ? "▼" : "◆";
  return <span style={{ color, fontWeight: 600, fontSize: 13 }}>{arrow} {value > 0 ? "+" : ""}{value.toFixed(2)}%</span>;
}

function FearGreedGauge({ value }) {
  const w = 200, h = 120;
  const zones = [
    { start: 0, end: 20, color: "#B91C1C", label: "Extreme Fear" },
    { start: 20, end: 40, color: "#F97316", label: "Fear" },
    { start: 40, end: 60, color: "#EAB308", label: "Neutral" },
    { start: 60, end: 80, color: "#65A30D", label: "Greed" },
    { start: 80, end: 100, color: "#16A34A", label: "Extreme Greed" },
  ];
  const angle = Math.PI - (value / 100) * Math.PI;
  const needleX = 100 + 65 * Math.cos(angle);
  const needleY = 100 - 65 * Math.sin(angle);
  const classification = zones.find(z => value >= z.start && value < z.end)?.label || "Extreme Greed";
  return (
    <div style={{ textAlign: "center" }}>
      <svg viewBox="0 0 200 120" width={w} height={h}>
        {zones.map((z, i) => {
          const startAngle = Math.PI - (z.start / 100) * Math.PI;
          const endAngle = Math.PI - (z.end / 100) * Math.PI;
          const x1 = 100 + 80 * Math.cos(startAngle), y1 = 100 - 80 * Math.sin(startAngle);
          const x2 = 100 + 80 * Math.cos(endAngle), y2 = 100 - 80 * Math.sin(endAngle);
          const ix1 = 100 + 55 * Math.cos(startAngle), iy1 = 100 - 55 * Math.sin(startAngle);
          const ix2 = 100 + 55 * Math.cos(endAngle), iy2 = 100 - 55 * Math.sin(endAngle);
          return <path key={i} d={`M${ix1},${iy1} A55,55 0 0,0 ${ix2},${iy2} L${x2},${y2} A80,80 0 0,1 ${x1},${y1} Z`}
            fill={z.color} opacity={0.85} />;
        })}
        <line x1="100" y1="100" x2={needleX} y2={needleY} stroke="#fff" strokeWidth="2.5" strokeLinecap="round" />
        <circle cx="100" cy="100" r="5" fill="#fff" />
        <text x="100" y="92" textAnchor="middle" fill="#fff" fontSize="22" fontWeight="bold">{value}</text>
      </svg>
      <div style={{ color: C.textSec, fontSize: 12, marginTop: -4 }}>{classification}</div>
    </div>
  );
}

function RSIGauge({ value }) {
  const angle = Math.PI - (value / 100) * Math.PI;
  const nx = 60 + 38 * Math.cos(angle), ny = 60 - 38 * Math.sin(angle);
  const color = value < 30 ? "#3FB950" : value < 45 ? "#EAB308" : value <= 55 ? "#8B949E" :
    value <= 70 ? "#65A30D" : "#F85149";
  return (
    <svg viewBox="0 0 120 70" width={120} height={70}>
      <path d={`M15,60 A45,45 0 0,1 105,60`} fill="none" stroke={C.border} strokeWidth="8" strokeLinecap="round" />
      <path d={`M15,60 A45,45 0 0,1 105,60`} fill="none" stroke={color} strokeWidth="8" strokeLinecap="round"
        strokeDasharray={`${(value/100)*141.4} 141.4`} />
      <line x1="60" y1="60" x2={nx} y2={ny} stroke="#fff" strokeWidth="2" strokeLinecap="round" />
      <circle cx="60" cy="60" r="3" fill="#fff" />
      <text x="60" y="55" textAnchor="middle" fill="#fff" fontSize="14" fontWeight="bold">{value.toFixed(0)}</text>
    </svg>
  );
}

function MacroCard({ label, value, prev, unit = "" }) {
  const delta = prev != null ? value - prev : null;
  const deltaPct = prev && prev !== 0 ? ((value - prev) / prev * 100) : null;
  return (
    <div style={{ background: C.bgCard, border: `1px solid ${C.border}`, borderRadius: 8, padding: "14px 18px", minWidth: 160 }}>
      <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 6 }}>{label}</div>
      <div style={{ color: C.textPri, fontSize: 22, fontWeight: 700 }}>{typeof value === "number" ? value.toFixed(2) : value}{unit}</div>
      {delta != null && (
        <div style={{ display: "flex", gap: 8, alignItems: "center", marginTop: 4 }}>
          <span style={{ color: delta >= 0 ? C.buyText : C.sellText, fontSize: 12, fontWeight: 600 }}>
            {delta >= 0 ? "▲" : "▼"} {Math.abs(delta).toFixed(2)}
          </span>
          {deltaPct != null && <span style={{ color: C.textSec, fontSize: 11 }}>({deltaPct >= 0 ? "+" : ""}{deltaPct.toFixed(2)}%)</span>}
          <span style={{ color: C.textSec, fontSize: 11 }}>vs anterior</span>
        </div>
      )}
    </div>
  );
}

function generateCandleData(asset) {
  const r = prng(asset.ticker + "candles");
  const candles = [];
  let p = asset.inchidere * 0.92;
  for (let i = 0; i < 60; i++) {
    const o = p;
    const c = o * (0.97 + r() * 0.06);
    const h = Math.max(o, c) * (1 + r() * 0.02);
    const l = Math.min(o, c) * (1 - r() * 0.02);
    candles.push({ date: i, open: o, high: h, low: l, close: c });
    p = c;
  }
  return candles;
}

function CandleChart({ asset }) {
  const candles = useMemo(() => generateCandleData(asset), [asset.ticker]);
  const w = 600, h = 260, pad = { t: 10, r: 10, b: 25, l: 55 };
  const iw = w - pad.l - pad.r, ih = h - pad.t - pad.b;
  const allPrices = candles.flatMap(c => [c.high, c.low]);
  const minP = Math.min(...allPrices), maxP = Math.max(...allPrices);
  const range = maxP - minP || 1;
  const yScale = (v) => pad.t + ih - ((v - minP) / range) * ih;
  const xScale = (i) => pad.l + (i / (candles.length - 1)) * iw;
  const cw = Math.max(3, iw / candles.length * 0.6);

  // MA20 line
  const ma20 = candles.map((_, i) => {
    if (i < 19) return null;
    const slice = candles.slice(i - 19, i + 1);
    return slice.reduce((s, c) => s + c.close, 0) / 20;
  });

  return (
    <svg viewBox={`0 0 ${w} ${h}`} width="100%" style={{ maxWidth: w }}>
      <rect width={w} height={h} fill="transparent" />
      {/* Y grid */}
      {Array.from({ length: 5 }, (_, i) => {
        const v = minP + (range * i) / 4;
        const y = yScale(v);
        return <g key={i}>
          <line x1={pad.l} y1={y} x2={w - pad.r} y2={y} stroke={C.border} strokeWidth={0.5} />
          <text x={pad.l - 4} y={y + 4} textAnchor="end" fill={C.textSec} fontSize={9}>{v.toFixed(v > 1000 ? 0 : 2)}</text>
        </g>;
      })}
      {/* Candles */}
      {candles.map((c, i) => {
        const x = xScale(i);
        const green = c.close >= c.open;
        const color = green ? "#3FB950" : "#F85149";
        const bodyTop = yScale(Math.max(c.open, c.close));
        const bodyBot = yScale(Math.min(c.open, c.close));
        const bodyH = Math.max(1, bodyBot - bodyTop);
        return <g key={i}>
          <line x1={x} y1={yScale(c.high)} x2={x} y2={yScale(c.low)} stroke={color} strokeWidth={1} />
          <rect x={x - cw / 2} y={bodyTop} width={cw} height={bodyH} fill={color} rx={0.5} />
        </g>;
      })}
      {/* MA20 */}
      <path d={ma20.map((v, i) => v != null ? `${i === 0 || ma20[i-1]==null ? "M" : "L"}${xScale(i)},${yScale(v)}` : "").join(" ")}
        fill="none" stroke="#EAB308" strokeWidth={1.5} opacity={0.8} />
    </svg>
  );
}

// ═══════════════════════════════════════════════════════════════
// MAIN APP
// ═══════════════════════════════════════════════════════════════
const TABS = ["DASHBOARD", "SCREENER", "FIȘĂ ACTIV", "MACRO", "RISCURI & CALENDAR"];

export default function MarketProAnalyzer() {
  const [tab, setTab] = useState("DASHBOARD");
  const [allData] = useState(() => generateAllData());
  const [catFilter, setCatFilter] = useState("TOATE");
  const [signalFilter, setSignalFilter] = useState(null);
  const [search, setSearch] = useState("");
  const [sortCol, setSortCol] = useState(null);
  const [sortDir, setSortDir] = useState(1);
  const [selectedAsset, setSelectedAsset] = useState(null);
  const [detailTab, setDetailTab] = useState("GRAFICE");

  const stats = useMemo(() => {
    const buy = allData.filter(a => a.semnal === "BUY");
    const sell = allData.filter(a => a.semnal === "SELL");
    const wait = allData.filter(a => a.semnal === "WAIT");
    const pctBuy = buy.length / allData.length * 100;
    const pctSell = sell.length / allData.length * 100;
    const trendGen = pctBuy > 55 ? "Bullish" : pctSell > 55 ? "Bearish" : "Mixt";
    const best = [...buy].sort((a, b) => (b.score + b.probabilitate * 0.1) - (a.score + a.probabilitate * 0.1))[0] ||
      [...allData].sort((a, b) => b.score - a.score)[0];
    const topBuy = [...buy].sort((a, b) => b.score - a.score).slice(0, 5);
    const topSell = [...sell].sort((a, b) => a.score - b.score).slice(0, 5);
    return { buy: buy.length, sell: sell.length, wait: wait.length, trendGen, best, topBuy, topSell };
  }, [allData]);

  const filtered = useMemo(() => {
    let arr = allData;
    if (catFilter !== "TOATE") arr = arr.filter(a => a.category === catFilter);
    if (signalFilter) arr = arr.filter(a => a.semnal === signalFilter);
    if (search) {
      const s = search.toLowerCase();
      arr = arr.filter(a => a.name.toLowerCase().includes(s) || a.ticker.toLowerCase().includes(s));
    }
    if (sortCol) {
      arr = [...arr].sort((a, b) => {
        const va = a[sortCol], vb = b[sortCol];
        if (va == null && vb == null) return 0;
        if (va == null) return 1;
        if (vb == null) return -1;
        return (va > vb ? 1 : va < vb ? -1 : 0) * sortDir;
      });
    }
    return arr;
  }, [allData, catFilter, signalFilter, search, sortCol, sortDir]);

  const handleSort = (col) => {
    if (sortCol === col) setSortDir(-sortDir);
    else { setSortCol(col); setSortDir(1); }
  };

  const openDetail = (asset) => { setSelectedAsset(asset); setTab("FIȘĂ ACTIV"); setDetailTab("GRAFICE"); };

  const catForAsset = (a) => {
    for (const {label, dict} of CATEGORIES) {
      if (a.name in dict) return label.replace("_PRIME","");
    }
    return "INDICI";
  };

  // ─── STYLES ───
  const sidebarStyle = {
    width: 220, background: C.bgSidebar, borderRight: `1px solid ${C.border}`,
    display: "flex", flexDirection: "column", padding: "16px 0", flexShrink: 0,
  };
  const tabBtnStyle = (active) => ({
    padding: "12px 20px", background: active ? C.accent : "transparent", color: active ? "#fff" : C.textSec,
    border: "none", textAlign: "left", cursor: "pointer", fontSize: 13, fontWeight: active ? 700 : 500,
    borderLeft: active ? "3px solid #58A6FF" : "3px solid transparent", transition: "all .15s",
    fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
  });
  const cardStyle = {
    background: C.bgCard, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16,
  };
  const chipStyle = (active) => ({
    padding: "5px 12px", borderRadius: 6, cursor: "pointer", fontSize: 12, fontWeight: 600, border: "none",
    background: active ? C.accent : C.bgCard, color: active ? "#fff" : C.textSec,
    transition: "all .15s",
  });

  const rr = (entry, sl, tp) => {
    if (!sl || !tp || !entry) return "N/A";
    const risk = Math.abs(entry - sl);
    const reward = Math.abs(tp - entry);
    return risk === 0 ? "N/A" : (reward / risk).toFixed(2) + "x";
  };

  // ─── RENDER ───
  return (
    <div style={{ display: "flex", height: "100vh", background: C.bg, color: C.textPri,
      fontFamily: "'JetBrains Mono', 'Fira Code', 'Courier New', monospace", overflow: "hidden" }}>
      {/* SIDEBAR */}
      <div style={sidebarStyle}>
        <div style={{ padding: "8px 20px 24px", borderBottom: `1px solid ${C.border}`, marginBottom: 8 }}>
          <div style={{ fontSize: 15, fontWeight: 800, color: "#58A6FF", letterSpacing: 1 }}>📊 MarketPro</div>
          <div style={{ fontSize: 10, color: C.textSec, marginTop: 2 }}>Analyzer v2.0</div>
        </div>
        {TABS.map(t => (
          <button key={t} style={tabBtnStyle(tab === t)} onClick={() => setTab(t)}>{t}</button>
        ))}
        <div style={{ marginTop: "auto", padding: "12px 20px", borderTop: `1px solid ${C.border}` }}>
          <div style={{ fontSize: 10, color: C.textSec }}>Actualizat: 08.04.2026 14:30</div>
          <div style={{ fontSize: 10, color: C.textSec, marginTop: 2 }}>95 active monitorizate</div>
        </div>
      </div>

      {/* MAIN CONTENT */}
      <div style={{ flex: 1, overflow: "auto", padding: 24 }}>

        {/* ════════════ DASHBOARD ════════════ */}
        {tab === "DASHBOARD" && (
          <div>
            <h2 style={{ margin: "0 0 20px", fontSize: 20, fontWeight: 800, color: "#58A6FF" }}>Dashboard Principal</h2>

            {/* Stats row */}
            <div style={{ display: "flex", gap: 12, marginBottom: 20, flexWrap: "wrap" }}>
              {[
                { label: "BUY", val: stats.buy, bg: C.buyBg, color: C.buyText },
                { label: "SELL", val: stats.sell, bg: C.sellBg, color: C.sellText },
                { label: "WAIT", val: stats.wait, bg: C.waitBg, color: C.waitText },
              ].map(s => (
                <div key={s.label} style={{ ...cardStyle, display: "flex", alignItems: "center", gap: 12, minWidth: 140 }}>
                  <div style={{ width: 42, height: 42, borderRadius: 8, background: s.bg, display: "flex",
                    alignItems: "center", justifyContent: "center", fontWeight: 800, fontSize: 18, color: s.color }}>{s.val}</div>
                  <div>
                    <div style={{ color: s.color, fontWeight: 700, fontSize: 13 }}>{s.label}</div>
                    <div style={{ color: C.textSec, fontSize: 11 }}>{(s.val / allData.length * 100).toFixed(0)}%</div>
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ height: 6, background: C.border, borderRadius: 3, overflow: "hidden" }}>
                      <div style={{ height: "100%", width: `${(s.val / allData.length * 100)}%`, background: s.color, borderRadius: 3 }} />
                    </div>
                  </div>
                </div>
              ))}
              <div style={{ ...cardStyle, minWidth: 140 }}>
                <div style={{ color: C.textSec, fontSize: 11, marginBottom: 4 }}>Trend General</div>
                <SignalBadge signal={stats.trendGen === "Bullish" ? "BUY" : stats.trendGen === "Bearish" ? "SELL" : "WAIT"} size="md" />
                <div style={{ color: C.textPri, fontWeight: 700, marginTop: 4, fontSize: 14 }}>{stats.trendGen}</div>
              </div>
            </div>

            {/* Best signal + Fear&Greed */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 20 }}>
              {/* Best Signal */}
              {stats.best && (
                <div style={{ ...cardStyle, cursor: "pointer" }} onClick={() => openDetail(stats.best)}>
                  <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 10 }}>
                    Semnal Principal
                  </div>
                  <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 10 }}>
                    <SignalBadge signal={stats.best.semnal} size="lg" />
                    <div>
                      <div style={{ fontSize: 18, fontWeight: 800 }}>{stats.best.name}</div>
                      <div style={{ color: C.textSec, fontSize: 12 }}>{stats.best.ticker}</div>
                    </div>
                  </div>
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr 1fr", gap: 8, fontSize: 11 }}>
                    {[
                      ["Entry", stats.best.inchidere?.toFixed(2)],
                      ["SL", stats.best.sl?.toFixed(2)],
                      ["TP", stats.best.tp?.toFixed(2)],
                      ["R/R", rr(stats.best.inchidere, stats.best.sl, stats.best.tp)],
                    ].map(([l, v]) => (
                      <div key={l}>
                        <div style={{ color: C.textSec }}>{l}</div>
                        <div style={{ fontWeight: 700, color: C.textPri }}>{v || "N/A"}</div>
                      </div>
                    ))}
                  </div>
                  <div style={{ marginTop: 8, fontSize: 11, color: C.textSec }}>
                    RSI={stats.best.rsi.toFixed(0)} | {stats.best.macd_cross} | {stats.best.macross} | RVOL={stats.best.rvol}x | Score={stats.best.score} | Prob={stats.best.probabilitate}%
                  </div>
                </div>
              )}

              {/* Fear & Greed */}
              <div style={cardStyle}>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
                  <div>
                    <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>Fear & Greed Index</div>
                    <FearGreedGauge value={MACRO.fearGreed.value} />
                  </div>
                  <div>
                    <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>VIX</div>
                    <div style={{ fontSize: 32, fontWeight: 800, color: MACRO.vix.value > 25 ? C.sellText : MACRO.vix.value > 18 ? C.waitText : C.buyText }}>
                      {MACRO.vix.value}
                    </div>
                    <PriceChange value={((MACRO.vix.value - MACRO.vix.prev) / MACRO.vix.prev * 100)} />
                    <div style={{ marginTop: 16 }}>
                      <div style={{ color: C.textSec, fontSize: 11, marginBottom: 4 }}>USD Index</div>
                      <div style={{ fontSize: 20, fontWeight: 700 }}>{MACRO.usdIndex.value}</div>
                      <PriceChange value={((MACRO.usdIndex.value - MACRO.usdIndex.prev) / MACRO.usdIndex.prev * 100)} />
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* Top BUY / Top SELL */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
              {[
                { title: "Top BUY", list: stats.topBuy, color: C.buyText },
                { title: "Top SELL", list: stats.topSell, color: C.sellText },
              ].map(({ title, list, color }) => (
                <div key={title} style={cardStyle}>
                  <div style={{ color, fontSize: 13, fontWeight: 700, marginBottom: 10 }}>{title}</div>
                  {list.map((a, i) => (
                    <div key={a.ticker} onClick={() => openDetail(a)}
                      style={{ display: "flex", alignItems: "center", gap: 10, padding: "6px 0",
                        borderBottom: i < list.length - 1 ? `1px solid ${C.border}` : "none", cursor: "pointer" }}>
                      <span style={{ color: C.textSec, fontSize: 11, width: 16 }}>{i + 1}</span>
                      <span style={{ flex: 1, fontWeight: 600, fontSize: 13 }}>{a.name}</span>
                      <PriceChange value={a.var_zi_pct} />
                      <span style={{ fontSize: 11, color: C.textSec, width: 50, textAlign: "right" }}>Score {a.score}</span>
                    </div>
                  ))}
                  {list.length === 0 && <div style={{ color: C.textSec, fontSize: 12 }}>Niciun activ</div>}
                </div>
              ))}
            </div>

            {/* Executive Summary */}
            <div style={{ ...cardStyle, marginTop: 16 }}>
              <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>Rezumat Executiv</div>
              {[
                `Piața prezintă un trend ${stats.trendGen.toLowerCase()} cu ${stats.buy} semnale BUY și ${stats.sell} semnale SELL din ${allData.length} active monitorizate.`,
                `Indicele Fear & Greed la ${MACRO.fearGreed.value} (${MACRO.fearGreed.classification}) sugerează ${MACRO.fearGreed.value > 60 ? "optimism excesiv" : MACRO.fearGreed.value < 40 ? "teamă excesivă" : "echilibru"}.`,
                `VIX la ${MACRO.vix.value} indică volatilitate ${MACRO.vix.value > 25 ? "ridicată" : MACRO.vix.value > 18 ? "moderată" : "scăzută"}.`,
                `Cel mai puternic semnal: ${stats.best?.name || "N/A"} (${stats.best?.semnal || "N/A"}) cu scor ${stats.best?.score || 0} și probabilitate ${stats.best?.probabilitate || 0}%.`,
                `Yield curve spread (10Y-2Y): ${(MACRO.yield10y.value - MACRO.yield2y.value).toFixed(2)}% — ${MACRO.yield10y.value > MACRO.yield2y.value ? "normal" : "inversat (risc recesiune)"}.`,
              ].map((b, i) => (
                <div key={i} style={{ fontSize: 12, color: C.textPri, padding: "4px 0", display: "flex", gap: 8 }}>
                  <span style={{ color: "#58A6FF", fontWeight: 700 }}>•</span> {b}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* ════════════ SCREENER ════════════ */}
        {tab === "SCREENER" && (
          <div>
            <h2 style={{ margin: "0 0 16px", fontSize: 20, fontWeight: 800, color: "#58A6FF" }}>Screener — Prețuri & Semnale</h2>

            {/* Filters */}
            <div style={{ display: "flex", gap: 8, marginBottom: 12, flexWrap: "wrap", alignItems: "center" }}>
              {["TOATE", ...CATEGORIES.map(c => c.label)].map(c => (
                <button key={c} style={chipStyle(catFilter === c)} onClick={() => setCatFilter(c)}>
                  {c.replace("_PRIME", " PRIME")}
                </button>
              ))}
              <div style={{ width: 1, height: 24, background: C.border, margin: "0 4px" }} />
              {["BUY", "SELL", "WAIT"].map(s => (
                <button key={s} style={chipStyle(signalFilter === s)}
                  onClick={() => setSignalFilter(signalFilter === s ? null : s)}>
                  {s}
                </button>
              ))}
              <input placeholder="Caută activ..." value={search} onChange={e => setSearch(e.target.value)}
                style={{ marginLeft: "auto", padding: "6px 12px", background: C.bgCard, border: `1px solid ${C.border}`,
                  borderRadius: 6, color: C.textPri, fontSize: 12, width: 180, outline: "none",
                  fontFamily: "inherit" }} />
            </div>
            <div style={{ fontSize: 11, color: C.textSec, marginBottom: 8 }}>{filtered.length} active afișate</div>

            {/* Table */}
            <div style={{ overflow: "auto", maxHeight: "calc(100vh - 200px)", borderRadius: 8, border: `1px solid ${C.border}` }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <thead>
                  <tr style={{ background: C.header, position: "sticky", top: 0, zIndex: 1 }}>
                    {[
                      ["name", "Activ"], ["inchidere", "Preț"], ["var_zi_pct", "Zi%"], ["var_sapt_pct", "Săpt%"],
                      ["var_luna_pct", "Lună%"], ["rsi", "RSI"], ["semnal", "Semnal"], ["trend", "Trend"],
                      ["macross", "MA Cross"], ["macd_cross", "MACD"], ["rvol", "RVOL"], ["score", "Score"],
                      ["probabilitate", "Prob%"],
                    ].map(([key, label]) => (
                      <th key={key} onClick={() => handleSort(key)}
                        style={{ padding: "8px 6px", color: "#fff", fontWeight: 700, cursor: "pointer",
                          textAlign: key === "name" ? "left" : "right", whiteSpace: "nowrap", fontSize: 11,
                          borderBottom: `2px solid ${C.accent}` }}>
                        {label} {sortCol === key ? (sortDir === 1 ? "▲" : "▼") : ""}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((a, i) => (
                    <tr key={a.ticker} onClick={() => openDetail(a)}
                      style={{ background: i % 2 === 0 ? C.bg : C.bgCard, cursor: "pointer",
                        borderBottom: `1px solid ${C.border}` }}
                      onMouseEnter={e => e.currentTarget.style.background = "#1c2333"}
                      onMouseLeave={e => e.currentTarget.style.background = i % 2 === 0 ? C.bg : C.bgCard}>
                      <td style={{ padding: "7px 6px", fontWeight: 600, textAlign: "left" }}>{a.name}</td>
                      <td style={{ padding: "7px 6px", textAlign: "right", fontWeight: 600 }}>{a.inchidere.toLocaleString(undefined, { maximumFractionDigits: 4 })}</td>
                      <td style={{ padding: "7px 6px", textAlign: "right" }}><PriceChange value={a.var_zi_pct} /></td>
                      <td style={{ padding: "7px 6px", textAlign: "right" }}><PriceChange value={a.var_sapt_pct} /></td>
                      <td style={{ padding: "7px 6px", textAlign: "right" }}><PriceChange value={a.var_luna_pct} /></td>
                      <td style={{ padding: "7px 6px", textAlign: "right",
                        color: a.rsi < 30 ? C.buyText : a.rsi > 70 ? C.sellText : a.rsi < 45 ? C.waitText : C.textPri }}>
                        {a.rsi.toFixed(1)}
                      </td>
                      <td style={{ padding: "7px 6px", textAlign: "right" }}><SignalBadge signal={a.semnal} size="sm" /></td>
                      <td style={{ padding: "7px 6px", textAlign: "right",
                        color: a.trend === "Bullish" ? C.buyText : a.trend === "Bearish" ? C.sellText : C.waitText }}>
                        {a.trend}
                      </td>
                      <td style={{ padding: "7px 6px", textAlign: "right", fontSize: 10,
                        color: a.macross === "Golden Cross" ? C.buyText : C.sellText }}>{a.macross}</td>
                      <td style={{ padding: "7px 6px", textAlign: "right", fontSize: 10,
                        color: a.macd_cross.includes("pozitiv") ? C.buyText : C.sellText }}>
                        {a.macd_cross.replace("Impuls ", "")}
                      </td>
                      <td style={{ padding: "7px 6px", textAlign: "right" }}>{a.rvol}x</td>
                      <td style={{ padding: "7px 6px", textAlign: "right", fontWeight: 700,
                        color: a.score >= 3 ? C.buyText : a.score <= -3 ? C.sellText : C.waitText }}>
                        {a.score}
                      </td>
                      <td style={{ padding: "7px 6px", textAlign: "right",
                        color: a.probabilitate >= 65 ? C.buyText : a.probabilitate >= 50 ? C.waitText : C.sellText }}>
                        {a.probabilitate}%
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* ════════════ FIȘĂ ACTIV ════════════ */}
        {tab === "FIȘĂ ACTIV" && (
          <div>
            {/* Selector */}
            <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
              <select value={selectedAsset?.ticker || ""}
                onChange={e => { const a = allData.find(x => x.ticker === e.target.value); if (a) setSelectedAsset(a); }}
                style={{ padding: "8px 12px", background: C.bgCard, border: `1px solid ${C.border}`,
                  borderRadius: 6, color: C.textPri, fontSize: 13, fontFamily: "inherit", minWidth: 250 }}>
                <option value="">— Selectează activ —</option>
                {CATEGORIES.map(cat => (
                  <optgroup key={cat.label} label={cat.label}>
                    {Object.entries(cat.dict).map(([name, ticker]) => (
                      <option key={ticker} value={ticker}>{name} ({ticker})</option>
                    ))}
                  </optgroup>
                ))}
              </select>
              {selectedAsset && <SignalBadge signal={selectedAsset.semnal} size="lg" />}
              {selectedAsset && <span style={{ fontSize: 22, fontWeight: 800 }}>{selectedAsset.name}</span>}
              {selectedAsset && <span style={{ fontSize: 18, fontWeight: 700 }}>{selectedAsset.inchidere.toLocaleString(undefined, { maximumFractionDigits: 4 })}</span>}
              {selectedAsset && <PriceChange value={selectedAsset.var_zi_pct} />}
            </div>

            {selectedAsset ? (
              <>
                {/* Detail tabs */}
                <div style={{ display: "flex", gap: 4, marginBottom: 16 }}>
                  {["GRAFICE", "INDICATORI", "SEMNAL"].map(t => (
                    <button key={t} style={{ ...chipStyle(detailTab === t), fontSize: 12 }}
                      onClick={() => setDetailTab(t)}>{t}</button>
                  ))}
                </div>

                {detailTab === "GRAFICE" && (
                  <div style={cardStyle}>
                    <div style={{ color: C.textSec, fontSize: 11, marginBottom: 8 }}>Grafic Candlestick (60 zile) + MA20</div>
                    <CandleChart asset={selectedAsset} />
                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginTop: 16 }}>
                      <div>
                        <div style={{ color: C.textSec, fontSize: 11, marginBottom: 4 }}>RSI (14)</div>
                        <RSIGauge value={selectedAsset.rsi} />
                        <div style={{ fontSize: 11, color: C.textSec, marginTop: 4 }}>{selectedAsset.rsi_status}</div>
                      </div>
                      <div>
                        <div style={{ color: C.textSec, fontSize: 11, marginBottom: 4 }}>MACD</div>
                        <div style={{ fontSize: 16, fontWeight: 700, color: selectedAsset.macd_hist > 0 ? C.buyText : C.sellText }}>
                          {selectedAsset.macd_hist > 0 ? "+" : ""}{selectedAsset.macd_hist.toFixed(6)}
                        </div>
                        <div style={{ fontSize: 11, color: selectedAsset.macd_cross.includes("pozitiv") ? C.buyText : C.sellText, marginTop: 4 }}>
                          {selectedAsset.macd_cross}
                        </div>
                        <div style={{ marginTop: 8, fontSize: 11, color: C.textSec }}>
                          MACD: {selectedAsset.macd.toFixed(6)} | Signal: {selectedAsset.macd_signal.toFixed(6)}
                        </div>
                      </div>
                    </div>
                  </div>
                )}

                {detailTab === "INDICATORI" && (
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
                    {[
                      { label: "RSI (14)", value: selectedAsset.rsi.toFixed(2), status: selectedAsset.rsi_status,
                        color: selectedAsset.rsi < 30 ? C.buyText : selectedAsset.rsi > 70 ? C.sellText : C.textPri },
                      { label: "MACD Cross", value: selectedAsset.macd_cross, status: `Hist: ${selectedAsset.macd_hist.toFixed(6)}`,
                        color: selectedAsset.macd_cross.includes("pozitiv") ? C.buyText : C.sellText },
                      { label: "MA Cross", value: selectedAsset.macross, status: `MA50: ${selectedAsset.ma50?.toFixed(2)} | MA200: ${selectedAsset.ma200?.toFixed(2)}`,
                        color: selectedAsset.macross === "Golden Cross" ? C.buyText : C.sellText },
                      { label: "Stochastic", value: `K: ${selectedAsset.stoch_k.toFixed(1)} D: ${selectedAsset.stoch_d.toFixed(1)}`,
                        status: selectedAsset.stoch_k > 80 ? "Supracumpărat" : selectedAsset.stoch_k < 20 ? "Supravândut" : "Neutru",
                        color: C.textPri },
                      { label: "ATR (14)", value: selectedAsset.atr.toFixed(4), status: "Volatilitate",
                        color: C.textPri },
                      { label: "Bollinger Width", value: selectedAsset.bb_width?.toFixed(4) || "N/A",
                        status: `Sup: ${selectedAsset.bb_sup?.toFixed(2)} | Inf: ${selectedAsset.bb_inf?.toFixed(2)}`,
                        color: C.textPri },
                      { label: "RVOL", value: `${selectedAsset.rvol}x`, status: selectedAsset.rvol > 1.5 ? "Volum ridicat" : selectedAsset.rvol < 0.6 ? "Volum scăzut" : "Normal",
                        color: selectedAsset.rvol > 1.5 ? C.buyText : selectedAsset.rvol < 0.6 ? C.sellText : C.textPri },
                      { label: "Momentum 10z", value: `${selectedAsset.momentum_10z.toFixed(2)}%`,
                        status: selectedAsset.momentum_10z > 0 ? "Pozitiv" : "Negativ",
                        color: selectedAsset.momentum_10z > 0 ? C.buyText : C.sellText },
                      { label: "Trend", value: selectedAsset.trend, status: `MA20: ${selectedAsset.ma20?.toFixed(2)}`,
                        color: selectedAsset.trend === "Bullish" ? C.buyText : selectedAsset.trend === "Bearish" ? C.sellText : C.waitText },
                    ].map((ind, i) => (
                      <div key={i} style={cardStyle}>
                        <div style={{ color: C.textSec, fontSize: 10, textTransform: "uppercase", letterSpacing: 1 }}>{ind.label}</div>
                        <div style={{ color: ind.color, fontSize: 18, fontWeight: 800, margin: "6px 0" }}>{ind.value}</div>
                        <div style={{ color: C.textSec, fontSize: 10 }}>{ind.status}</div>
                      </div>
                    ))}
                  </div>
                )}

                {detailTab === "SEMNAL" && (
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
                    <div style={cardStyle}>
                      <div style={{ textAlign: "center", marginBottom: 16 }}>
                        <SignalBadge signal={selectedAsset.semnal} size="lg" />
                        <div style={{ fontSize: 28, fontWeight: 800, marginTop: 8 }}>{selectedAsset.name}</div>
                      </div>
                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                        {[
                          ["Entry Price", selectedAsset.inchidere?.toFixed(4)],
                          ["Stop Loss", selectedAsset.sl?.toFixed(4) || "N/A"],
                          ["Take Profit", selectedAsset.tp?.toFixed(4) || "N/A"],
                          ["Risk/Reward", rr(selectedAsset.inchidere, selectedAsset.sl, selectedAsset.tp)],
                          ["Score", selectedAsset.score],
                          ["Confluențe", `${selectedAsset.confluente}/5`],
                          ["Probabilitate", `${selectedAsset.probabilitate}%`],
                          ["Support", selectedAsset.support?.toFixed(4)],
                          ["Rezistență", selectedAsset.resistance?.toFixed(4)],
                        ].map(([l, v]) => (
                          <div key={l} style={{ padding: "6px 0", borderBottom: `1px solid ${C.border}` }}>
                            <div style={{ color: C.textSec, fontSize: 10 }}>{l}</div>
                            <div style={{ fontWeight: 700, fontSize: 14 }}>{v}</div>
                          </div>
                        ))}
                      </div>
                    </div>
                    <div style={cardStyle}>
                      <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 10 }}>
                        Condiție Declanșare
                      </div>
                      <div style={{ fontSize: 12, lineHeight: 1.8, color: C.textPri }}>
                        <div>• RSI = <b style={{ color: selectedAsset.rsi < 30 ? C.buyText : selectedAsset.rsi > 70 ? C.sellText : C.textPri }}>
                          {selectedAsset.rsi.toFixed(1)}</b> — {selectedAsset.rsi_status}</div>
                        <div>• MACD: <b style={{ color: selectedAsset.macd_cross.includes("pozitiv") ? C.buyText : C.sellText }}>
                          {selectedAsset.macd_cross}</b></div>
                        <div>• MA Cross: <b style={{ color: selectedAsset.macross === "Golden Cross" ? C.buyText : C.sellText }}>
                          {selectedAsset.macross}</b></div>
                        <div>• RVOL = <b>{selectedAsset.rvol}x</b> {selectedAsset.rvol > 1.5 ? "(ridicat)" : selectedAsset.rvol < 0.6 ? "(scăzut)" : "(normal)"}</div>
                        <div>• Trend: <b style={{ color: selectedAsset.trend === "Bullish" ? C.buyText : selectedAsset.trend === "Bearish" ? C.sellText : C.waitText }}>
                          {selectedAsset.trend}</b></div>
                        <div style={{ marginTop: 12, padding: 10, background: C.bg, borderRadius: 6, fontSize: 11 }}>
                          Scoring: RSI({selectedAsset.rsi < 35 ? "+2" : selectedAsset.rsi < 45 ? "+1" : selectedAsset.rsi > 75 ? "-2" : selectedAsset.rsi > 65 ? "-1" : "0"})
                          + MACD({selectedAsset.macd_cross.includes("pozitiv nou") ? "+2" : selectedAsset.macd_cross.includes("pozitiv activ") ? "+1" : selectedAsset.macd_cross.includes("negativ nou") ? "-2" : "-1"})
                          + MA({selectedAsset.macross === "Golden Cross" ? "+2" : "-2"})
                          + RVOL({selectedAsset.rvol > 1.5 ? "+1" : selectedAsset.rvol < 0.6 ? "-1" : "0"})
                          = <b>{selectedAsset.score}</b> → <b>{selectedAsset.semnal}</b>
                        </div>
                      </div>
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div style={{ ...cardStyle, textAlign: "center", padding: 60 }}>
                <div style={{ fontSize: 48, marginBottom: 12 }}>📊</div>
                <div style={{ color: C.textSec, fontSize: 14 }}>Selectează un activ din dropdown sau din Screener</div>
              </div>
            )}
          </div>
        )}

        {/* ════════════ MACRO ════════════ */}
        {tab === "MACRO" && (
          <div>
            <h2 style={{ margin: "0 0 20px", fontSize: 20, fontWeight: 800, color: "#58A6FF" }}>Indicatori Macro</h2>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 14, marginBottom: 20 }}>
              <MacroCard label="VIX" value={MACRO.vix.value} prev={MACRO.vix.prev} />
              <MacroCard label="Yield 10Y US" value={MACRO.yield10y.value} prev={MACRO.yield10y.prev} unit="%" />
              <MacroCard label="Yield 2Y US" value={MACRO.yield2y.value} prev={MACRO.yield2y.prev} unit="%" />
              <MacroCard label="USD Index" value={MACRO.usdIndex.value} prev={MACRO.usdIndex.prev} />
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 14, marginBottom: 20 }}>
              <div style={cardStyle}>
                <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 12 }}>Fear & Greed Index</div>
                <FearGreedGauge value={MACRO.fearGreed.value} />
              </div>
              <MacroCard label="Rata Dobânzii FED" value={MACRO.fedRate.value} prev={MACRO.fedRate.prev} unit="%" />
              <MacroCard label="CPI" value={MACRO.cpi.value} prev={MACRO.cpi.prev} />
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
              <MacroCard label="Șomaj SUA" value={MACRO.unemployment.value} prev={MACRO.unemployment.prev} unit="%" />
              <div style={cardStyle}>
                <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>Yield Curve Spread (10Y - 2Y)</div>
                <div style={{ fontSize: 28, fontWeight: 800,
                  color: (MACRO.yield10y.value - MACRO.yield2y.value) < 0 ? C.sellText : C.buyText }}>
                  {(MACRO.yield10y.value - MACRO.yield2y.value).toFixed(2)}%
                </div>
                <div style={{ color: C.textSec, fontSize: 12, marginTop: 4 }}>
                  {(MACRO.yield10y.value - MACRO.yield2y.value) < 0 ? "⚠️ Curbă inversată — risc recesiune" : "✅ Curbă normală"}
                </div>
              </div>
            </div>
            <div style={{ ...cardStyle, marginTop: 16 }}>
              <div style={{ color: C.textSec, fontSize: 11, textTransform: "uppercase", letterSpacing: 1, marginBottom: 8 }}>Interpretare Automată</div>
              <div style={{ fontSize: 12, lineHeight: 1.8, color: C.textPri }}>
                <div>• VIX la <b>{MACRO.vix.value}</b> — volatilitate {MACRO.vix.value > 25 ? "ridicată, piețe agitate" : MACRO.vix.value > 18 ? "moderată" : "scăzută, piețe calme"}.</div>
                <div>• Fear & Greed <b>{MACRO.fearGreed.value}</b> ({MACRO.fearGreed.classification}) — {MACRO.fearGreed.value > 60 ? "atenție la euforie" : MACRO.fearGreed.value < 40 ? "potențial oportunități de cumpărare" : "sentiment echilibrat"}.</div>
                <div>• Rata FED <b>{MACRO.fedRate.value}%</b> — {MACRO.fedRate.value > 5 ? "politică monetară restrictivă" : "politică monetară relaxată"}.</div>
                <div>• Inflația (CPI) la <b>{MACRO.cpi.value}</b> — {MACRO.cpi.value > MACRO.cpi.prev ? "în creștere" : "în scădere"}.</div>
                <div>• Șomaj <b>{MACRO.unemployment.value}%</b> — {MACRO.unemployment.value < 4 ? "piață a muncii solidă" : "piață a muncii în deteriorare"}.</div>
              </div>
            </div>
          </div>
        )}

        {/* ════════════ RISCURI & CALENDAR ════════════ */}
        {tab === "RISCURI & CALENDAR" && (
          <div>
            <h2 style={{ margin: "0 0 20px", fontSize: 20, fontWeight: 800, color: "#58A6FF" }}>Matrice Riscuri & Calendar Evenimente</h2>
            {Object.entries(RISK_LIBRARY).map(([cat, risks]) => (
              <div key={cat} style={{ marginBottom: 24 }}>
                <div style={{ background: C.header, padding: "8px 14px", borderRadius: "8px 8px 0 0",
                  fontWeight: 700, fontSize: 13, color: "#fff" }}>{cat.replace("_PRIME", " PRIME")}</div>
                <div style={{ border: `1px solid ${C.border}`, borderTop: "none", borderRadius: "0 0 8px 8px", overflow: "hidden" }}>
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                    <thead>
                      <tr style={{ background: C.bgCard }}>
                        {["ID", "Tip", "Descriere", "Impact", "Prob%", "Orizont"].map(h => (
                          <th key={h} style={{ padding: "6px 10px", color: C.textSec, textAlign: "left", fontWeight: 600,
                            borderBottom: `1px solid ${C.border}` }}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {risks.map((r, i) => (
                        <tr key={r.ID} style={{ background: i % 2 === 0 ? C.bg : C.bgCard }}>
                          <td style={{ padding: "6px 10px", color: C.textSec }}>{r.ID}</td>
                          <td style={{ padding: "6px 10px" }}>{r.Tip}</td>
                          <td style={{ padding: "6px 10px" }}>{r.Descriere}</td>
                          <td style={{ padding: "6px 10px", textAlign: "center" }}>
                            <span style={{ background: r.Impact >= 4 ? C.sellBg : r.Impact >= 3 ? C.waitBg : C.bgCard,
                              padding: "2px 8px", borderRadius: 4, fontWeight: 700,
                              color: r.Impact >= 4 ? C.sellText : r.Impact >= 3 ? C.waitText : C.textPri }}>
                              {r.Impact}/5
                            </span>
                          </td>
                          <td style={{ padding: "6px 10px", textAlign: "center" }}>{r.Probabilitate}%</td>
                          <td style={{ padding: "6px 10px", color: C.textSec }}>{r.Orizont}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* Calendar for this category */}
                {CALENDAR_LIBRARY[cat] && (
                  <div style={{ display: "flex", gap: 8, marginTop: 8, flexWrap: "wrap" }}>
                    <span style={{ color: C.textSec, fontSize: 11, marginRight: 4 }}>Evenimente cheie:</span>
                    {CALENDAR_LIBRARY[cat].map(ev => (
                      <span key={ev} style={{ padding: "3px 10px", background: C.bgCard, border: `1px solid ${C.border}`,
                        borderRadius: 12, fontSize: 11, color: "#58A6FF" }}>{ev}</span>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
