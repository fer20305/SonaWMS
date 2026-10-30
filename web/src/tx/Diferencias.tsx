import { useState } from 'react';
import { get, post, fechaHora } from '../api';
import { Aviso, Campo, Chip, Tabla, useDatos, useMensaje } from '../comp';
import type { PropsTx } from '../App';

export default function Diferencias({ parametros }: PropsTx) {
  const [tarea, setTarea] = useState<string>(parametros?.tarea ? String(parametros.tarea) : '');
  const [revision, setRevision] = useState('pendiente');
  const tareas = useDatos(() => get('/api/conteo/tareas'), []);
  const { datos, error, recargar } = useDatos(() => get(`/api/conteo/diferencias?${new URLSearchParams({ ...(tarea ? { tarea } : {}), ...(revision ? { revision } : {}) })}`), [tarea, revision], ['conteo']);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const mostrar = useMensaje();
  const marcar = async (r: string) => {
    for (const id of sel) await post(`/api/conteo/lineas/${id}/revision`, { revision: r });
    mostrar(`${sel.size} líneas marcadas como ${r}`, 'ok'); setSel(new Set()); recargar();
  };
  return (
    <div className="pantalla">
      <h2>Diferencias de conteo <code>ZDIF</code></h2>
      <div className="filtros">
        <Campo etiqueta="Tarea">
          <select value={tarea} onChange={(e) => setTarea(e.target.value)}><option value="">Todas</option>{(tareas.datos ?? []).map((t: any) => <option key={t.id} value={t.id}>{t.id} · {t.nombre}</option>)}</select>
        </Campo>
        <Campo etiqueta="Revisión">
          <select value={revision} onChange={(e) => setRevision(e.target.value)}>
            <option value="">Todas</option><option value="pendiente">Pendientes</option><option value="aprobada">Aprobadas</option><option value="recontar">A recontar</option><option value="rechazada">Rechazadas</option>
          </select>
        </Campo>
        <div className="acciones" style={{ marginLeft: 'auto' }}>
          <span>{sel.size} seleccionadas</span>
          <button className="boton ok" disabled={!sel.size} onClick={() => marcar('aprobada')}>Aprobar</button>
          <button className="boton" disabled={!sel.size} onClick={() => marcar('recontar')}>Pedir recuento</button>
          <button className="boton peligro" disabled={!sel.size} onClick={() => marcar('rechazada')}>Rechazar</button>
        </div>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tabla filas={datos ?? []} clave={(l: any) => String(l.id)} seleccion={sel} multiple alSeleccionar={setSel} vacio="Sin diferencias 👍"
        columnas={[
          { id: 'hora', titulo: 'Fecha', render: (l: any) => fechaHora(l.hora) },
          { id: 'tarea', titulo: 'Tarea' },
          { id: 'posicion', titulo: 'Posición', mono: true },
          { id: 'contenedor', titulo: 'Contenedor', mono: true },
          { id: 'articulo', titulo: 'Artículo', mono: true },
          { id: 'descripcion', titulo: 'Descripción' },
          { id: 'cantidad_wms', titulo: 'Sistema', num: true, valor: (l: any) => (l.cantidad_wms != null ? Number(l.cantidad_wms) : null) },
          { id: 'cantidad_contada', titulo: 'Contado', num: true, valor: (l: any) => (l.cantidad_contada != null ? Number(l.cantidad_contada) : null) },
          { id: 'dif', titulo: 'Dif.', num: true, valor: (l: any) => (l.cantidad_wms != null && l.cantidad_contada != null ? Number(l.cantidad_contada) - Number(l.cantidad_wms) : null) },
          { id: 'novedad', titulo: 'Novedad', render: (l: any) => <Chip v={l.novedad} /> },
          { id: 'usuario', titulo: 'Contó' },
          { id: 'revision', titulo: 'Revisión', render: (l: any) => <Chip v={l.revision} /> },
        ]} />
    </div>
  );
}
