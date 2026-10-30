import { useMemo, useState } from 'react';
import { get, post, descargar, fechaHora, hoyISO, urlFoto } from '../api';
import { Aviso, Campo, Chip, Fotos, Modal, Tabla, useDatos, useMensaje, etiqueta } from '../comp';
import type { PropsTx } from '../App';

const TIPOS = ['transferencia', 'guardado', 'cambio_estado', 'anulacion'];
const ESTADOS = ['pendiente_wms', 'aplicado', 'error', 'esperando_autorizacion', 'rechazado', 'anulado'];

export default function Movimientos({ parametros }: PropsTx) {
  const inicial = {
    usuario: parametros?.usuario ?? '', desde: parametros?.desde ?? (parametros?.estado ? '' : hoyISO()), hasta: parametros?.hasta ?? '',
    tipo: parametros?.tipo ?? '', estado: parametros?.estado ?? '', empresa: '', texto: parametros?.texto ?? '',
  };
  const [f, setF] = useState(inicial);
  const [aplicado, setAplicado] = useState(inicial);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [anular, setAnular] = useState<any>(null);
  const mostrar = useMensaje();
  const qs = useMemo(() => new URLSearchParams(Object.entries(aplicado).filter(([, v]) => v)).toString(), [aplicado]);
  const { datos, error, cargando, recargar } = useDatos(() => get(`/api/movimientos?${qs}&limite=2000`), [qs], ['movimiento', 'autorizacion']);
  const usuarios = useDatos(() => get('/api/usuarios'), []);
  const empresas = useDatos(() => get('/api/empresas'), []);
  const actual = datos?.filas?.find((m: any) => sel.has(m.id));
  const cambiar = (k: string, v: string) => setF((x) => ({ ...x, [k]: v }));
  const rapido = (dias: number) => { const d = new Date(Date.now() - dias * 86400000); const iso = new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10); const n = { ...f, desde: iso, hasta: '' }; setF(n); setAplicado(n); };

  return (
    <div className="pantalla">
      <h2>Movimientos <code>ZMOV</code></h2>
      <form className="filtros" onSubmit={(e) => { e.preventDefault(); setAplicado(f); }}>
        <Campo etiqueta="Operario">
          <select value={f.usuario} onChange={(e) => cambiar('usuario', e.target.value)}>
            <option value="">Todos</option>
            {(usuarios.datos ?? []).map((u: any) => <option key={u.usuario} value={u.usuario}>{u.nombre ? `${u.nombre} (${u.usuario})` : u.usuario}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Desde"><input type="date" value={f.desde} onChange={(e) => cambiar('desde', e.target.value)} /></Campo>
        <Campo etiqueta="Hasta"><input type="date" value={f.hasta} onChange={(e) => cambiar('hasta', e.target.value)} /></Campo>
        <Campo etiqueta="Tipo">
          <select value={f.tipo} onChange={(e) => cambiar('tipo', e.target.value)}><option value="">Todos</option>{TIPOS.map((t) => <option key={t} value={t}>{etiqueta(t)}</option>)}</select>
        </Campo>
        <Campo etiqueta="Estado">
          <select value={f.estado} onChange={(e) => cambiar('estado', e.target.value)}><option value="">Todos</option>{ESTADOS.map((t) => <option key={t} value={t}>{etiqueta(t)}</option>)}</select>
        </Campo>
        <Campo etiqueta="Empresa">
          <select value={f.empresa} onChange={(e) => cambiar('empresa', e.target.value)} style={{ maxWidth: 200 }}>
            <option value="">Todas</option>{(empresas.datos ?? []).map((x: any) => <option key={x.codigo} value={x.codigo}>{x.nombre}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Contenedor / artículo / posición"><input value={f.texto} onChange={(e) => cambiar('texto', e.target.value)} placeholder="I-129288, DI0406…" /></Campo>
        <button className="boton primario">Ejecutar</button>
        <div className="acciones">
          <button type="button" className="boton" onClick={() => rapido(0)}>Hoy</button>
          <button type="button" className="boton" onClick={() => rapido(1)}>Ayer y hoy</button>
          <button type="button" className="boton" onClick={() => rapido(7)}>7 días</button>
          <button type="button" className="boton" onClick={() => { const n = { ...inicial, desde: '', usuario: '', estado: '', texto: '' }; setF(n); setAplicado(n); }}>Limpiar</button>
        </div>
      </form>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className="acciones">
        <span><b>{datos?.total ?? 0}</b> movimientos {cargando && '· cargando…'}{datos && datos.total > datos.filas.length ? ` (se muestran ${datos.filas.length})` : ''}</span>
        <div className="der">
          <button className="boton" onClick={recargar}>↻</button>
          <button className="boton azul" onClick={() => descargar(`/api/movimientos/export.xlsx?${qs}`, `movimientos_${hoyISO()}.xlsx`).catch((e) => mostrar(e.message, 'error'))}>⬇ Excel</button>
        </div>
      </div>
      <div className={`con-detalle ${actual ? '' : 'cerrado'}`}>
        <Tabla filas={datos?.filas ?? []} clave={(m: any) => m.id} seleccion={sel} alSeleccionar={(s) => setSel(s)}
          columnas={[
            { id: 'hora_servidor', titulo: 'Fecha y hora', render: (m: any) => fechaHora(m.hora_servidor) },
            { id: 'usuario', titulo: 'Operario', render: (m: any) => m.usuario_nombre || m.usuario },
            { id: 'tipo', titulo: 'Tipo', render: (m: any) => <Chip v={m.tipo} tipo /> },
            { id: 'contenedor', titulo: 'Contenedor', mono: true },
            { id: 'articulo', titulo: 'Artículo', mono: true },
            { id: 'descripcion', titulo: 'Descripción' },
            { id: 'empresa_nombre', titulo: 'Empresa' },
            { id: 'cantidad', titulo: 'Cant.', num: true, valor: (m: any) => (m.cantidad != null ? Number(m.cantidad) : null) },
            { id: 'origen', titulo: 'Origen', mono: true },
            { id: 'destino', titulo: 'Destino', mono: true, render: (m: any) => m.destino ?? m.estado_nuevo ?? '' },
            { id: 'estado', titulo: 'Estado', render: (m: any) => <Chip v={m.estado} /> },
            { id: 'fotos', titulo: '📷', render: (m: any) => (m.fotos?.length ? m.fotos.length : '') },
          ]} />
        {actual && (
          <div className="detalle">
            <div className="acciones" style={{ marginBottom: 8 }}>
              <h3 style={{ margin: 0 }}>{etiqueta(actual.tipo)} · <span className="mono">{actual.contenedor}</span></h3>
              <div className="der"><button className="boton" onClick={() => setSel(new Set())}>✕</button></div>
            </div>
            <dl>
              <dt>Estado</dt><dd><Chip v={actual.estado} /></dd>
              <dt>Operario</dt><dd>{actual.usuario_nombre || actual.usuario}</dd>
              <dt>Hora servidor</dt><dd>{fechaHora(actual.hora_servidor)}</dd>
              {actual.hora_celular && <><dt>Hora celular</dt><dd>{fechaHora(actual.hora_celular)}</dd></>}
              <dt>Artículo</dt><dd>{actual.articulo} {actual.descripcion}</dd>
              <dt>Empresa</dt><dd>{actual.empresa_nombre ?? actual.empresa}</dd>
              {actual.cantidad != null && <><dt>Cantidad</dt><dd>{Number(actual.cantidad)}</dd></>}
              {actual.origen && <><dt>Origen</dt><dd className="mono">{actual.origen}</dd></>}
              {actual.destino && <><dt>Destino</dt><dd className="mono">{actual.destino}{actual.destino_ubic ? ` (Ubic.Ctrl ${actual.destino_ubic})` : ''}</dd></>}
              {actual.estado_nuevo && <><dt>Estado nuevo</dt><dd>{actual.estado_nuevo}</dd></>}
              {actual.detalle?.sugerida_wms && <><dt>Sugerida WMS</dt><dd className="mono">{actual.detalle.sugerida_wms}</dd></>}
              <dt>Respuesta WMS</dt><dd>{actual.respuesta_wms || '—'}</dd>
              <dt>Intentos</dt><dd>{actual.intentos}</dd>
              {actual.aplicado_en && <><dt>Aplicado</dt><dd>{fechaHora(actual.aplicado_en)}</dd></>}
              {actual.comentario && <><dt>Comentario</dt><dd>{actual.comentario}</dd></>}
              <dt>Dispositivo</dt><dd>{actual.dispositivo ?? '—'}</dd>
              <dt>ID</dt><dd className="mono" style={{ fontSize: 10.5 }}>{actual.id}</dd>
            </dl>
            {actual.fotos?.length > 0 && <><h3 style={{ marginTop: 12 }}>Fotos</h3><Fotos urls={actual.fotos.map(urlFoto)} /></>}
            <div className="acciones" style={{ marginTop: 14 }}>
              {actual.estado === 'error' && <button className="boton azul" onClick={async () => { await post(`/api/movimientos/${actual.id}/reintentar`); mostrar('Se reintenta cargar en el WMS', 'ok'); recargar(); }}>↻ Reintentar en WMS</button>}
              {['pendiente_wms', 'error', 'esperando_autorizacion', 'aplicado'].includes(actual.estado) && actual.tipo !== 'anulacion' &&
                <button className="boton peligro" onClick={() => setAnular(actual)}>Anular…</button>}
            </div>
          </div>
        )}
      </div>
      {anular && <Anular m={anular} cerrar={() => setAnular(null)} listo={(t) => { mostrar(t, 'ok'); setAnular(null); recargar(); }} />}
    </div>
  );
}

function Anular({ m, cerrar, listo }: { m: any; cerrar: () => void; listo: (t: string) => void }) {
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  return (
    <Modal titulo={`Anular movimiento de ${m.contenedor}`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button>
        <button className="boton peligro" disabled={motivo.trim().length < 3} onClick={async () => {
          try { const r = await post(`/api/movimientos/${m.id}/anular`, { motivo }); listo(r.mensaje); } catch (e) { setError((e as Error).message); }
        }}>Anular</button></>}>
      {m.estado === 'aplicado' && <Aviso tipo="amarillo">Este movimiento ya se cargó en el WMS. Se registra la anulación, pero el contenedor hay que moverlo de vuelta.</Aviso>}
      <Campo etiqueta="Motivo"><textarea value={motivo} onChange={(e) => setMotivo(e.target.value)} autoFocus /></Campo>
      {error && <Aviso tipo="error">{error}</Aviso>}
    </Modal>
  );
}
