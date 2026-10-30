import { useState } from 'react';
import { get, post, fechaHora } from '../api';
import { Aviso, Chip, useDatos, useMensaje } from '../comp';

export default function Autorizaciones() {
  const [ver, setVer] = useState<'pendiente' | ''>('pendiente');
  const { datos, error, recargar } = useDatos(() => get(`/api/autorizaciones${ver ? '?estado=' + ver : ''}`), [ver], ['autorizacion', 'movimiento']);
  const mostrar = useMensaje();
  const [notas, setNotas] = useState<Record<number, string>>({});
  const resolver = async (a: any, aprobar: boolean) => {
    try {
      await post(`/api/autorizaciones/${a.id}`, { aprobar, nota: notas[a.id] });
      mostrar(aprobar ? `Autorizado: ${a.motivo}` : `Rechazado: ${a.motivo}`, aprobar ? 'ok' : 'info');
      recargar();
    } catch (e) { mostrar((e as Error).message, 'error'); }
  };
  return (
    <div className="pantalla">
      <h2>Autorizaciones <code>ZAUT</code></h2>
      <Aviso tipo="info">Cuando un operario quiere dejar material en una zona asignada a otro cliente (Layout Objetivo), el celular espera la respuesta de esta pantalla. Al autorizar, el movimiento se carga en el WMS.</Aviso>
      <div className="acciones">
        <button className={`boton ${ver === 'pendiente' ? 'azul' : ''}`} onClick={() => setVer('pendiente')}>Pendientes</button>
        <button className={`boton ${ver === '' ? 'azul' : ''}`} onClick={() => setVer('')}>Todas</button>
        <div className="der"><button className="boton" onClick={recargar}>↻</button></div>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      {!datos?.length && <div className="vacio">{ver ? 'No hay pedidos pendientes 👍' : 'Sin autorizaciones'}</div>}
      {(datos ?? []).map((a: any) => (
        <div key={a.id} className={`autorizacion ${a.estado !== 'pendiente' ? 'resuelta' : ''}`}>
          <div className="txt">
            <div><b>{a.solicitado_por}</b> · {fechaHora(a.creado)} · <Chip v={a.estado} />{a.resuelto_por && <> por <b>{a.resuelto_por}</b> {fechaHora(a.resuelto_en)}</>}</div>
            <div style={{ fontSize: 14, margin: '4px 0' }}>{a.motivo}</div>
            <div className="mono" style={{ color: 'var(--tenue)' }}>
              {(a.movimientos ?? []).map((m: any) => `${m.contenedor} (${m.articulo ?? '?'}) ${m.origen ?? ''} → ${m.destino}`).join(' · ')}
            </div>
          </div>
          {a.estado === 'pendiente' && (
            <>
              <input placeholder="Nota (opcional)" value={notas[a.id] ?? ''} onChange={(e) => setNotas((n) => ({ ...n, [a.id]: e.target.value }))}
                style={{ border: '1px solid var(--borde)', borderRadius: 3, height: 34, padding: '0 8px', width: 200 }} />
              <button className="boton ok" style={{ height: 38, padding: '0 22px' }} onClick={() => resolver(a, true)}>✔ Sí, autorizar</button>
              <button className="boton peligro" style={{ height: 38, padding: '0 22px' }} onClick={() => resolver(a, false)}>✕ No</button>
            </>
          )}
        </div>
      ))}
    </div>
  );
}
