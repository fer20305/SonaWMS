import { useState } from 'react';
import { borrar, fechaHora, get, post, put } from '../api';
import { Aviso, useDatos, useMensaje } from '../comp';

interface Sis { codigo: string; nombre: string; descripcion: string; editable: boolean; usuario?: string; cargada: boolean; usaWms: boolean; probado?: string; ok?: boolean | null; mensaje?: string }

/** ZCRE – Mis credenciales: cada usuario carga una vez sus usuarios y claves de las otras plataformas (quedan cifradas en el servidor). */
export default function Credenciales() {
  const { datos, error, recargar } = useDatos(() => get('/api/yo/credenciales'), []);
  const [editando, setEditando] = useState<string | null>(null);
  const [u, setU] = useState(''); const [c, setC] = useState('');
  const [ocupado, setOcupado] = useState<string | null>(null);
  const mostrar = useMensaje();
  if (error) return <div className="pantalla"><Aviso tipo="error">{error}</Aviso></div>;
  if (!datos) return <div className="pantalla">Cargando…</div>;
  const hacer = async (cod: string, fn: () => Promise<any>, ok?: string) => {
    setOcupado(cod);
    try { const r = await fn(); if (ok) mostrar(ok, 'ok'); return r; }
    catch (e) { mostrar((e as Error).message, 'error'); } finally { setOcupado(null); }
  };
  const guardar = (s: Sis) => hacer(s.codigo, async () => { await put(`/api/yo/credenciales/${s.codigo}`, { usuario: u.trim(), clave: c }); setEditando(null); setC(''); recargar(); }, `${s.nombre}: entró bien y quedó guardado`);
  const probar = (s: Sis) => hacer(s.codigo, async () => { const r = await post(`/api/yo/credenciales/${s.codigo}/probar`); mostrar(`${s.nombre}: ${r.mensaje}`, r.ok ? 'ok' : 'error'); recargar(); });
  const quitar = (s: Sis) => { if (confirm(`¿Borrar tu usuario de ${s.nombre}?`)) hacer(s.codigo, async () => { await borrar(`/api/yo/credenciales/${s.codigo}`); recargar(); }, 'Borrado'); };
  return (
    <div className="pantalla">
      <h2>Mis credenciales <code>ZCRE</code> · {datos.usuario}</h2>
      <Aviso tipo="info">
        Cargá una sola vez tu usuario y clave de cada plataforma: SONA entra con ellos en tu nombre (panel web, panel de escritorio, módulo de Ingreso y la app).
        Quedan cifrados en el servidor; nadie los ve, ni los administradores. Antes de guardar, SONA prueba que entren.
      </Aviso>
      <div className="creds">
        {(datos.sistemas as Sis[]).map((s) => (
          <div key={s.codigo} className="tarjeta cred">
            <div className="cred-cab">
              <b>{s.nombre}</b>
              {s.cargada ? <span className="chip ok">{s.usaWms ? 'usa tu usuario del WMS' : 'cargada'}</span> : s.usaWms ? <span className="chip">usa tu usuario del WMS</span> : <span className="chip error">sin cargar</span>}
              {s.ok === true && <span className="chip ok">✔ probada {fechaHora(s.probado)}</span>}
              {s.ok === false && <span className="chip error" title={s.mensaje}>✖ no entró {fechaHora(s.probado)}</span>}
            </div>
            <div className="cred-desc">{s.descripcion}</div>
            {s.ok === false && s.mensaje && <Aviso tipo="error">{s.mensaje}</Aviso>}
            {editando === s.codigo ? (
              <div className="fila" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                <label className="campo"><span>Usuario</span><input value={u} onChange={(e) => setU(e.target.value)} autoComplete="off" style={{ width: 180 }} /></label>
                <label className="campo"><span>Clave</span><input type="password" value={c} onChange={(e) => setC(e.target.value)} autoComplete="new-password" style={{ width: 180 }}
                  onKeyDown={(e) => e.key === 'Enter' && guardar(s)} /></label>
                <button className="boton primario" disabled={ocupado !== null || !u.trim() || !c} onClick={() => guardar(s)}>{ocupado === s.codigo ? 'Probando…' : 'Probar y guardar'}</button>
                <button className="boton" onClick={() => { setEditando(null); setC(''); }}>Cancelar</button>
              </div>
            ) : (
              <div className="fila" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <span>Usuario: <b>{s.usuario || (s.usaWms ? datos.usuario : '—')}</b></span>
                {s.editable && <button className="boton primario" onClick={() => { setEditando(s.codigo); setU(s.usuario || datos.usuario); setC(''); }}>{s.cargada && !s.usaWms ? 'Cambiar' : 'Cargar'}</button>}
                {(s.cargada || s.usaWms) && <button className="boton" disabled={ocupado !== null} onClick={() => probar(s)}>{ocupado === s.codigo ? 'Probando…' : 'Probar'}</button>}
                {s.editable && s.cargada && !s.usaWms && <button className="boton" onClick={() => quitar(s)}>Borrar</button>}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
