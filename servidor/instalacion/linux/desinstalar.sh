#!/bin/bash
# SONA Deposito - quitar el servicio. Los DATOS (/var/lib/sona-deposito) NO se borran.
[ "$(id -u)" = 0 ] || { echo "Ejecutalo con sudo"; exit 1; }
systemctl disable --now sona-deposito 2>/dev/null
rm -f /etc/systemd/system/sona-deposito.service && systemctl daemon-reload
rm -rf /opt/sona-deposito
echo "Servicio quitado. Los datos siguen en /var/lib/sona-deposito"
