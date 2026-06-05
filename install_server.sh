#!/bin/bash
# Deploy probe_server.py to VPS and set up as systemd service
# Run from Windows: bash install_server.sh
set -e

VPS_IP="185.121.12.210"
VPS_USER="root"
VPS_PASS="M51pYFmj"
REMOTE_DIR="/opt/vless-monitor"
PORT=8765
SECRET="vless-monitor-secret-2026"

echo "=== Uploading probe server ==="
sshpass -p "$VPS_PASS" ssh -o StrictHostKeyChecking=no "$VPS_USER@$VPS_IP" \
  "mkdir -p $REMOTE_DIR"

sshpass -p "$VPS_PASS" scp -o StrictHostKeyChecking=no \
  server/probe_server.py "$VPS_USER@$VPS_IP:$REMOTE_DIR/"

echo "=== Installing systemd service ==="
sshpass -p "$VPS_PASS" ssh -o StrictHostKeyChecking=no "$VPS_USER@$VPS_IP" \
  "cat > /etc/systemd/system/vless-probe.service << 'EOF'
[Unit]
Description=VLESS Monitor Probe Server
After=network.target x-ui.service

[Service]
Type=simple
ExecStart=/usr/bin/python3 $REMOTE_DIR/probe_server.py --port $PORT --secret $SECRET
Restart=always
RestartSec=5
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable vless-probe
systemctl restart vless-probe
sleep 2
systemctl status vless-probe --no-pager"

echo ""
echo "=== Opening firewall port $PORT ==="
sshpass -p "$VPS_PASS" ssh -o StrictHostKeyChecking=no "$VPS_USER@$VPS_IP" \
  "ufw allow $PORT/tcp 2>/dev/null || iptables -I INPUT -p tcp --dport $PORT -j ACCEPT 2>/dev/null || true"

echo ""
echo "=== Done! Probe server running on $VPS_IP:$PORT ==="
echo "Test: curl -H 'X-Token: $SECRET' http://$VPS_IP:$PORT/health"
