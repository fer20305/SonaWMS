import { useState } from 'react';
import { get, post, put, fechaHora } from '../api';
import { Aviso, Campo, Modal, Tabla, useAbrir, useDatos, useMensaje } from '../comp';
import { TIPOS, ROLES, MEZCLAS, NOMBRE } from './Posiciones';

export default function Zonas() {
  const { datos, error, recargar } = useDatos(() => get('/api/zonas'), [], ['layout']);
  const estado = useDatos(() => get('/api/layout/estado'), [], ['layout']);
  const [editar, setEditar] = useState<any>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const mostrar = useMensaje();
  const abrir = useAbrir();
  const sincronizar = async (caras?: string[]) => {
    try { const r = await post('/api/layout/sincronizar', { caras }); mostrar(r.mensaje, 'info'); } catch (e) { mostrar((e as Error).message, 'error'); }
  };
  return (
    <div className="pantalla">
      <h2>Zonas · Layout Objetivo <code>ZZON</code></h2>
      <Aviso tipo="info">Cada cara (pasillo) tiene un tipo, un cliente, un rol (núcleo / desborde / flex) y una regla de mezcla. Con esto el sistema sugiere el destino y bloquea o pide autorización. Una posición puntual se puede cambiar en <a href="#" onClick={(e) => { e.preventDefault(); abrir('ZPOS'); }}>ZPOS</a>.</Aviso>
      <div className="acciones">
        <button className="boton primario" onClick={() => setEditar({ cara: '', tipo: 'reserva', empresa: null, rol_zona: 'nucleo', mezcla: 'monoproducto', nueva: true })}>＋ Nueva cara</button>
        <button className="boton azul" onClick={() => sincronizar()}>⟳ Sincronizar layout del WMS (todas)</button>
        <button className="boton" disabled={!sel.size} onClick={() => sincronizar([...sel])}>⟳ Sincronizar seleccionadas</button>
        <div className="der" style={{ color: 'var(--tenue)' }}>
          {estado.datos ? <>Última sincronización: {fechaHora(estado.datos.fin ?? estado.datos.inicio)} · {estado.datos.caras} caras · {estado.datos.posiciones} posiciones{estado.datos.errores?.length ? ` · errores: ${estado.datos.errores.join('; ')}` : ''}</> : 'Layout sin sincronizar en esta sesión del servidor'}
        </div>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tabla filas={datos ?? []} clave={(z: any) => z.cara} seleccion={sel} multiple alSeleccionar={setSel} alDobleClic={setEditar}
        columnas={[
          { id: 'cara', titulo: 'Cara', mono: true, render: (z: any) => <b>{z.cara}</b> },
          { id: 'tipo', titulo: 'Tipo', render: (z: any) => NOMBRE[z.tipo] },
          { id: 'empresa_nombre', titulo: 'Cliente', render: (z: any) => z.empresa_nombre ?? <span style={{ color: 'var(--tenue)' }}>— flex —</span> },
          { id: 'rol_zona', titulo: 'Rol', render: (z: any) => NOMBRE[z.rol_zona] },
          { id: 'mezcla', titulo: 'Mezcla', render: (z: any) => NOMBRE[z.mezcla] },
          { id: 'posiciones', titulo: 'Posiciones', num: true, valor: (z: any) => Number(z.posiciones) },
          { id: 'acc', titulo: '', render: (z: any) => <><button className="boton" style={{ height: 24 }} onClick={() => setEditar(z)}>Editar</button> <button className="boton" style={{ height: 24 }} onClick={() => abrir('ZMAP', { cara: z.cara })}>Mapa</button></> },
        ]} />
      {editar && <EditarZona z={editar} cerrar={() => setEditar(null)} listo={() => { setEditar(null); recargar(); mostrar('Zona guardada', 'ok'); }} />}
    </div>
  );
}

function EditarZona({ z, cerrar, listo }: { z: any; cerrar: () => void; listo: () => void }) {
  const empresas = useDatos(() => get('/api/empresas'), []);
  const [v, setV] = useState({ ...z });
  const [error, setError] = useState<string | null>(null);
  return (
    <Modal titulo={z.nueva ? 'Nueva cara' : `Cara ${z.cara}`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" disabled={!v.cara} onClick={async () => {
        try { await put(`/api/zonas/${encodeURIComponent(v.cara)}`, { tipo: v.tipo, empresa: v.empresa, rol_zona: v.rol_zona, mezcla: v.mezcla }); listo(); } catch (e) { setError((e as Error).message); }
      }}>Guardar</button></>}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, minWidth: 460 }}>
        <Campo etiqueta="Cara"><input value={v.cara} disabled={!z.nueva} onChange={(e) => setV({ ...v, cara: e.target.value.toUpperCase() })} /></Campo>
        <Campo etiqueta="Tipo"><select value={v.tipo} onChange={(e) => setV({ ...v, tipo: e.target.value })}>{TIPOS.map((t) => <option key={t} value={t}>{NOMBRE[t]}</option>)}</select></Campo>
        <Campo etiqueta="Cliente">
          <select value={v.empresa ?? ''} onChange={(e) => setV({ ...v, empresa: e.target.value ? Number(e.target.value) : null })}>
            <option value="">— flex (cualquiera) —</option>{(empresas.datos ?? []).map((x: any) => <option key={x.codigo} value={x.codigo}>{x.nombre}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Rol"><select value={v.rol_zona} onChange={(e) => setV({ ...v, rol_zona: e.target.value })}>{ROLES.map((t) => <option key={t} value={t}>{NOMBRE[t]}</option>)}</select></Campo>
        <Campo etiqueta="Mezcla"><select value={v.mezcla} onChange={(e) => setV({ ...v, mezcla: e.target.value })}>{MEZCLAS.map((t) => <option key={t} value={t}>{NOMBRE[t]}</option>)}</select></Campo>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
    </Modal>
  );
}
