/* OTP Flight Finder – app.js
   Vanilla JS ES2020, no build step
   UTF-8, no BOM
*/

'use strict';

const API_BASE = '';   // same origin

// ── DOM refs ──────────────────────────────────────────────────────────────────
const form        = document.getElementById('searchForm');
const destFilter  = document.getElementById('destFilter');
const destSelect  = document.getElementById('destination');
const depDate     = document.getElementById('depDate');
const retDate     = document.getElementById('retDate');
const retDateWrap = document.getElementById('retDateWrap');
const adultCount  = document.getElementById('adultCount');
const adultsInput = document.getElementById('adults');
const btnMinus    = document.getElementById('btnAdultMinus');
const btnPlus     = document.getElementById('btnAdultPlus');
const btnOneway   = document.getElementById('btnOneway');
const btnRoundtrip= document.getElementById('btnRoundtrip');
const skeleton    = document.getElementById('skeleton');
const emptyState  = document.getElementById('emptyState');
const errorState  = document.getElementById('errorState');
const errorMsg    = document.getElementById('errorMsg');
const resultsGrid = document.getElementById('resultsGrid');
const searchBtn   = document.getElementById('searchBtn');

// ── State ─────────────────────────────────────────────────────────────────────
let isRoundtrip   = false;
let adults        = 1;
let allDests      = [];   // [{iata, name, country}]

// ── Init ──────────────────────────────────────────────────────────────────────
(function init() {
  setDefaultDates();
  loadDestinations();
  bindEvents();
})();

// ── Set default dates ─────────────────────────────────────────────────────────
function setDefaultDates() {
  const today    = new Date();
  const tomorrow = new Date(today);
  tomorrow.setDate(today.getDate() + 1);

  const fmt = (d) => d.toISOString().split('T')[0];
  const todayStr = fmt(today);
  const tomStr   = fmt(tomorrow);

  depDate.min   = todayStr;
  depDate.value = tomStr;
  retDate.min   = tomStr;
}

// ── Bind all UI events ────────────────────────────────────────────────────────
function bindEvents() {
  // Form submit
  form.addEventListener('submit', searchFlights);

  // Trip-type toggle
  btnOneway.addEventListener('click', () => setTripType(false));
  btnRoundtrip.addEventListener('click', () => setTripType(true));

  // Adult counter
  btnMinus.addEventListener('click', () => changeAdults(-1));
  btnPlus.addEventListener('click',  () => changeAdults(+1));

  // Dep date → update ret date min
  depDate.addEventListener('change', () => {
    retDate.min = depDate.value;
    if (retDate.value && retDate.value < depDate.value) {
      retDate.value = depDate.value;
    }
  });

  // Destination filter
  destFilter.addEventListener('input', filterDestinations);
}

// ── Trip type ─────────────────────────────────────────────────────────────────
function setTripType(roundtrip) {
  isRoundtrip = roundtrip;

  if (roundtrip) {
    retDateWrap.classList.remove('hidden');
    btnRoundtrip.classList.add('trip-btn-active');
    btnOneway.classList.remove('trip-btn-active');
    btnRoundtrip.setAttribute('aria-pressed', 'true');
    btnOneway.setAttribute('aria-pressed', 'false');
  } else {
    retDateWrap.classList.add('hidden');
    btnOneway.classList.add('trip-btn-active');
    btnRoundtrip.classList.remove('trip-btn-active');
    btnOneway.setAttribute('aria-pressed', 'true');
    btnRoundtrip.setAttribute('aria-pressed', 'false');
    retDate.value = '';
  }
}

// ── Adult counter ─────────────────────────────────────────────────────────────
function changeAdults(delta) {
  adults = Math.min(9, Math.max(1, adults + delta));
  adultCount.textContent   = adults;
  adultsInput.value        = adults;
  btnMinus.disabled        = adults === 1;
  btnPlus.disabled         = adults === 9;
}

// ── Load destinations ─────────────────────────────────────────────────────────
async function loadDestinations() {
  try {
    const res = await fetch(`${API_BASE}/api/destinations`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    allDests = await res.json();           // [{iata, name, country}] or [strings]
    populateSelect(allDests);
  } catch (err) {
    console.warn('Could not load destinations:', err.message);
    // Fallback: leave select empty with placeholder
  }
}

function populateSelect(dests) {
  // Clear existing options except placeholder
  while (destSelect.options.length > 1) {
    destSelect.remove(1);
  }
  dests.forEach((d) => {
    const opt   = document.createElement('option');
    // Support both object {iata, name, country} and plain strings
    if (typeof d === 'string') {
      opt.value      = d;
      opt.textContent = d;
    } else {
      opt.value       = d.iata || d.code || d.name || d;
      opt.textContent = d.name
        ? `${d.iata ? d.iata + ' – ' : ''}${d.name}${d.country ? ', ' + d.country : ''}`
        : String(d);
      opt.dataset.label = opt.textContent.toLowerCase();
    }
    destSelect.appendChild(opt);
  });
}

// ── Destination filter ────────────────────────────────────────────────────────
function filterDestinations() {
  const q = destFilter.value.toLowerCase().trim();
  const filtered = q
    ? allDests.filter((d) => {
        const label = typeof d === 'string'
          ? d.toLowerCase()
          : `${d.iata || ''} ${d.name || ''} ${d.country || ''}`.toLowerCase();
        return label.includes(q);
      })
    : allDests;
  populateSelect(filtered);
}

// ── Search ────────────────────────────────────────────────────────────────────
async function searchFlights(e) {
  e.preventDefault();

  const dest = destSelect.value;
  if (!dest) {
    destSelect.focus();
    destSelect.style.borderColor = 'rgba(239,68,68,0.7)';
    setTimeout(() => { destSelect.style.borderColor = ''; }, 2000);
    return;
  }
  if (!depDate.value) {
    depDate.focus();
    return;
  }

  const params = new URLSearchParams({
    destination: dest,
    dep_date:    depDate.value,
    adults,
  });
  if (isRoundtrip && retDate.value) {
    params.append('ret_date', retDate.value);
  }

  setLoading(true);
  clearResults();

  try {
    const res = await fetch(`${API_BASE}/api/search?${params}`);
    if (!res.ok) throw new Error(`Server error ${res.status}`);
    const data = await res.json();
    renderResults(data);
  } catch (err) {
    showError(err.message);
  } finally {
    setLoading(false);
  }
}

// ── Render results ────────────────────────────────────────────────────────────
function renderResults(deals) {
  if (!Array.isArray(deals) || deals.length === 0) {
    emptyState.classList.remove('hidden');
    return;
  }
  const frag = document.createDocumentFragment();
  deals.forEach((deal) => {
    frag.appendChild(renderCard(deal));
  });
  resultsGrid.appendChild(frag);
}

// ── Render a single flight card ───────────────────────────────────────────────
function renderCard(deal) {
  const {
    airline      = 'Unknown',
    destination  = '',
    dep_date     = '',
    ret_date     = '',
    adults: pax  = 1,
    price        = 0,
    deep_link    = '#',
    currency     = 'EUR',
  } = deal;

  const card = document.createElement('article');
  card.className = 'flight-card p-5 flex flex-col gap-3';
  card.setAttribute('aria-label', `Zbor ${airline} spre ${destination}, ${formatPrice(price, currency)}`);

  // Header row: badge + route
  const header = document.createElement('div');
  header.className = 'flex items-start justify-between gap-2 flex-wrap';
  header.innerHTML = `
    <span class="badge ${airlineBadgeClass(airline)}" aria-label="Companie aeriană: ${escHtml(airline)}">
      ${escHtml(airline)}
    </span>
    <span class="route-display ml-auto" aria-label="Rută: OTP spre ${escHtml(destination)}">
      OTP → ${escHtml(destination)}
    </span>
  `;

  // Divider
  const hr1 = document.createElement('hr');
  hr1.className = 'glass-divider';

  // Details
  const details = document.createElement('div');
  details.className = 'text-sm space-y-1';
  details.style.color = '#94a3b8';

  const rows = [
    ['✈ Plecare',     formatDate(dep_date)],
    ret_date ? ['🔄 Întoarcere', formatDate(ret_date)] : null,
    ['👤 Adulți',     String(pax)],
  ].filter(Boolean);

  details.innerHTML = rows.map(([label, value]) => `
    <div class="flex justify-between">
      <span>${escHtml(label)}</span>
      <span class="font-medium" style="color:#e2e8f0;">${escHtml(value)}</span>
    </div>
  `).join('');

  // Divider
  const hr2 = document.createElement('hr');
  hr2.className = 'glass-divider';

  // Price + CTA row
  const footer = document.createElement('div');
  footer.className = 'flex items-end justify-between gap-3 mt-auto';
  footer.innerHTML = `
    <div>
      <div class="price-label">de la</div>
      <div class="price-display" aria-label="Preț: ${formatPrice(price, currency)}">${formatPrice(price, currency)}</div>
    </div>
    <button class="reserve-btn" aria-label="Rezervă zbor ${escHtml(airline)} spre ${escHtml(destination)}">
      Rezervă →
    </button>
  `;

  // Reserve button event
  const reserveBtn = footer.querySelector('.reserve-btn');
  reserveBtn.addEventListener('click', () => {
    if (deep_link && deep_link !== '#') {
      window.open(deep_link, '_blank', 'noopener,noreferrer');
    }
  });

  card.appendChild(header);
  card.appendChild(hr1);
  card.appendChild(details);
  card.appendChild(hr2);
  card.appendChild(footer);

  return card;
}

// ── Loading skeleton ──────────────────────────────────────────────────────────
function setLoading(loading) {
  searchBtn.disabled = loading;
  searchBtn.textContent = loading ? 'Se caută…' : 'Caută zboruri →';

  if (loading) {
    skeleton.classList.remove('hidden');
    emptyState.classList.add('hidden');
    errorState.classList.add('hidden');
  } else {
    skeleton.classList.add('hidden');
  }
}

// ── Clear results ─────────────────────────────────────────────────────────────
function clearResults() {
  resultsGrid.innerHTML = '';
  emptyState.classList.add('hidden');
  errorState.classList.add('hidden');
}

// ── Show error ────────────────────────────────────────────────────────────────
function showError(msg) {
  errorState.classList.remove('hidden');
  errorMsg.textContent = msg || 'A apărut o eroare neașteptată.';
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/**
 * Format a price number with currency symbol.
 * @param {number} amount
 * @param {string} [currency='EUR']
 */
function formatPrice(amount, currency = 'EUR') {
  const sym = { EUR: '€', USD: '$', RON: 'RON ', GBP: '£' }[currency] ?? (currency + ' ');
  return `${sym}${Number(amount).toFixed(0)}`;
}

/**
 * Return badge CSS class for a given airline name.
 * @param {string} airline
 */
function airlineBadgeClass(airline) {
  const a = (airline || '').toLowerCase();
  if (a.includes('ryanair'))      return 'badge-ryanair';
  if (a.includes('wizz'))         return 'badge-wizz';
  if (a.includes('blue air') || a.includes('blueair')) return 'badge-blueair';
  if (a.includes('tarom'))        return 'badge-tarom';
  return 'badge-other';
}

/**
 * Format an ISO date string to a readable Romanian format.
 * @param {string} iso  e.g. "2026-09-15"
 */
function formatDate(iso) {
  if (!iso) return '—';
  const [year, month, day] = iso.split('-');
  const months = ['ian','feb','mar','apr','mai','iun','iul','aug','sep','oct','nov','dec'];
  return `${parseInt(day, 10)} ${months[parseInt(month, 10) - 1]} ${year}`;
}

/**
 * Escape HTML special characters to prevent XSS.
 * @param {string} str
 */
function escHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
