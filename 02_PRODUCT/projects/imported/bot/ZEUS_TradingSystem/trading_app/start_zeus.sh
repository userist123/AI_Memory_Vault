#!/bin/bash
echo "⚡ Pornire ZEUS Trading System..."
cd "$(dirname "$0")"
pip install -r requirements.txt -q
python3 main.py
