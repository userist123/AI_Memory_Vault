const express = require('express');
const router = express.Router();
const OpenAI = require('openai');
const SYSTEM_PROMPT = require('../prompt/system');

const ollama = new OpenAI({
  apiKey: 'ollama',
  baseURL: 'http://localhost:11434/v1',
});

router.post('/', async (req, res) => {
  const { message, type, context } = req.body;
  if (!message) return res.status(400).json({ error: 'Mesajul lipseste' });

  const userContent = `TIP: ${type === 'received' ? 'Mesaj PRIMIT de la ea' : 'Mesaj TRIMIS de mine'}\nMESAJ: "${message}"\n${context ? `CONTEXT ANTERIOR: ${context}` : ''}`;

  try {
    const completion = await ollama.chat.completions.create({
      model: 'deepseek-r1:latest',
      messages: [
        { role: 'system', content: SYSTEM_PROMPT },
        { role: 'user', content: userContent }
      ],
      temperature: 0.8,
      max_tokens: 800,
    });

    let reply = completion.choices[0].message.content;

    // DeepSeek-R1 include <think>...</think> in raspuns — il eliminam
    reply = reply.replace(/<think>[\s\S]*?<\/think>/g, '').trim();

    res.json({ reply });
  } catch (err) {
    res.status(500).json({ error: 'Eroare Ollama: ' + err.message });
  }
});

module.exports = router;
