import { useMemo, useState } from 'react';
import { get, put, fechaHora } from '../api';
import { Aviso, Campo, Modal, Tabla, useAbrir, useDatos, useMensaje } from '../comp';
import type { PropsTx } from '../App';

export const TIPOS = ['reserva', 'picking', 'proyectos', 'nn', 'otro_deposito'];
export const ROLES = ['nucleo', 'desborde', 'flex'];
export const MEZCLAS = ['monoproducto', 'mismo_cliente', 'libre'];
export const NOMBRE: Record<string, string> = {
  reserva: 'Reserva', picking: 'Picking', proyectos: 'Proyectos', nn: 'NN', otro_deposito: 'Otro depósito',
  nucleo: 'Núcleo', desborde: 'Desborde', flex: 'Flex', monoproducto: 'Monoproducto', mismo_cliente: 'Mismo cliente', libre: 'Libre',
};

export default function Posiciones({ parametros }: PropsTx) {
  const [cara, setCara] = useState(parametros?.cara ?? '');
  const [texto, setTexto] = useState(parametros?.texto ?? '');
  const [filtro, setFiltro] = useState({ cara: parametros?.cara ?? '', texto: parametros?.texto ?? '' });
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [editar, setEditar] = useState(false);
  const zonas = useDatos(() => get('/api/zonas'), []);
  const { datos, error, recargar, cargando } = useDatos(
    () => (filtro.cara || filtro.texto ? get(`/api/posiciones?${new URLSearchParams(Object.entries(filtro).filter(([, v]) => v))}`) : Promise.resolve([])),
    [filtro.cara, filtro.texto], ['posiciones', 'layout']);
  const abrir = useAbrir();
  const filas = datos ?? [];
  const resumen = useMemo(() => ({
    total: filas.length, inhab: filas.filter((p: any) => !p.habilitada).length, libres: filas.filter((p: any) => p.estado_wms === 'Libre').length,
  }), [filas]);

  return (
    <div className="pantalla">
      <h2>Fichas de posiciones <code>ZPOS</code></h2>
      <form className="filtros" onSubmit={(e) => { e.preventDefault(); setFiltro({ cara, texto }); setSel(new Set()); }}>
        <Campo etiqueta="Cara (pasillo)">
          <select value={cara} onChange={(e) => setCara(e.target.value)}>
            <option value="">—</option>{(zonas.datos ?? []).map((z: any) => <option key={z.cara} value={z.cara}>{z.cara} ({z.posiciones})</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Código contiene"><input value={texto} onChange={(e) => setTexto(e.target.value.toUpperCase())} placeholder="FI15" /></Campo>
        <button className="boton primario">Mostrar</button>
        <span style={{ color: 'var(--tenue)' }}>Las fichas vienen del WMS (Ubicaciones). Lo que se edita acá pisa lo de la zona; vacío = toma lo de la zona.</span>
      </form>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className="acciones">
        <span><b>{resumen.total}</b> posiciones · {resumen.libres} libres según WMS · {resumen.inhab} inhabilitadas {cargando && '· cargando…'}</span>
        <div className="der">
          <span>{sel.size} seleccionadas (Ctrl/Shift para varias)</span>
          <button className="boton" onClick={() => setSel(new Set(filas.map((p: any) => p.codigo)))}>Seleccionar todo</button>
          <button className="boton primario" disabled={!sel.size} onClick={() => setEditar(true)}>✎ Editar seleccionadas</button>
          {filtro.cara && <button className="boton" onClick={() => abrir('ZMAP', { cara: filtro.cara })}>Ver mapa</button>}
        </div>
      </div>
      <Tabla filas={filas} clave={(p: any) => p.codigo} seleccion={sel} multiple alSeleccionar={setSel} alDobleClic={(p: any) => abrir('ZCON', { codigo: p.codigo })} vacio="Elegí una cara o escribí un código"
        columnas={[
          { id: 'codigo', titulo: 'Posición', mono: true, render: (p: any) => <b>{p.codigo}</b> },
          { id: 'ubic_ctrl', titulo: 'Ubic.Ctrl', num: true },
          { id: 'tipo', titulo: 'Tipo', render: (p: any) => NOMBRE[p.tipo] ?? p.tipo },
          { id: 'empresa_nombre', titulo: 'Cliente asignado', render: (p: any) => p.empresa_nombre ?? <span style={{ color: 'var(--tenue)' }}>— flex —</span> },
          { id: 'rol_zona', titulo: 'Rol', render: (p: any) => NOMBRE[p.rol_zona] },
          { id: 'mezcla', titulo: 'Mezcla', render: (p: any) => NOMBRE[p.mezcla] },
          { id: 'capacidad', titulo: 'Huecos', num: true },
          { id: 'medidas', titulo: 'An×Al×Pr (cm)', valor: (p: any) => (p.ancho_cm ? `${p.ancho_cm}×${p.alto_cm}×${p.prof_cm}` : '') },
          { id: 'estado_wms', titulo: 'WMS' },
          { id: 'contenedores', titulo: 'Cont.', num: true, valor: (p: any) => Number(p.contenedores) },
          { id: 'habilitada', titulo: 'Habilitada', render: (p: any) => (p.habilitada ? '✔' : <span style={{ color: 'var(--error)' }}>✕ {p.motivo_inhab}</span>) },
        ]} />
      {editar && <Editar codigos={[...sel]} cerrar={() => setEditar(false)} listo={() => { setEditar(false); recargar(); }} />}
    </div>
  );
}

export function Editar({ codigos, cerrar, listo }: { codigos: string[]; cerrar: () => void; listo: () => void }) {
  const empresas = useDatos(() => get('/api/empresas'), []);
  const mostrar = useMensaje();
  // '' = no cambiar · 'null' = volver a lo de la zona
  const [c, setC] = useState({ tipo: '', empresa: '', rol_zona: '', mezcla: '', capacidad: '', habilitada: '', motivo_inhab: '' });
  const [error, setError] = useState<string | null>(null);
  const val = (v: string, n = false) => (v === '' ? undefined : v === 'null' ? null : n ? Number(v) : v);
  const guardar = async () => {
    try {
      const cambios = {
        tipo: val(c.tipo), empresa: val(c.empresa, true), rol_zona: val(c.rol_zona), mezcla: val(c.mezcla), capacidad: val(c.capacidad, true),
        habilitada: c.habilitada === '' ? undefined : c.habilitada === 'si', motivo_inhab: c.habilitada === 'no' ? c.motivo_inhab || null : c.habilitada === 'si' ? null : undefined,
      };
      const r = await put('/api/posiciones', { codigos, cambios });
      mostrar(`${r.cambiadas} posiciones actualizadas`, 'ok');
      listo();
    } catch (e) { setError((e as Error).message); }
  };
  const sel = (k: keyof typeof c, ops: string[], nombres: Record<string, string> = NOMBRE) => (
    <select value={c[k]} onChange={(e) => setC({ ...c, [k]: e.target.value })}>
      <option value="">(no cambiar)</option><option value="null">← lo de la zona</option>
      {ops.map((o) => <option key={o} value={o}>{nombres[o] ?? o}</option>)}
    </select>
  );
  return (
    <Modal titulo={`Editar ${codigos.length} posición(es)`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" onClick={guardar}>Guardar</button></>}>
      <div style={{ color: 'var(--tenue)', maxWidth: 520 }} className="mono">{codigos.slice(0, 30).join(' ')}{codigos.length > 30 ? ' …' : ''}</div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        <Campo etiqueta="Tipo">{sel('tipo', TIPOS)}</Campo>
        <Campo etiqueta="Cliente asignado">
          <select value={c.empresa} onChange={(e) => setC({ ...c, empresa: e.target.value })}>
            <option value="">(no cambiar)</option><option value="null">← lo de la zona</option>
            {(empresas.datos ?? []).map((x: any) => <option key={x.codigo} value={x.codigo}>{x.nombre}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Rol de zona">{sel('rol_zona', ROLES)}</Campo>
        <Campo etiqueta="Mezcla">{sel('mezcla', MEZCLAS)}</Campo>
        <Campo etiqueta="Huecos (capacidad)"><input value={c.capacidad} placeholder="(no cambiar)" onChange={(e) => setC({ ...c, capacidad: e.target.value.replace(/\D/g, '') })} /></Campo>
        <Campo etiqueta="Habilitada">
          <select value={c.habilitada} onChange={(e) => setC({ ...c, habilitada: e.target.value })}><option value="">(no cambiar)</option><option value="si">Sí</option><option value="no">No (inhabilitar)</option></select>
        </Campo>
      </div>
      {c.habilitada === 'no' && <Campo etiqueta="Motivo de inhabilitación"><input value={c.motivo_inhab} onChange={(e) => setC({ ...c, motivo_inhab: e.target.value })} placeholder="Rack dañado, reservado…" /></Campo>}
      {error && <Aviso tipo="error">{error}</Aviso>}
    </Modal>
  );
}

export const fh = fechaHora;
