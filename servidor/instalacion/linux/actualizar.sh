#!/bin/bash
# SONA Deposito - actualizar el servidor (los datos no se tocan). sudo ./actualizar.sh
set -e
[ "$(id -u)" = 0 ] || { echo "Ejecutalo con sudo"; exit 1; }
cd "$(dirname "$0")"
systemctl stop sona-deposito
cp -f SonaServidor /opt/sona-deposito/ && chmod +x /opt/sona-deposito/SonaServidor
cp -rf wwwroot /opt/sona-deposito/
systemctl start sona-deposito
sleep 8; systemctl is-active sona-deposito && echo "LISTO: actualizado" || echo "No arranco: journalctl -u sona-deposito -n 50"
