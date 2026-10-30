import { get, fechaHora } from '../api';
import { Aviso, Tabla, useDatos } from '../comp';

export default function Estado() {
  const est = useDatos(() => get('/api/estado'), [], ['movimiento']);
  const mon = useDatos(() => get('/api/monitor'), []);
  const e = est.datos;
  return (
    <div className="pantalla">
      <h2>Estado y conexiones <code>ZEST</code> <button className="boton" style={{ marginLeft: 'auto' }} onClick={() => { est.recargar(); mon.recargar(); }}>↻</button></h2>
      {est.error && <Aviso tipo="error">{est.error}</Aviso>}
      <div className="grilla-kpi">
        <div className="kpi verde"><div className="v">{e ? 'OK' : '—'}</div><div className="t">Servidor v{e?.version}</div></div>
        <div className="kpi"><div className="v">{e?.pendientesWms ?? '—'}</div><div className="t">Cola hacia el WMS</div></div>
        <div className="kpi"><div className="v">{e?.sesionesWms?.length ?? '—'}</div><div className="t">Sesiones abiertas en el WMS</div></div>
        <div className="kpi"><div className="v">{e?.conectados ?? '—'}</div><div className="t">Equipos en vivo</div></div>
      </div>
      <div className="dos-col">
        <div className="tarjeta">
          <h3>Sesiones en el WMS (red usada)</h3>
          <Tabla filas={e?.sesionesWms ?? []} clave={(s: any) => s.usuario} vacio="Ninguna todavía"
            columnas={[{ id: 'usuario', titulo: 'Usuario' }, { id: 'red', titulo: 'Red', render: (s: any) => (s.red ? (s.red.includes('192.168') ? `Local · ${s.red}` : `Externa · ${s.red}`) : '—') }]} />
        </div>
        <div className="tarjeta">
          <h3>Celulares</h3>
          <Tabla filas={mon.datos?.dispositivos ?? []} clave={(d: any) => d.id} vacio="Ningún celular registrado"
            columnas={[{ id: 'usuario', titulo: 'Usuario' }, { id: 'modelo', titulo: 'Modelo' }, { id: 'version_app', titulo: 'App' }, { id: 'ultimo_contacto', titulo: 'Último contacto', render: (d: any) => fechaHora(d.ultimo_contacto) }]} />
        </div>
      </div>
      {e?.layout && <div className="tarjeta"><h3>Última sincronización del layout</h3><div>{fechaHora(e.layout.inicio)} → {fechaHora(e.layout.fin)} · {e.layout.caras} caras · {e.layout.posiciones} posiciones</div>{e.layout.errores?.map((x: string) => <div key={x} style={{ color: 'var(--error)' }}>{x}</div>)}</div>}
    </div>
  );
}
