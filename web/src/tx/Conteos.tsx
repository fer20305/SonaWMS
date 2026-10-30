import { useEffect, useState } from 'react';
import { get, post, fechaHora } from '../api';
import { Aviso, Campo, Chip, Modal, Tabla, useAbrir, useDatos, useMensaje } from '../comp';

export default function Conteos() {
  const { datos, error, recargar } = useDatos(() => get('/api/conteo/tareas'), [], ['conteo']);
  const [nueva, setNueva] = useState(false);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const actual = (datos ?? []).find((t: any) => sel.has(String(t.id)));
  const detalle = useDatos(() => (actual ? get(`/api/conteo/tareas/${actual.id}`) : Promise.resolve(null)), [actual?.id], ['conteo']);
  const mostrar = useMensaje();
  const abrir = useAbrir();
  return (
    <div className="pantalla">
      <h2>Tareas de conteo <code>ZCNT</code></h2>
      <Aviso tipo="info">Conteo ciego: el contador ve contenedor, artículo, descripción y foto, pero no la cantidad del sistema. Las diferencias se revisan en ZDIF.</Aviso>
      <div className="acciones">
        <button className="boton primario" onClick={() => setNueva(true)}>＋ Nueva tarea</button>
        <div className="der"><button className="boton" onClick={recargar}>↻</button></div>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className={`con-detalle ${actual ? '' : 'cerrado'}`}>
        <Tabla filas={datos ?? []} clave={(t: any) => String(t.id)} seleccion={sel} alSeleccionar={setSel} vacio="No hay tareas de conteo"
          columnas={[
            { id: 'id', titulo: 'N°', num: true },
            { id: 'nombre', titulo: 'Tarea', render: (t: any) => <b>{t.nombre}</b> },
            { id: 'modo', titulo: 'Modo', render: (t: any) => MODOS[t.modo] ?? t.modo ?? '' },
            { id: 'articulo', titulo: 'Artículo', mono: true },
            { id: 'asignado_a', titulo: 'Asignada a', render: (t: any) => t.asignado_a ?? 'Cualquiera' },
            { id: 'avance', titulo: 'Avance', valor: (t: any) => `${t.contadas}/${t.total}` },
            { id: 'diferencias', titulo: 'Diferencias', num: true, valor: (t: any) => Number(t.diferencias) },
            { id: 'estado', titulo: 'Estado', render: (t: any) => <Chip v={t.estado} /> },
            { id: 'creado', titulo: 'Creada', render: (t: any) => `${fechaHora(t.creado)} · ${t.creado_por}` },
          ]} />
        {actual && (
          <div className="detalle">
            <div className="acciones"><h3 style={{ margin: 0 }}>{actual.nombre}</h3><div className="der"><button className="boton" onClick={() => setSel(new Set())}>✕</button></div></div>
            <div className="acciones" style={{ margin: '8px 0' }}>
              <button className="boton" onClick={() => abrir('ZDIF', { tarea: actual.id })}>Ver diferencias</button>
              {actual.estado !== 'cerrada' && <button className="boton peligro" onClick={async () => { await post(`/api/conteo/tareas/${actual.id}/cerrar`); mostrar('Tarea cerrada', 'ok'); recargar(); }}>Cerrar tarea</button>}
            </div>
            <table className="alv">
              <thead><tr><th>Posición</th><th>Contada</th><th>Líneas</th><th>Dif.</th></tr></thead>
              <tbody>
                {(detalle.datos?.detalle ?? []).map((p: any) => (
                  <tr key={p.posicion}><td className="mono">{p.posicion}</td><td>{p.contada ? '✔' : ''}</td><td className="num">{p.lineas || ''}</td>
                    <td className="num" style={{ color: p.diferencias ? 'var(--error)' : undefined }}>{p.diferencias || ''}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {nueva && <NuevaTarea cerrar={() => setNueva(false)} listo={() => { setNueva(false); recargar(); mostrar('Tarea creada y enviada a los contadores', 'ok'); }} />}
    </div>
  );
}

const MODOS: Record<string, string> = { ciego: 'Ciego', confirmacion: 'Confirmación', articulo: 'Por artículo' };

function NuevaTarea({ cerrar, listo }: { cerrar: () => void; listo: () => void }) {
  const usuarios = useDatos(() => get('/api/usuarios'), []);
  const zonas = useDatos(() => get('/api/zonas'), []);
  const [v, setV] = useState({ nombre: '', asignado_a: '', modo: 'articulo', conteo: 'ciego', articulo: '', cara: '', desde: '1', hasta: '10', niveles: '', lista: '' });
  const [error, setError] = useState<string | null>(null);
  const [creando, setCreando] = useState(false);
  const [sug, setSug] = useState<any[]>([]);
  // buscador de artículos mientras se escribe (código o descripción), como en Consultar
  useEffect(() => {
    if (v.modo !== 'articulo' || v.articulo.trim().length < 2) { setSug([]); return; }
    const t = window.setTimeout(() => get(`/api/articulos/sugerir?q=${encodeURIComponent(v.articulo.trim())}`).then((r) => setSug(r?.items ?? [])).catch(() => setSug([])), 350);
    return () => clearTimeout(t);
  }, [v.articulo, v.modo]);
  const crear = async () => {
    setCreando(true); setError(null);
    try {
      await post('/api/conteo/tareas', {
        nombre: v.nombre || (v.modo === 'articulo' ? `Artículo ${v.articulo.trim()}` : ''), asignado_a: v.asignado_a || null,
        ...(v.modo === 'articulo' ? { modo: 'articulo', articulo: v.articulo.trim() } : { modo: v.conteo }),
        ...(v.modo === 'articulo' ? {} : v.modo === 'rango'
          ? { rango: { cara: v.cara, desde: Number(v.desde), hasta: Number(v.hasta), niveles: v.niveles ? v.niveles.split(/[ ,;]+/).filter(Boolean).map(Number) : undefined } }
          : { posiciones: v.lista.split(/[\s,;]+/).filter(Boolean) }),
      });
      listo();
    } catch (e) { setError((e as Error).message); } finally { setCreando(false); }
  };
  const puedeCrear = v.modo === 'articulo' ? v.articulo.trim().length > 0 : !!v.nombre;
  return (
    <Modal titulo="Nueva tarea de conteo" cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" disabled={!puedeCrear || creando} onClick={crear}>{creando ? 'Buscando posiciones…' : 'Crear'}</button></>}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, minWidth: 480 }}>
        <Campo etiqueta="Nombre"><input value={v.nombre} onChange={(e) => setV({ ...v, nombre: e.target.value })} placeholder={v.modo === 'articulo' ? 'Artículo … (automático)' : 'Auditoría Notro Bazar pasillo DD'} /></Campo>
        <Campo etiqueta="Asignar a">
          <select value={v.asignado_a} onChange={(e) => setV({ ...v, asignado_a: e.target.value })}>
            <option value="">Cualquier contador</option>
            {(usuarios.datos ?? []).filter((u: any) => u.activo).map((u: any) => <option key={u.usuario} value={u.usuario}>{u.nombre || u.usuario} ({u.rol})</option>)}
          </select>
        </Campo>
      </div>
      <div className="acciones">
        <label><input type="radio" checked={v.modo === 'articulo'} onChange={() => setV({ ...v, modo: 'articulo' })} /> Por artículo</label>
        <label><input type="radio" checked={v.modo === 'rango'} onChange={() => setV({ ...v, modo: 'rango' })} /> Por rango de pasillo</label>
        <label><input type="radio" checked={v.modo === 'lista'} onChange={() => setV({ ...v, modo: 'lista' })} /> Lista de posiciones</label>
      </div>
      {v.modo !== 'articulo' && (
        <Campo etiqueta="Modo de conteo">
          <select value={v.conteo} onChange={(e) => setV({ ...v, conteo: e.target.value })}>
            <option value="ciego">Ciego (no ve cantidades)</option><option value="confirmacion">Confirmación (ve lo del sistema y confirma)</option>
          </select>
        </Campo>
      )}
      {v.modo === 'articulo' ? (
        <div>
          <Campo etiqueta="Artículo (código o parte de la descripción)">
            <input autoFocus value={v.articulo} onChange={(e) => setV({ ...v, articulo: e.target.value.toUpperCase() })} placeholder="Ej. 80123456 o SHAMPOO" />
          </Campo>
          {sug.length > 0 && !sug.some((s) => s.articulo === v.articulo.trim()) && (
            <div className="sugerencias-art">
              {sug.slice(0, 8).map((s) => (
                <div key={s.articulo + s.empresa} className="op" onClick={() => setV({ ...v, articulo: s.articulo })}>
                  <b className="mono">{s.articulo}</b> {s.descripcion} <span style={{ color: 'var(--tenue)' }}>{s.empresaNombre}{s.contenedores != null ? ` · ${s.contenedores} cont.` : ''}</span>
                </div>
              ))}
            </div>
          )}
          <div style={{ color: 'var(--tenue)', marginTop: 6 }}>La tarea incluye todas las posiciones donde figura el artículo y el contador cuenta solo los contenedores de ese artículo.</div>
        </div>
      ) : v.modo === 'rango' ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 10 }}>
          <Campo etiqueta="Cara"><select value={v.cara} onChange={(e) => setV({ ...v, cara: e.target.value })}><option value="">—</option>{(zonas.datos ?? []).map((z: any) => <option key={z.cara}>{z.cara}</option>)}</select></Campo>
          <Campo etiqueta="Columna desde"><input value={v.desde} onChange={(e) => setV({ ...v, desde: e.target.value.replace(/\D/g, '') })} /></Campo>
          <Campo etiqueta="Columna hasta"><input value={v.hasta} onChange={(e) => setV({ ...v, hasta: e.target.value.replace(/\D/g, '') })} /></Campo>
          <Campo etiqueta="Niveles (vacío = todos)"><input value={v.niveles} onChange={(e) => setV({ ...v, niveles: e.target.value })} placeholder="1,2,3" /></Campo>
        </div>
      ) : (
        <Campo etiqueta="Posiciones (FI1502, una por línea o separadas por coma)"><textarea rows={6} value={v.lista} onChange={(e) => setV({ ...v, lista: e.target.value.toUpperCase() })} /></Campo>
      )}
      {error && <Aviso tipo="error">{error}</Aviso>}
    </Modal>
  );
}
