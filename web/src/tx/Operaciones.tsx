// Operaciones del depósito desde el panel web: las mismas de la app (Transferir, Guardar, Reubicar cajas, Ajustar,
// Cambiar estado, Crear contenedor, Enviar a NN, Eliminar). Todo pasa por /api/movimientos igual que desde el celular,
// así que el servidor controla los permisos y las autorizaciones de zona.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { get, post, subir, sesion, alEvento } from '../api';
import { Aviso, Campo, Chip, Tabla, useAbrir, useMensaje } from '../comp';
import type { PropsTx } from '../App';

// GUID v4 (el servidor lo exige). crypto.randomUUID solo existe en https/localhost: en http://192.168… se arma con getRandomValues.
const uuid = (): string => {
  if (typeof crypto !== 'undefined' && typeof (crypto as any).randomUUID === 'function') { try { return (crypto as any).randomUUID(); } catch { /* contexto no seguro */ } }
  const b = new Uint8Array(16);
  if (typeof crypto !== 'undefined' && crypto.getRandomValues) crypto.getRandomValues(b); else for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256);
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
};
const codigos = (t: string) => [...new Set(t.toUpperCase().split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean))];
const entero = (s: string) => (/^\d+$/.test(s.trim()) ? Number(s.trim()) : null);
const numero = (s: string) => { const n = Number(s.trim().replace(',', '.')); return s.trim() && Number.isFinite(n) ? n : null; };
const mov = (tipo: string, contenedor: string, grupo = uuid()): Record<string, any> => ({ id: uuid(), grupo, tipo, contenedor: contenedor.trim().toUpperCase() });

interface Registro { id: string; hora: string; tipo: string; contenedor: string; detalle: string; estado: string; mensaje: string }
type Res = { texto: string; tipo: 'ok' | 'error' | 'info' | 'amarillo' } | null;

/** Lo que tienen en común todas las operaciones: formulario a la izquierda y, a la derecha, lo registrado con su estado en el WMS (en vivo). */
function useOperacion() {
  const [registros, setRegistros] = useState<Registro[]>([]);
  const [res, setRes] = useState<Res>(null);
  const [ocupado, setOcupado] = useState(false);
  const mostrar = useMensaje();
  const ref = useRef(registros);
  ref.current = registros;

  const refrescar = async () => {
    const abiertos = ref.current.filter((r) => ['pendiente_wms', 'esperando_autorizacion', 'registrado'].includes(r.estado));
    if (!abiertos.length) return;
    const nuevos = await Promise.all(abiertos.map(async (r) => {
      try { const m = await get(`/api/movimientos/${encodeURIComponent(r.id)}`); return { ...r, estado: m.estado ?? r.estado, mensaje: m.respuesta_wms || m.comentario || r.mensaje }; } catch { return r; }
    }));
    const porId = new Map(nuevos.map((n) => [n.id, n]));
    setRegistros((rs) => rs.map((r) => porId.get(r.id) ?? r));
  };
  useEffect(() => {
    let t: number | undefined;
    const off = alEvento((e) => { if (e.tipo === 'movimiento' || e.tipo === 'autorizacion') { clearTimeout(t); t = window.setTimeout(refrescar, 500); } });
    const i = window.setInterval(refrescar, 15000);
    return () => { off(); clearInterval(i); clearTimeout(t); };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  /** Manda los movimientos y los agrega a la lista de la derecha. */
  const registrar = async (movs: Record<string, any>[], detalle: string) => {
    const r = await post('/api/movimientos', { movimientos: movs });
    const resultados: any[] = r?.resultados ?? [];
    const hora = new Date().toLocaleTimeString('es-AR');
    setRegistros((rs) => [...movs.map((m) => {
      const x = resultados.find((y) => y.id === m.id);
      return { id: m.id, hora, tipo: m.tipo, contenedor: m.contenedor, detalle, estado: x?.estado || 'registrado', mensaje: x?.mensaje ?? '' };
    }), ...rs]);
    const rech = resultados.filter((x) => x.estado === 'rechazado');
    const esp = resultados.filter((x) => x.estado === 'esperando_autorizacion').length;
    if (rech.length) setRes({ tipo: 'error', texto: `✖ ${rech.length} rechazado(s): ${[...new Set(rech.map((x) => x.mensaje))].join(' · ')}` });
    else if (esp) setRes({ tipo: 'amarillo', texto: `⏳ ${esp} esperando autorización de zona (ZAUT)` });
    else setRes({ tipo: 'ok', texto: `✔ ${movs.length} movimiento(s) registrado(s): se cargan en el WMS en segundo plano.` });
    return resultados;
  };

  /** Corre una acción mostrando el error si falla. */
  const hacer = async (f: () => Promise<void>) => {
    setOcupado(true);
    try { await f(); } catch (e) { setRes({ tipo: 'error', texto: '✖ ' + (e as Error).message }); mostrar((e as Error).message, 'error'); } finally { setOcupado(false); }
  };
  return { registros, res, setRes, ocupado, registrar, hacer };
}

function Marco({ codigo, titulo, ayuda, op, children }: { codigo: string; titulo: string; ayuda: string; op: ReturnType<typeof useOperacion>; children: ReactNode }) {
  const abrir = useAbrir();
  return (
    <div className="pantalla">
      <h2>{titulo} <code>{codigo}</code></h2>
      <Aviso tipo="info">{ayuda}</Aviso>
      <div className="operacion">
        <div className="op-form">
          {children}
          {op.res && <Aviso tipo={op.res.tipo}>{op.res.texto}</Aviso>}
        </div>
        <div className="op-reg">
          <div className="acciones"><h3 style={{ margin: 0 }}>Registrado desde esta pantalla</h3><div className="der"><button className="boton" onClick={() => abrir('ZMOV')}>Ver en Movimientos (ZMOV)</button></div></div>
          <Tabla filas={op.registros} clave={(r) => r.id} vacio="Todavía no registraste nada en esta pantalla" alDobleClic={(r) => abrir('ZCON', { codigo: r.contenedor })}
            columnas={[
              { id: 'hora', titulo: 'Hora' },
              { id: 'tipo', titulo: 'Tipo', render: (r) => <Chip v={r.tipo} tipo /> },
              { id: 'contenedor', titulo: 'Contenedor', mono: true },
              { id: 'detalle', titulo: 'Detalle' },
              { id: 'estado', titulo: 'Estado', render: (r) => <Chip v={r.estado} /> },
              { id: 'mensaje', titulo: 'Respuesta' },
            ]} />
        </div>
      </div>
    </div>
  );
}

const Titulo2 = ({ children }: { children: ReactNode }) => <h3 style={{ margin: '6px 0 0' }}>{children}</h3>;
const Fila = ({ children }: { children: ReactNode }) => <div className="acciones" style={{ alignItems: 'flex-end' }}>{children}</div>;
const Lista = ({ v, set, alto = 150 }: { v: string; set: (s: string) => void; alto?: number }) => (
  <textarea className="lista-cod" value={v} onChange={(e) => set(e.target.value.toUpperCase())} style={{ height: alto }} spellCheck={false} />
);

async function infoCont(c: string) {
  const r = await post('/api/contenedores/info', { contenedores: [c.trim().toUpperCase()] });
  return (r as any[])?.[0];
}
const resumen = (i: any) => !i?.encontrado ? '✖ No figura en stock'
  : `${i.articulo ?? ''} · ${i.descripcion ?? ''} — ${i.empresaNombre ?? ''} · ${i.cantidad ?? ''} u. · en ${i.posicion || '—'}${i.disponible === false ? ' · NO DISPONIBLE' : ''}`;

function Sugerencias({ lista, usar }: { lista: any[] | null; usar: (s: any) => void }) {
  if (!lista) return null;
  if (!lista.length) return <div style={{ color: 'var(--tenue)' }}>Sin sugerencias para esta carga.</div>;
  return (
    <div className="sugerencias">
      <span style={{ color: 'var(--tenue)' }}>Sugerencias (clic para usar):</span>
      {lista.map((s, i) => (
        <button key={i} className="boton" onClick={() => usar(s)}>
          <b>{s.legible}</b> nivel {s.nivel} · {s.motivo}{s.libres != null ? ` · ${s.libres} libres` : ''}
        </button>
      ))}
    </div>
  );
}

// ------------------------------------------------------------------ ZTRF Transferir
export function Transferir({ parametros }: PropsTx) {
  const op = useOperacion();
  const [conts, setConts] = useState<string>(parametros?.contenedor ?? '');
  const [origen, setOrigen] = useState(''); const [origenNiv, setOrigenNiv] = useState('');
  const [destino, setDestino] = useState(''); const [nivel, setNivel] = useState('');
  const [multi, setMulti] = useState(false); const [completa, setCompleta] = useState(false);
  const [sug, setSug] = useState<any[] | null>(null);
  const [val, setVal] = useState<any>(null);
  const mostrar = useMensaje();

  const traer = () => op.hacer(async () => {
    if (!origen.trim()) return;
    const r = await get(`/api/posicion/${encodeURIComponent(origen.trim())}${entero(origenNiv) != null ? `?nivel=${entero(origenNiv)}` : ''}`);
    const l: string[] = [];
    for (const n of r?.niveles ?? []) for (const c of n.contenedores ?? []) l.push(typeof c === 'string' ? c : (c.contenedor ?? c.codigo));
    const u = [...new Set(l.filter(Boolean))];
    setConts(u.join('\n')); setCompleta(u.length > 0 && entero(origenNiv) != null);
    mostrar(`${u.length} contenedor(es) en ${origen.trim()}`, 'info');
  });
  const sugerir = () => op.hacer(async () => {
    const cs = codigos(conts);
    if (!cs.length) { op.setRes({ tipo: 'error', texto: 'Cargá al menos un contenedor.' }); return; }
    const r = await post('/api/sugerencia', { contenedores: cs, origen: origen.trim() || null, cuantas: 6 });
    setSug(r?.sugerencias ?? []);
  });
  const validar = async (): Promise<boolean> => {
    const cs = codigos(conts);
    if (!cs.length || !destino.trim()) { op.setRes({ tipo: 'error', texto: 'Faltan contenedores o destino.' }); return false; }
    const v = await post('/api/validar', { contenedores: cs, origen: origen.trim() || null, destino: destino.trim(), nivel: entero(nivel), multi });
    setVal(v);
    const forzable = !v?.ok && !(v?.bloqueos?.length) && (v?.forzables?.length ?? 0) > 0;
    return !!v?.ok || forzable;
  };
  const transferir = () => op.hacer(async () => {
    const cs = codigos(conts);
    if (!(await validar())) { op.setRes({ tipo: 'error', texto: 'El destino no pasó el control.' }); return; }
    if (!confirm(`¿Transferir ${cs.length} contenedor(es) a ${destino.trim()}${entero(nivel) != null ? ` nivel ${entero(nivel)}` : ''}?`)) return;
    const grupo = uuid();
    await op.registrar(cs.map((c) => ({
      ...mov('transferencia', c, grupo), destino: destino.trim(), nivel: entero(nivel), multi,
      ...(origen.trim() ? { origen: origen.trim() } : {}), ...(completa ? { completa: true } : {}), comentario: 'Desde el panel web',
    })), `→ ${destino.trim()} N${nivel.trim()}`);
    setConts(''); setSug(null); setVal(null);
  });

  return (
    <Marco codigo="ZTRF" titulo="Transferir" op={op}
      ayuda="Igual que en la app: cargá los contenedores (o traé los de una posición), pedí sugerencia o escribí el destino, controlalo y transferí. Si el destino es zona de otro cliente pide autorización (ZAUT), salvo que marques multicontenedor.">
      <Titulo2>1 · Qué se mueve</Titulo2>
      <Campo etiqueta="Contenedores (uno por línea, o pegados)"><Lista v={conts} set={setConts} /></Campo>
      <Fila>
        <Campo etiqueta="o traer los de la posición"><input value={origen} onChange={(e) => setOrigen(e.target.value.toUpperCase())} style={{ width: 130 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={origenNiv} onChange={(e) => setOrigenNiv(e.target.value.replace(/\D/g, ''))} style={{ width: 60, minWidth: 60 }} /></Campo>
        <button className="boton" disabled={op.ocupado} onClick={traer}>Traer contenedores</button>
      </Fila>
      <Titulo2>2 · A dónde</Titulo2>
      <Fila>
        <Campo etiqueta="Destino (Ubic.Ctrl o posición)"><input value={destino} onChange={(e) => { setDestino(e.target.value.toUpperCase()); setVal(null); }} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={nivel} onChange={(e) => { setNivel(e.target.value.replace(/\D/g, '')); setVal(null); }} style={{ width: 60, minWidth: 60 }} /></Campo>
        <button className="boton" disabled={op.ocupado} onClick={sugerir}>Sugerir destino</button>
      </Fila>
      <Sugerencias lista={sug} usar={(s) => { setDestino(String(s.ubicCtrl || s.codigo || "")); setNivel(String(s.nivel ?? '')); setVal(null); }} />
      <label className="chk"><input type="checkbox" checked={multi} onChange={(e) => setMulti(e.target.checked)} /> Multicontenedor (distintas empresas a propósito)</label>
      <label className="chk"><input type="checkbox" checked={completa} onChange={(e) => setCompleta(e.target.checked)} /> Posición completa (al terminar mueve lo que haya quedado en el origen)</label>
      <Fila>
        <button className="boton azul" disabled={op.ocupado} onClick={() => op.hacer(async () => { await validar(); })}>Controlar destino</button>
        <button className="boton primario" disabled={op.ocupado} onClick={transferir}>Transferir</button>
      </Fila>
      {val && (
        <div className="validacion">
          {(val.bloqueos ?? []).map((b: string, i: number) => <div key={'b' + i} className="v-error">⛔ {b}</div>)}
          {(val.forzables ?? []).map((b: string, i: number) => <div key={'f' + i} className="v-aviso">⚠ {b} (se puede transferir igual)</div>)}
          {(val.avisos ?? []).map((b: string, i: number) => <div key={'a' + i} className="v-tenue">• {b}</div>)}
          {val.autorizacion && <div className="v-aviso"><b>🔐 Pide autorización: {val.autorizacion}</b></div>}
          {val.ok && <div className="v-ok"><b>✔ Destino válido{val.destino ? `: ${val.destino.legible} nivel ${val.destino.nivel}` : ''}</b></div>}
        </div>
      )}
    </Marco>
  );
}

// ------------------------------------------------------------------ ZGUA Guardar
export function Guardar() {
  const op = useOperacion();
  const [lista, setLista] = useState<any[]>([]);
  const [sel, setSel] = useState<any>(null);
  const [destino, setDestino] = useState(''); const [nivel, setNivel] = useState('');
  const [sug, setSug] = useState<any[] | null>(null);
  const leer = () => op.hacer(async () => {
    const l: any[] = await get('/api/guardado/pendientes');
    const ok = l.filter((x) => x.contenedor); const err = l.filter((x) => x.error).map((x) => x.error);
    setLista(ok); setSel(null);
    op.setRes({ tipo: err.length ? 'error' : 'info', texto: `${ok.length} pendiente(s) de guardar${err.length ? ' · ' + err.join(' · ') : ''}` });
  });
  useEffect(() => { leer(); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const elegir = (f: any) => {
    setSel(f); setSug(null);
    op.hacer(async () => { const r = await post('/api/sugerencia', { contenedores: [f.contenedor], cuantas: 4 }); setSug(r?.sugerencias ?? []); });
  };
  const guardar = () => op.hacer(async () => {
    if (!sel) { op.setRes({ tipo: 'error', texto: 'Elegí un contenedor de la lista.' }); return; }
    if (!destino.trim()) { op.setRes({ tipo: 'error', texto: 'Falta el destino.' }); return; }
    await op.registrar([{ ...mov('guardado', sel.contenedor), destino: destino.trim(), nivel: entero(nivel), comentario: 'Desde el panel web' }], `guardado → ${destino.trim()} N${nivel.trim()}`);
    setDestino(''); setNivel(''); setSug(null);
    setLista((l) => l.filter((x) => x.contenedor !== sel.contenedor)); setSel(null);
  });
  return (
    <Marco codigo="ZGUA" titulo="Guardar recepciones" op={op} ayuda="Contenedores recibidos que todavía no se guardaron. Elegí uno, pedí la sugerencia o escribí el destino y guardalo.">
      <Fila><button className="boton azul" disabled={op.ocupado} onClick={leer}>↻ Leer pendientes del WMS</button></Fila>
      <div style={{ height: 260, display: 'flex' }}>
        <Tabla filas={lista} clave={(f: any) => f.contenedor} seleccion={new Set(sel ? [sel.contenedor] : [])} alSeleccionar={(_, f) => f && elegir(f)} vacio="No hay recepciones pendientes de guardar"
          columnas={[
            { id: 'contenedor', titulo: 'Contenedor', mono: true }, { id: 'articulo', titulo: 'Artículo', mono: true }, { id: 'descripcion', titulo: 'Descripción' },
            { id: 'empresaNombre', titulo: 'Empresa' }, { id: 'cantidad', titulo: 'Cant.', num: true }, { id: 'recepcion', titulo: 'Recepción' },
          ]} />
      </div>
      <Fila>
        <Campo etiqueta={sel ? `Destino para ${sel.contenedor}` : 'Destino'}><input value={destino} onChange={(e) => setDestino(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} style={{ width: 60, minWidth: 60 }} /></Campo>
        <button className="boton primario" disabled={op.ocupado || !sel} onClick={guardar}>Guardar</button>
      </Fila>
      <Sugerencias lista={sug} usar={(s) => { setDestino(String(s.ubicCtrl || s.codigo || "")); setNivel(String(s.nivel ?? '')); }} />
    </Marco>
  );
}

// ------------------------------------------------------------------ ZCAJ Reubicar cajas
export function ReubicarCajas() {
  const op = useOperacion();
  const [origen, setOrigen] = useState(''); const [info, setInfo] = useState('');
  const [destinos, setDestinos] = useState<any[]>([]);
  const [destino, setDestino] = useState(''); const [cant, setCant] = useState('');
  const [posDest, setPosDest] = useState(''); const [nivel, setNivel] = useState('');
  const buscar = () => op.hacer(async () => {
    if (!origen.trim()) return;
    setInfo(resumen(await infoCont(origen)));
    const r = await get(`/api/cajas/destinos/${encodeURIComponent(origen.trim())}`);
    setDestinos(r?.destinos ?? []);
  });
  const reubicar = () => op.hacer(async () => {
    const n = numero(cant);
    if (!origen.trim() || !destino.trim() || !n || n <= 0) { op.setRes({ tipo: 'error', texto: 'Completá origen, destino y cantidad.' }); return; }
    if (!confirm(`¿Pasar ${n} de ${origen.trim()} a ${destino.trim()}?`)) return;
    await op.registrar([{
      ...mov('reubicar_cajas', origen), destino: destino.trim().toUpperCase(), cantidad: n, comentario: 'Desde el panel web',
      ...(posDest.trim() ? { posicion_destino: posDest.trim(), nivel: entero(nivel) } : {}),
    }], `${n} u. → ${destino.trim()}`);
    setCant('');
  });
  return (
    <Marco codigo="ZCAJ" titulo="Reubicar cajas" op={op} ayuda='Pasar cajas (unidades) de un contenedor a otro del mismo artículo y empresa. "Buscar destinos" muestra los otros contenedores del artículo.'>
      <Fila>
        <Campo etiqueta="Contenedor origen"><input value={origen} onChange={(e) => setOrigen(e.target.value.toUpperCase())} onKeyDown={(e) => e.key === 'Enter' && buscar()} style={{ width: 160 }} /></Campo>
        <button className="boton" disabled={op.ocupado} onClick={buscar}>Buscar destinos</button>
      </Fila>
      {info && <div style={{ color: 'var(--tenue)' }}>{info}</div>}
      <div style={{ height: 180, display: 'flex' }}>
        <Tabla filas={destinos} clave={(f: any) => f.contenedor} seleccion={new Set([destino])} alSeleccionar={(_, f: any) => { if (f) { setDestino(f.contenedor); setPosDest(f.posicion ?? ''); } }}
          vacio="Buscá un contenedor para ver a cuáles se le pueden pasar cajas"
          columnas={[{ id: 'contenedor', titulo: 'Contenedor', mono: true }, { id: 'posicion', titulo: 'Posición', mono: true }, { id: 'cantidad', titulo: 'Cant.', num: true },
            { id: 'mismaPosicion', titulo: 'Misma posición', render: (f: any) => (f.mismaPosicion ? '✔' : '') }]} />
      </div>
      <Fila>
        <Campo etiqueta="Contenedor destino"><input value={destino} onChange={(e) => setDestino(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Cantidad a pasar"><input value={cant} onChange={(e) => setCant(e.target.value)} style={{ width: 90, minWidth: 90 }} /></Campo>
      </Fila>
      <Fila>
        <Campo etiqueta="Dónde está el destino (opcional)"><input value={posDest} onChange={(e) => setPosDest(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} style={{ width: 60, minWidth: 60 }} /></Campo>
        <button className="boton primario" disabled={op.ocupado} onClick={reubicar}>Reubicar</button>
      </Fila>
    </Marco>
  );
}

// ------------------------------------------------------------------ ZAJU Ajustar
export function Ajustar({ parametros }: PropsTx) {
  const op = useOperacion();
  const [cont, setCont] = useState<string>(parametros?.contenedor ?? ''); const [info, setInfo] = useState('');
  const [signo, setSigno] = useState('restar'); const [cant, setCant] = useState(''); const [motivo, setMotivo] = useState('');
  const ajustar = () => op.hacer(async () => {
    const n = numero(cant);
    if (!cont.trim() || !n || n <= 0 || !motivo.trim()) { op.setRes({ tipo: 'error', texto: 'Completá contenedor, cantidad y motivo.' }); return; }
    const sumar = signo === 'sumar';
    if (!confirm(`¿${sumar ? 'Sumar' : 'Restar'} ${n} al contenedor ${cont.trim()}?`)) return;
    await op.registrar([{ ...mov('ajuste', cont), cantidad: n, sumar, motivo: motivo.trim(), comentario: 'Desde el panel web' }], `${sumar ? '+' : '−'}${n} · ${motivo.trim()}`);
    setCant(''); setInfo('');
  });
  return (
    <Marco codigo="ZAJU" titulo="Ajustar contenedor" op={op} ayuda="Sumar o restar unidades de un contenedor. Queda registrado con el motivo.">
      <Fila>
        <Campo etiqueta="Contenedor"><input value={cont} onChange={(e) => setCont(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <button className="boton" disabled={op.ocupado} onClick={() => op.hacer(async () => setInfo(resumen(await infoCont(cont))))}>Ver</button>
      </Fila>
      {info && <div style={{ color: 'var(--tenue)' }}>{info}</div>}
      <Fila>
        <Campo etiqueta="Operación"><select value={signo} onChange={(e) => setSigno(e.target.value)}><option value="restar">Restar</option><option value="sumar">Sumar</option></select></Campo>
        <Campo etiqueta="Cantidad"><input value={cant} onChange={(e) => setCant(e.target.value)} style={{ width: 90, minWidth: 90 }} /></Campo>
      </Fila>
      <Campo etiqueta="Motivo"><input value={motivo} onChange={(e) => setMotivo(e.target.value)} style={{ width: 340 }} /></Campo>
      <Fila><button className="boton primario" disabled={op.ocupado} onClick={ajustar}>Ajustar</button></Fila>
    </Marco>
  );
}

// ------------------------------------------------------------------ ZCES Cambiar estado
const MOTIVOS = ['ROTURA', 'NO CONFORME', 'INCOMPLETO', 'CUARENTENA', 'OBSOLETO', 'SCRAP', 'DEPOSITO EXTERNO'];
export function CambiarEstado({ parametros }: PropsTx) {
  const op = useOperacion();
  const [conts, setConts] = useState<string>(parametros?.contenedor ?? '');
  const [disp, setDisp] = useState('no'); const [motivo, setMotivo] = useState(MOTIVOS[0]); const [coment, setComent] = useState('');
  const [fotos, setFotos] = useState<File[]>([]);
  const cambiar = () => op.hacer(async () => {
    const cs = codigos(conts);
    if (!cs.length) { op.setRes({ tipo: 'error', texto: 'Cargá al menos un contenedor.' }); return; }
    const d = disp === 'si';
    if (!confirm(`¿Marcar ${cs.length} contenedor(es) como ${d ? 'DISPONIBLE' : 'NO DISPONIBLE – ' + motivo}?`)) return;
    const ids: string[] = [];
    for (const f of fotos) { const fd = new FormData(); fd.append('foto', f, f.name); const r = await subir('/api/fotos', fd); if (r?.id) ids.push(r.id); }
    const grupo = uuid();
    await op.registrar(cs.map((c) => ({
      ...mov('cambio_estado', c, grupo), disponible: d, ...(d ? {} : { motivo }), comentario: coment.trim() || 'Desde el panel web', ...(ids.length ? { fotos: ids } : {}),
    })), d ? 'DISPONIBLE' : 'NO DISPONIBLE – ' + motivo);
    setConts(''); setFotos([]);
  });
  return (
    <Marco codigo="ZCES" titulo="Cambiar estado" op={op} ayuda="Marcar contenedores como no disponibles (rotura, no conforme, cuarentena…) o volverlos a disponible. Para rotura y no conforme la foto es obligatoria.">
      <Campo etiqueta="Contenedores"><Lista v={conts} set={setConts} alto={110} /></Campo>
      <Fila>
        <Campo etiqueta="Estado"><select value={disp} onChange={(e) => setDisp(e.target.value)}><option value="no">No disponible</option><option value="si">Disponible</option></select></Campo>
        <Campo etiqueta="Motivo"><select value={motivo} disabled={disp === 'si'} onChange={(e) => setMotivo(e.target.value)}>{MOTIVOS.map((m) => <option key={m}>{m}</option>)}</select></Campo>
      </Fila>
      <Campo etiqueta="Comentario"><input value={coment} onChange={(e) => setComent(e.target.value)} style={{ width: 340 }} /></Campo>
      <Fila>
        <label className="boton">📷 Agregar fotos…<input type="file" accept="image/*" multiple hidden onChange={(e) => { setFotos((f) => [...f, ...Array.from(e.target.files ?? [])]); e.target.value = ''; }} /></label>
        {fotos.length > 0 && <button className="boton" onClick={() => setFotos([])}>Quitar fotos</button>}
        <span style={{ color: 'var(--tenue)' }}>{fotos.length ? `${fotos.length} foto(s)` : 'Sin fotos'}</span>
      </Fila>
      <Fila><button className="boton primario" disabled={op.ocupado} onClick={cambiar}>Cambiar estado</button></Fila>
    </Marco>
  );
}

// ------------------------------------------------------------------ ZALT Crear contenedor
export function CrearContenedor() {
  const op = useOperacion();
  const [art, setArt] = useState(''); const [cant, setCant] = useState(''); const [pos, setPos] = useState(''); const [nivel, setNivel] = useState('');
  const [pref, setPref] = useState('PP'); const [cont, setCont] = useState('');
  const crear = () => op.hacer(async () => {
    const n = numero(cant);
    if (!art.trim() || !n || n <= 0 || !pos.trim()) { op.setRes({ tipo: 'error', texto: 'Completá artículo, cantidad y posición.' }); return; }
    const r = await post('/api/contenedores/crear', { articulo: art.trim(), cantidad: n, posicion: pos.trim(), nivel: entero(nivel), prefijo: pref, contenedor: cont.trim() || null });
    op.setRes({ tipo: r?.ok ? 'ok' : 'error', texto: (r?.ok ? '✔ ' : '✖ ') + (r?.mensaje ?? '') });
    if (r?.contenedor) {
      setCont(r.ok ? '' : r.contenedor);
      if (r.ok && confirm(`Contenedor ${r.contenedor} creado. ¿Abrir la etiqueta para imprimirla?`))
        window.open(`/api/etiquetas/${encodeURIComponent(r.contenedor)}?token=${encodeURIComponent(sesion().token ?? '')}`, '_blank');
    }
  });
  return (
    <Marco codigo="ZALT" titulo="Crear contenedor" op={op} ayuda="El LES da el número de contenedor y SONA lo carga en el WMS (carga inicial) en la posición y nivel, con la cantidad. Si ya tenés el número, ponelo para reintentar solo la carga.">
      <Fila>
        <Campo etiqueta="Artículo"><input value={art} onChange={(e) => setArt(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Cantidad"><input value={cant} onChange={(e) => setCant(e.target.value)} style={{ width: 90, minWidth: 90 }} /></Campo>
      </Fila>
      <Fila>
        <Campo etiqueta="Posición"><input value={pos} onChange={(e) => setPos(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} style={{ width: 60, minWidth: 60 }} /></Campo>
        <Campo etiqueta="Tipo"><select value={pref} onChange={(e) => setPref(e.target.value)}><option value="PP">PP</option><option value="PC">PC</option><option value="BULTO">Bulto</option></select></Campo>
      </Fila>
      <Campo etiqueta="Contenedor (solo para reintentar)"><input value={cont} onChange={(e) => setCont(e.target.value.toUpperCase())} style={{ width: 160 }} /></Campo>
      <Fila><button className="boton primario" disabled={op.ocupado} onClick={crear}>Crear contenedor</button></Fila>
    </Marco>
  );
}

// ------------------------------------------------------------------ ZNN Enviar lote a NN
let ultimoNn = { destino: '5422', nivel: '1' };
export function EnviarNn() {
  const op = useOperacion();
  const [lista, setLista] = useState('');
  const [destino, setDestino] = useState(ultimoNn.destino); const [nivel, setNivel] = useState(ultimoNn.nivel);
  const [filas, setFilas] = useState<any[]>([]);
  const controlar = () => op.hacer(async () => {
    const cs = codigos(lista);
    if (!cs.length) { op.setRes({ tipo: 'error', texto: 'Pegá al menos un contenedor.' }); return; }
    const r: any[] = await post('/api/contenedores/info', { contenedores: cs });
    setFilas(r ?? []);
    const ok = (r ?? []).filter((f) => f.encontrado).length;
    op.setRes({ tipo: 'info', texto: `${r.length} contenedores · ${ok} en stock · ${r.length - ok} no figuran (no se transfieren)` });
  });
  const enStock = filas.filter((f) => f.encontrado);
  const transferir = () => op.hacer(async () => {
    const n = entero(nivel);
    if (!destino.trim() || n == null) { op.setRes({ tipo: 'error', texto: 'Falta el destino o el nivel.' }); return; }
    if (!enStock.length || !confirm(`¿Transferir ${enStock.length} contenedores a ${destino.trim()} nivel ${n}?\nSe cargan en el WMS uno por uno.`)) return;
    ultimoNn = { destino: destino.trim(), nivel: String(n) };
    const grupo = uuid();
    await op.registrar(enStock.map((f) => ({ ...mov('transferencia', f.contenedor, grupo), destino: destino.trim(), nivel: n, multi: true, comentario: 'Lote a NN desde el panel web' })), `NN → ${destino.trim()} N${n}`);
    setFilas([]); setLista('');
  });
  return (
    <Marco codigo="ZNN" titulo="Enviar a NN (lote)" op={op} ayuda="Pegá los contenedores (uno por línea, o separados por coma, espacio o tab, como los tenés en el Bloc de notas). Primero se controla la lista y después se mandan todas las transferencias juntas al WMS. No pide autorización de zona.">
      <Campo etiqueta="Contenedores (Ctrl+V)"><Lista v={lista} set={(s) => { setLista(s); setFilas([]); }} alto={200} /></Campo>
      <Fila>
        <Campo etiqueta="Destino (Ubic.Ctrl o posición)"><input value={destino} onChange={(e) => setDestino(e.target.value.toUpperCase())} style={{ width: 140 }} /></Campo>
        <Campo etiqueta="Nivel"><input value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} style={{ width: 60, minWidth: 60 }} /></Campo>
      </Fila>
      <Fila>
        <button className="boton azul" disabled={op.ocupado} onClick={controlar}>1 · Controlar lista</button>
        <button className="boton primario" disabled={op.ocupado || !enStock.length} onClick={transferir}>2 · Transferir {enStock.length || ''} a NN</button>
      </Fila>
      {filas.length > 0 && (
        <div style={{ height: 220, display: 'flex' }}>
          <Tabla filas={filas} clave={(f: any) => f.contenedor}
            columnas={[{ id: 'contenedor', titulo: 'Contenedor', mono: true }, { id: 'encontrado', titulo: 'En stock', render: (f: any) => (f.encontrado ? '✔' : '✖ no figura') },
              { id: 'articulo', titulo: 'Artículo', mono: true }, { id: 'descripcion', titulo: 'Descripción' }, { id: 'cantidad', titulo: 'Cant.', num: true }, { id: 'posicion', titulo: 'Posición actual', mono: true }]} />
        </div>
      )}
    </Marco>
  );
}

// ------------------------------------------------------------------ ZELI Eliminar
export function Eliminar({ parametros }: PropsTx) {
  const op = useOperacion();
  const [conts, setConts] = useState<string>(parametros?.contenedor ?? ''); const [motivo, setMotivo] = useState('');
  const eliminar = () => op.hacer(async () => {
    const cs = codigos(conts);
    if (!cs.length || !motivo.trim()) { op.setRes({ tipo: 'error', texto: 'Cargá los contenedores y el motivo.' }); return; }
    if (!confirm(`¿ELIMINAR ${cs.length} contenedor(es)? Se ajustan a 0 en el WMS.\n${cs.slice(0, 10).join(', ')}${cs.length > 10 ? '…' : ''}`)) return;
    const grupo = uuid();
    await op.registrar(cs.map((c) => ({ ...mov('ajuste', c, grupo), motivo: motivo.trim(), comentario: 'Eliminado desde el panel web' })), 'eliminar · ' + motivo.trim());
    setConts('');
  });
  return (
    <Marco codigo="ZELI" titulo="Eliminar contenedor" op={op} ayuda="Ajusta el contenedor a 0 en el WMS (resta todo lo que tiene). Queda registrado con el motivo.">
      <Campo etiqueta="Contenedores"><Lista v={conts} set={setConts} alto={110} /></Campo>
      <Campo etiqueta="Motivo"><input value={motivo} onChange={(e) => setMotivo(e.target.value)} style={{ width: 340 }} /></Campo>
      <Fila><button className="boton peligro" disabled={op.ocupado} onClick={eliminar}>Eliminar</button></Fila>
    </Marco>
  );
}
