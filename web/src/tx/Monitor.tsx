import { get, fechaHora } from '../api';
import { Chip, Tabla, useAbrir, useDatos, Aviso } from '../comp';

export default function Monitor() {
  const { datos: d, error, recargar } = useDatos(() => get('/api/monitor'), [], ['movimiento', 'autorizacion', 'layout']);
  const abrir = useAbrir();
  const horas = Array.from({ length: 17 }, (_, i) => i + 5);
  const max = Math.max(1, ...(d?.porHora ?? []).map((h: any) => Number(h.n)));
  const ahora = new Date().getHours();
  return (
    <div className="pantalla">
      <h2>Monitor del depósito <code>ZMON</code><button className="boton" style={{ marginLeft: 'auto' }} onClick={recargar}>↻ Actualizar</button></h2>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className="grilla-kpi">
        <div className="kpi"><div className="v">{d?.totales?.hoy ?? '–'}</div><div className="t">Movimientos hoy</div></div>
        <div className="kpi naranja" style={{ cursor: 'pointer' }} onClick={() => abrir('ZAUT')}><div className="v">{d?.autorizacionesPendientes ?? '–'}</div><div className="t">Autorizaciones pendientes</div></div>
        <div className="kpi amarillo" style={{ cursor: 'pointer' }} onClick={() => abrir('ZMOV', { estado: 'pendiente_wms' })}><div className="v">{d?.totales?.pendientes ?? '–'}</div><div className="t">Pendientes de cargar en WMS</div></div>
        <div className="kpi rojo" style={{ cursor: 'pointer' }} onClick={() => abrir('ZMOV', { estado: 'error' })}><div className="v">{d?.totales?.errores ?? '–'}</div><div className="t">Con error del WMS</div></div>
        <div className="kpi verde"><div className="v">{d?.operarios?.length ?? '–'}</div><div className="t">Operarios activos hoy</div></div>
        <div className="kpi"><div className="v">{d?.conectados ?? '–'}</div><div className="t">Equipos conectados ahora</div></div>
      </div>
      <Ocupacion />
      <div className="dos-col">
        <div className="tarjeta">
          <h3>Movimientos por hora (hoy)</h3>
          <div className="barras">
            {horas.map((h) => {
              const n = Number(d?.porHora?.find((x: any) => x.hora === h)?.n ?? 0);
              return <div key={h} className={`b ${h === ahora ? 'ahora' : ''}`} style={{ height: `${(n / max) * 100}%` }}>{n > 0 && <span>{n}</span>}</div>;
            })}
          </div>
          <div className="barras-x">{horas.map((h) => <div key={h}>{h}</div>)}</div>
        </div>
        <div className="tarjeta">
          <h3>Operarios hoy</h3>
          <Tabla filas={d?.operarios ?? []} clave={(f: any) => f.usuario} alDobleClic={(f: any) => abrir('ZMOV', { usuario: f.usuario })} vacio="Todavía no hay movimientos hoy"
            columnas={[
              { id: 'usuario', titulo: 'Operario', render: (f: any) => <b>{f.nombre || f.usuario}</b> },
              { id: 'rol', titulo: 'Rol' },
              { id: 'hoy', titulo: 'Movs.', num: true, valor: (f: any) => Number(f.hoy) },
              { id: 'pendientes', titulo: 'Pend.', num: true, valor: (f: any) => Number(f.pendientes) },
              { id: 'errores', titulo: 'Errores', num: true, valor: (f: any) => Number(f.errores) },
              { id: 'ultimo', titulo: 'Último', render: (f: any) => fechaHora(f.ultimo) },
            ]} />
        </div>
      </div>
      <div className="tarjeta" style={{ flex: 1 }}>
        <h3>Últimos movimientos (en vivo)</h3>
        <Tabla filas={d?.ultimos ?? []} clave={(f: any) => f.id} alDobleClic={(f: any) => abrir('ZMOV', { texto: f.contenedor })}
          columnas={[
            { id: 'hora_servidor', titulo: 'Hora', render: (f: any) => fechaHora(f.hora_servidor) },
            { id: 'usuario', titulo: 'Operario' },
            { id: 'tipo', titulo: 'Tipo', render: (f: any) => <Chip v={f.tipo} tipo /> },
            { id: 'contenedor', titulo: 'Contenedor', mono: true },
            { id: 'descripcion', titulo: 'Artículo', render: (f: any) => `${f.articulo ?? ''} ${f.descripcion ? '· ' + f.descripcion : ''}` },
            { id: 'empresa_nombre', titulo: 'Empresa' },
            { id: 'origen', titulo: 'Origen', mono: true },
            { id: 'destino', titulo: 'Destino', mono: true, render: (f: any) => f.destino ?? f.estado_nuevo },
            { id: 'estado', titulo: 'Estado', render: (f: any) => <Chip v={f.estado} /> },
          ]} />
      </div>
      {d?.layout && <div style={{ color: 'var(--tenue)' }}>Layout sincronizado del WMS: {fechaHora(d.layout.fin)} · {d.layout.posiciones} posiciones{d.layout.errores?.length ? ` · ${d.layout.errores.length} caras con error` : ''}</div>}
    </div>
  );
}

const C = { v: '#22C55E', a: '#F59E0B', r: '#EF4444', x: '#64748B' };

/** Ocupación de posiciones en una franja compacta estilo tablero: medidor del total + una ficha por cara con su barra. Clic en una cara = mapa (ZMAP). */
function Ocupacion() {
  const { datos: d, error } = useDatos(() => get('/api/monitor/ocupacion'), [], ['movimiento', 'layout', 'posiciones']);
  const abrir = useAbrir();
  const caras: any[] = d?.caras ?? [];
  const t = { v: d?.libres ?? 0, a: d?.parciales ?? 0, r: d?.llenas ?? 0, x: d?.inhabilitadas ?? 0, total: d?.total ?? 0 };
  const hab = Math.max(0, t.total - t.x);
  const pct = hab ? Math.round(((t.a + t.r) / hab) * 100) : 0;
  const nivel = (p: number) => (p >= 90 ? 'r' : p >= 70 ? 'a' : 'v');
  if (error) return <Aviso tipo="error">Ocupación: {error}</Aviso>;
  if (d && !t.total) return <Aviso tipo="amarillo">Ocupación: todavía no hay posiciones (sincronizá el layout del WMS en ZEST o cargá las fichas en ZPOS).</Aviso>;

  // medidor circular: arcos de llenas (rojo), con lugar (ámbar) y libres (verde) sobre las habilitadas
  const R = 34, L = 2 * Math.PI * R;
  let acum = 0;
  const arcos = (['r', 'a', 'v'] as const).map((k) => {
    const largo = hab ? (t[k] / hab) * L : 0;
    const a = { k, largo, desde: acum };
    acum += largo;
    return a;
  });
  return (
    <div className="ocup">
      <div className="ocup-medidor" title={`${pct}% de las posiciones habilitadas tiene mercadería`}>
        <svg viewBox="0 0 84 84" width="84" height="84">
          <circle cx="42" cy="42" r={R} className="pista" />
          {arcos.map((a) => a.largo > 0 && (
            <circle key={a.k} cx="42" cy="42" r={R} fill="none" stroke={C[a.k]} strokeWidth="8"
              strokeDasharray={`${Math.max(0, a.largo - 1.5)} ${L}`} strokeDashoffset={-a.desde} transform="rotate(-90 42 42)" className="arco" />
          ))}
        </svg>
        <div className="ocup-pct"><b>{pct}<small>%</small></b><span>ocupado</span></div>
      </div>
      <div className="ocup-kpis">
        <div><i style={{ background: C.v }} /><b>{t.v}</b><span>libres</span></div>
        <div><i style={{ background: C.a }} /><b>{t.a}</b><span>con lugar</span></div>
        <div><i style={{ background: C.r }} /><b>{t.r}</b><span>llenas</span></div>
        <div><i style={{ background: C.x }} /><b>{t.x}</b><span>inhab.</span></div>
      </div>
      <div className="ocup-caras">
        {caras.map((c) => {
          const h = Math.max(1, c.total - c.inhabilitadas);
          const p = Math.round(((c.parciales + c.llenas) / h) * 100);
          return (
            <button key={c.cara} className={`ocup-cara n-${nivel(p)}`} onClick={() => abrir('ZMAP', { cara: c.cara })}
              title={`Cara ${c.cara} · ${p}% ocupado\n${c.libres} libres · ${c.parciales} con lugar · ${c.llenas} llenas · ${c.inhabilitadas} inhabilitadas\nClic: abrir el mapa del pasillo`}>
              <span className="cod">{c.cara}</span><span className="pc">{p}%</span>
              <span className="barra">
                <i style={{ width: `${(c.llenas / h) * 100}%`, background: C.r }} />
                <i style={{ width: `${(c.parciales / h) * 100}%`, background: C.a }} />
                <i style={{ width: `${(c.libres / h) * 100}%`, background: C.v }} />
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
