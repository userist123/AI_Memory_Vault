let messageType = 'received';
let history = JSON.parse(localStorage.getItem('rizz_history') || '[]');

document.querySelectorAll('.toggle').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.toggle').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    messageType = btn.dataset.type;
  });
});

document.getElementById('submit').addEventListener('click', async () => {
  const message = document.getElementById('message').value.trim();
  const context = document.getElementById('context').value.trim();
  const resultDiv = document.getElementById('result');
  const btn = document.getElementById('submit');

  if (!message) {
    alert('Scrie un mesaj mai întâi!');
    return;
  }

  resultDiv.classList.remove('hidden');
  resultDiv.innerHTML = '<div class="loading">Se analizează<span class="loading-dots"></span></div>';
  btn.disabled = true;

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message, type: messageType, context })
    });

    const data = await res.json();

    if (data.error) {
      resultDiv.innerHTML = `<span style="color:#f74f6a">Eroare: ${data.error}</span>`;
    } else {
      resultDiv.textContent = data.reply;

      // Save to history
      const item = { message, type: messageType, reply: data.reply, ts: Date.now() };
      history.unshift(item);
      if (history.length > 10) history.pop();
      localStorage.setItem('rizz_history', JSON.stringify(history));
      renderHistory();
    }
  } catch (err) {
    resultDiv.innerHTML = `<span style="color:#f74f6a">Eroare conexiune: ${err.message}</span>`;
  } finally {
    btn.disabled = false;
  }
});

function renderHistory() {
  const container = document.getElementById('history');
  if (history.length === 0) { container.innerHTML = ''; return; }

  container.innerHTML = '<label style="margin-bottom:12px;display:block">Istoricul recent</label>' +
    history.map((item, i) => `
      <div class="history-item" onclick="loadHistory(${i})">
        <div class="msg-preview">${item.type === 'received' ? '📥' : '📤'} "${item.message.substring(0, 60)}${item.message.length > 60 ? '...' : ''}"</div>
        <div>${new Date(item.ts).toLocaleTimeString('ro-RO')}</div>
      </div>
    `).join('');
}

function loadHistory(index) {
  const item = history[index];
  document.getElementById('message').value = item.message;
  const resultDiv = document.getElementById('result');
  resultDiv.classList.remove('hidden');
  resultDiv.textContent = item.reply;

  document.querySelectorAll('.toggle').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.type === item.type);
  });
  messageType = item.type;
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

// Init
renderHistory();
