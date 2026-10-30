import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { alEvento } from './api';

// ------------------------------------------------------------------ mensajes a la barra de estado
type Msg = { texto: string; tipo: 'ok' | 'error' | 'info' } | null;
export const CtxMensaje = createContext<(t: string, tipo?: 'ok' | 'error' | 'info') => void>(() => undefined);
export const useMensaje = () => useContext(CtxMensaje);
export function useMensajeEstado() {
  const [msg, setMsg] = useState<Msg>(null);
  const t = useRef<number>();
  const mostrar = useCallback((texto: string, tipo: 'ok' | 'error' | 'info' = 'info') => {
    setMsg({ texto, tipo });
    clearTimeout(t.current);
    t.current = window.setTimeout(() => setMsg(null), 8000);
  }, []);
  return [msg, mostrar] as const;
}

// ------------------------------------------------------------------ abrir transacciones desde cualquier pantalla
export const CtxAbrir = createContext<(codigo: string, parametros?: Record<string, any>) => void>(() => undefined);
export const useAbrir = () => useContext(CtxAbrir);

// ------------------------------------------------------------------ carga de datos
export function useDatos<T>(f: () => Promise<T>, deps: unknown[] = [], eventos?: string[]) {
  const [datos, setDatos] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const fr = useRef(f);
  fr.current = f;
  const recargar = useCallback(async () => {
    setCargando(true);
    try { setDatos(await fr.current()); setError(null); } catch (e) { setError((e as Error).message); } finally { setCargando(false); }
  }, []);
  useEffect(() => { recargar(); }, deps); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!eventos?.length) return;
    let t: number | undefined;
    return alEvento((e) => { if (eventos.includes(e.tipo)) { clearTimeout(t); t = window.setTimeout(recargar, 400); } });
  }, [recargar, eventos?.join()]); // eslint-disable-line react-hooks/exhaustive-deps
  return { datos, error, cargando, recargar, setDatos };
}

// ------------------------------------------------------------------ piezas
const ETIQUETAS: Record<string, string> = {
  pendiente_wms: 'Pendiente WMS', aplicado: 'Aplicado', error: 'Error', esperando_autorizacion: 'Esperando autorización',
  rechazado: 'Rechazado', anulado: 'Anulado', registrado: 'Registrado', pendiente: 'Pendiente', aprobada: 'Aprobada',
  rechazada: 'Rechazada', cancelada: 'Cancelada', abierta: 'Abierta', en_curso: 'En curso', cerrada: 'Cerrada',
  transferencia: 'Transferencia', guardado: 'Guardado', cambio_estado: 'Cambio de estado', conteo: 'Conteo', anulacion: 'Anulación',
  ok: 'OK', falta: 'Falta', sobra: 'Sobra', ajeno: 'Ajeno', diferencia: 'Diferencia', recontar: 'Recontar',
};
export const etiqueta = (s?: string | null) => (s ? ETIQUETAS[s] ?? s : '');
export const Chip = ({ v, tipo }: { v?: string | null; tipo?: boolean }) => (v ? <span className={`chip ${tipo ? 'tipo' : v}`}>{etiqueta(v)}</span> : null);

export function Campo({ etiqueta: e, children, ancho }: { etiqueta: string; children: ReactNode; ancho?: number }) {
  return <div className="campo" style={ancho ? { width: ancho } : undefined}><label>{e}</label>{children}</div>;
}

export function Aviso({ tipo = 'info', children }: { tipo?: 'error' | 'ok' | 'info' | 'amarillo'; children: ReactNode }) {
  return <div className={`aviso ${tipo}`}>{children}</div>;
}

export function Modal({ titulo, children, pie, cerrar }: { titulo: string; children: ReactNode; pie?: ReactNode; cerrar: () => void }) {
  useEffect(() => {
    const k = (e: KeyboardEvent) => e.key === 'Escape' && cerrar();
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, [cerrar]);
  return (
    <div className="modal-fondo" onMouseDown={(e) => e.target === e.currentTarget && cerrar()}>
      <div className="modal">
        <header>{titulo}</header>
        <div className="cuerpo-modal">{children}</div>
        {pie && <footer>{pie}</footer>}
      </div>
    </div>
  );
}

export function Fotos({ urls }: { urls: string[] }) {
  const [grande, setGrande] = useState<string | null>(null);
  if (!urls.length) return null;
  return (
    <>
      <div className="fotos">{urls.map((u) => <img key={u} src={u} onClick={() => setGrande(u)} alt="" loading="lazy" />)}</div>
      {grande && <div className="visor" onClick={() => setGrande(null)}><img src={grande} alt="" /></div>}
    </>
  );
}

// ------------------------------------------------------------------ tabla tipo ALV (ordenable, selección, totales)
export interface Columna<T> {
  id: string; titulo: string; valor?: (f: T) => any; render?: (f: T) => ReactNode; num?: boolean; mono?: boolean; ancho?: number;
}
export function Tabla<T>({ filas, columnas, clave, seleccion, alSeleccionar, alDobleClic, multiple, vacio = 'Sin datos' }: {
  filas: T[]; columnas: Columna<T>[]; clave: (f: T) => string; seleccion?: Set<string>; alSeleccionar?: (s: Set<string>, ultima?: T) => void;
  alDobleClic?: (f: T) => void; multiple?: boolean; vacio?: string;
}) {
  const [orden, setOrden] = useState<{ id: string; asc: boolean } | null>(null);
  const ultimoIdx = useRef<number>(-1);
  const ordenadas = useMemo(() => {
    if (!orden) return filas;
    const col = columnas.find((c) => c.id === orden.id);
    const v = (f: T) => (col?.valor ? col.valor(f) : (f as any)[orden.id]);
    return [...filas].sort((a, b) => {
      const x = v(a), y = v(b);
      const r = x == null ? 1 : y == null ? -1 : typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'es', { numeric: true });
      return orden.asc ? r : -r;
    });
  }, [filas, orden, columnas]);
  const clic = (f: T, i: number, e: React.MouseEvent) => {
    if (!alSeleccionar) return;
    const k = clave(f);
    let s = new Set(multiple ? seleccion : []);
    if (multiple && e.shiftKey && ultimoIdx.current >= 0) {
      const [a, b] = [Math.min(ultimoIdx.current, i), Math.max(ultimoIdx.current, i)];
      for (let j = a; j <= b; j++) s.add(clave(ordenadas[j]));
    } else if (multiple && (e.ctrlKey || e.metaKey)) {
      s.has(k) ? s.delete(k) : s.add(k);
    } else {
      s = new Set([k]);
    }
    ultimoIdx.current = i;
    alSeleccionar(s, f);
  };
  return (
    <div className="tabla-caja">
      <table className="alv">
        <thead>
          <tr>{columnas.map((c) => (
            <th key={c.id} style={c.ancho ? { width: c.ancho } : undefined} onClick={() => setOrden((o) => (o?.id === c.id ? { id: c.id, asc: !o.asc } : { id: c.id, asc: true }))}>
              {c.titulo}{orden?.id === c.id ? (orden.asc ? ' ▲' : ' ▼') : ''}
            </th>
          ))}</tr>
        </thead>
        <tbody>
          {ordenadas.map((f, i) => (
            <tr key={clave(f)} className={seleccion?.has(clave(f)) ? 'sel' : ''} onClick={(e) => clic(f, i, e)} onDoubleClick={() => alDobleClic?.(f)}>
              {columnas.map((c) => (
                <td key={c.id} className={`${c.num ? 'num' : ''} ${c.mono ? 'mono' : ''}`}>{c.render ? c.render(f) : String((c.valor ? c.valor(f) : (f as any)[c.id]) ?? '')}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {!filas.length && <div className="vacio">{vacio}</div>}
    </div>
  );
}
