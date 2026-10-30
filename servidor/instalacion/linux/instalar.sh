#!/bin/bash
# SONA Deposito - instalar el servidor como servicio (systemd). Ejecutar con: sudo ./instalar.sh
set -e
[ "$(id -u)" = 0 ] || { echo "Ejecutalo con sudo: sudo ./instalar.sh"; exit 1; }
cd "$(dirname "$0")"
# dependencias: ICU (textos y fechas en español)
if command -v apt-get >/dev/null; then apt-get install -y libicu-dev >/dev/null 2>&1 || apt-get install -y libicu* >/dev/null 2>&1 || true
elif command -v dnf >/dev/null; then dnf install -y libicu >/dev/null 2>&1 || true
elif command -v yum >/dev/null; then yum install -y libicu >/dev/null 2>&1 || true; fi
id sona >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin sona
systemctl stop sona-deposito 2>/dev/null || true
mkdir -p /opt/sona-deposito /var/lib/sona-deposito
cp -f SonaServidor /opt/sona-deposito/
cp -rf wwwroot /opt/sona-deposito/
chmod +x /opt/sona-deposito/SonaServidor
chown -R sona:sona /var/lib/sona-deposito
cp -f sona-deposito.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now sona-deposito
# firewall (si hay)
if command -v ufw >/dev/null && ufw status | grep -q active; then ufw allow 8080/tcp; fi
if command -v firewall-cmd >/dev/null && systemctl is-active --quiet firewalld; then firewall-cmd --permanent --add-port=8080/tcp && firewall-cmd --reload; fi
sleep 8
if systemctl is-active --quiet sona-deposito; then
  echo; echo " LISTO: el servicio sona-deposito esta funcionando y arranca solo."
  echo " Datos:      /var/lib/sona-deposito"
  echo " Registro:   /var/lib/sona-deposito/registro   (o: journalctl -u sona-deposito -f)"
  echo " Produccion: http://$(hostname -I | awk '{print $1}'):8080/produccion"
else
  echo " No arranco. Motivo:"; cat /var/lib/sona-deposito/registro/arranque_error.txt 2>/dev/null | tail -3; echo " Mas detalle: journalctl -u sona-deposito -n 50"
fi
