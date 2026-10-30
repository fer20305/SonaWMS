import { Fragment, useMemo, useState } from 'react';
import { get } from '../api';
import { Campo, useAbrir, useDatos } from '../comp';
import type { PropsTx } from '../App';
import { Editar, NOMBRE } from './Posiciones';

export default function Mapa({ parametros }: PropsTx) {
  const [cara, setCara] = useState(parametros?.cara ?? '');
  const zonas = useDatos(() => get('/api/zonas'), []);
  const { datos, recargar } = useDatos(() => (cara ? get(`/api/posiciones?cara=${encodeURIComponent(cara)}`) : Promise.resolve([])), [cara], ['posiciones', 'layout']);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [editar, setEditar] = useState(false);
  const abrir = useAbrir();
  const { columnas, niveles, mapa } = useMemo(() => {
    const m = new Map<string, any>();
    const cols = new Set<number>(), nivs = new Set<number>();
    for (const p of datos ?? []) { m.set(`${p.columna}-${p.nivel}`, p); cols.add(p.columna); nivs.add(p.nivel); }
    return { columnas: [...cols].sort((a, b) => a - b), niveles: [...nivs].sort((a, b) => b - a), mapa: m };
  }, [datos]);
  return (
    <div className="pantalla">
      <h2>Mapa de pasillo <code>ZMAP</code></h2>
      <div className="filtros">
        <Campo etiqueta="Cara">
          <select value={cara} onChange={(e) => { setCara(e.target.value); setSel(new Set()); }}>
            <option value="">—</option>{(zonas.datos ?? []).map((z: any) => <option key={z.cara} value={z.cara}>{z.cara}</option>)}
          </select>
        </Campo>
        <div className="leyenda">
          <span><i style={{ borderLeft: '3px solid var(--navy)', background: '#fff', border: '1px solid var(--borde)' }} />Núcleo</span>
          <span><i style={{ background: 'var(--naranja)' }} />Desborde</span>
          <span><i style={{ background: '#9AA5B1' }} />Flex</span>
          <span><i style={{ background: '#E8EEF5', border: '1px solid var(--borde)' }} />Ocupada (WMS)</span>
          <span><i style={{ background: 'repeating-linear-gradient(45deg,#fde7e7,#fde7e7 3px,#fff 3px,#fff 6px)', border: '1px solid var(--borde)' }} />Inhabilitada</span>
        </div>
        <div style={{ marginLeft: 'auto' }} className="acciones">
          <span>{sel.size} seleccionadas</span>
          <button className="boton primario" disabled={!sel.size} onClick={() => setEditar(true)}>✎ Editar</button>
        </div>
      </div>
      {!cara && <div className="vacio">Elegí una cara para ver su mapa (columnas × niveles).</div>}
      {cara && (
        <div style={{ overflow: 'auto' }}>
          <div className="mapa" style={{ gridTemplateColumns: `40px repeat(${columnas.length}, minmax(54px, 1fr))` }}>
            {niveles.map((n) => (
              <Fragment key={'n' + n}>
                <div style={{ display: 'grid', placeItems: 'center', color: 'var(--tenue)' }}>N{n}</div>
                {columnas.map((c) => {
                  const p = mapa.get(`${c}-${n}`);
                  if (!p) return <div key={`${c}-${n}`} />;
                  return (
                    <div key={p.codigo} title={`${p.codigo} · ${NOMBRE[p.tipo]} · ${p.empresa_nombre ?? 'flex'} · ${NOMBRE[p.mezcla]} · ${p.estado_wms ?? ''}`}
                      className={`celda ${p.rol_zona} ${p.estado_wms === 'Ocupado' ? 'ocupado' : ''} ${!p.habilitada ? 'inhab' : ''} ${sel.has(p.codigo) ? 'sel' : ''}`}
                      onClick={(e) => setSel((s) => { const x = new Set(e.ctrlKey || e.shiftKey ? s : []); x.has(p.codigo) ? x.delete(p.codigo) : x.add(p.codigo); return x; })}
                      onDoubleClick={() => abrir('ZCON', { codigo: p.codigo })}>
                      <div className="mono">{p.codigo}</div>
                      <div style={{ color: 'var(--tenue)' }}>{Number(p.contenedores)}{p.capacidad ? `/${p.capacidad}` : ''}</div>
                    </div>
                  );
                })}
              </Fragment>
            ))}
            <div />
            {columnas.map((c) => <div key={'c' + c} style={{ textAlign: 'center', color: 'var(--tenue)' }}>{c}</div>)}
          </div>
        </div>
      )}
      {editar && <Editar codigos={[...sel]} cerrar={() => setEditar(false)} listo={() => { setEditar(false); recargar(); }} />}
    </div>
  );
}
