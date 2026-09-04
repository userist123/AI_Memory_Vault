import dotenv from 'dotenv';
dotenv.config();

import express from 'express';
import cors from 'cors';
import healthRoutes from './routes/healthRoutes.js';
import authRoutes from './routes/authRoutes.js';

dotenv.config();

const app = express();
const PORT = process.env.PORT || 5000;

app.use(cors());
app.use(express.json());

app.use('/api', healthRoutes);
app.use('/api/auth', authRoutes);

// 📋 Basic route to test if our backend is alive
app.get('/', (req, res) => {
  res.send({ message: "System check: Live and running cleanly!" });
});

app.listen(PORT, () => {
  console.log(`🚀 Server running on http://localhost:${PORT}`);
});