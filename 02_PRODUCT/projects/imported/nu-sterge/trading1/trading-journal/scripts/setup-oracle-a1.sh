#!/usr/bin/env bash
# ============================================================
# Oracle Cloud A1 Always Free setup script
# Target: Ubuntu 22.04 ARM64 (aarch64)
# What this does:
#   1. Install Docker + Docker Compose
#   2. Configure firewall (only 80/443 open)
#   3. Clone/pull your repo
#   4. Setup systemd timer for auto-updates
# ============================================================
set -euo pipefail

REPO_URL="${REPO_URL:-}"
DOMAIN="${DOMAIN:-}"
APP_DIR="${APP_DIR:-/opt/trading-journal}"

if [ -z "$REPO_URL" ]; then
  echo "Usage: REPO_URL=https://github.com/you/trading-journal.git DOMAIN=trading.yourdomain.com ./setup-oracle-a1.sh"
  exit 1
fi

echo "===> Updating Ubuntu..."
sudo apt-get update -qq
sudo apt-get upgrade -y -qq

echo "===> Installing essentials..."
sudo apt-get install -y -qq \
  ca-certificates \
  curl \
  gnupg \
  git \
  ufw \
  fail2ban

echo "===> Installing Docker..."
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update -qq
sudo apt-get install -y -qq \
  docker-ce \
  docker-ce-cli \
  containerd.io \
  docker-buildx-plugin \
  docker-compose-plugin

sudo usermod -aG docker "$USER"

echo "===> Configuring firewall..."
# Oracle Cloud also has NSG/security lists that MUST allow 80 and 443
# See: OCI Console → Networking → VCN → Security Lists → add ingress for TCP 80, 443
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp comment 'SSH'
sudo ufw allow 80/tcp comment 'HTTP'
sudo ufw allow 443/tcp comment 'HTTPS'
sudo ufw --force enable

echo "===> Enabling fail2ban for SSH protection..."
sudo systemctl enable --now fail2ban

echo "===> Oracle iptables quirk: Oracle A1 has default DROP rules that block 80/443."
echo "     Adding explicit ACCEPT rules..."
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT || true
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT || true
sudo netfilter-persistent save 2>/dev/null || sudo iptables-save | sudo tee /etc/iptables/rules.v4 > /dev/null

echo "===> Cloning repo..."
sudo mkdir -p "$APP_DIR"
sudo chown "$USER:$USER" "$APP_DIR"
if [ -d "$APP_DIR/.git" ]; then
  cd "$APP_DIR" && git pull
else
  git clone "$REPO_URL" "$APP_DIR"
  cd "$APP_DIR"
fi

echo "===> Setting up .env (you MUST edit this!)"
if [ ! -f .env.production ]; then
  cp .env.example .env.production
  echo ""
  echo "*** IMPORTANT: Edit $APP_DIR/.env.production NOW with your real secrets!"
  echo "*** Minimum required:"
  echo "***   JWT_SECRET (openssl rand -base64 32)"
  echo "***   GROQ_API_KEY"
  echo "***   MONGODB_URI"
  echo "***   POLAR_* (when you're ready to monetize)"
  echo ""
fi

if [ -n "$DOMAIN" ]; then
  echo "===> Configuring Caddy for $DOMAIN..."
  sudo sed -i "s/trading-journal.yourdomain.com/$DOMAIN/" "$APP_DIR/deploy/Caddyfile"
fi

echo ""
echo "============================================================"
echo "Setup complete!"
echo "============================================================"
echo ""
echo "Next steps:"
echo "  1. Log out and back in (for docker group membership)"
echo "  2. cd $APP_DIR"
echo "  3. Edit .env.production with your real secrets"
echo "  4. Start: docker compose --env-file .env.production --profile production up -d --build"
echo "  5. Logs:  docker compose logs -f app"
echo "  6. In OCI Console, add ingress rules for TCP 80 and 443 in your VCN security list"
echo ""
echo "For updates later:"
echo "  cd $APP_DIR && git pull && docker compose build && docker compose up -d"
echo ""
