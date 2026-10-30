import { useState } from 'react';
import { get, put, sesion } from '../api';
import { Aviso, useDatos, useMensaje } from '../comp';

const AYUDA: Record<string, string> = {
  poca_cantidad_m3: 'Hasta este volumen (m³) se considera "pocas cajas": se permite dejarlo junto al mismo artículo o la misma empresa aunque la posición sea monoproducto.',
  niveles_max: 'Niveles que se recorren cuando se consulta una posición sin nivel y no hay ficha.',
  empresas_guardado: 'Empresas (códigos del WMS) cuyos contenedores pendientes de guardar se muestran en la app. Ej: [6, 38]',
  niveles_rotacion_alta: 'Niveles preferidos para artículos de mucha rotación. Los de poca rotación van a niveles altos.',
  dias_rotacion: 'Días hacia atrás para medir la rotación de un artículo.',
  fotos_obligatorias: 'Motivos de "No disponible" que exigen foto. Ej: ["ROTURA","NO CONFORME"]',
};

export default function Parametros() {
  const { datos, error, recargar } = useDatos(() => get('/api/parametros'), []);
  const [edit, setEdit] = useState<Record<string, string>>({});
  const mostrar = useMensaje();
  const admin = sesion().rol === 'admin';
  const guardar = async (clave: string) => {
    try { await put(`/api/parametros/${clave}`, { valor: JSON.parse(edit[clave]) }); mostrar('Parámetro guardado', 'ok'); setEdit((e) => { const n = { ...e }; delete n[clave]; return n; }); recargar(); }
    catch (e) { mostrar(e instanceof SyntaxError ? 'Valor inválido (tiene que ser número, texto entre comillas o lista)' : (e as Error).message, 'error'); }
  };
  return (
    <div className="pantalla">
      <h2>Parámetros <code>ZPAR</code></h2>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <table className="alv" style={{ maxWidth: 1100 }}>
        <thead><tr><th>Parámetro</th><th>Valor</th><th>Para qué sirve</th><th /></tr></thead>
        <tbody>
          {(datos ?? []).map((p: any) => (
            <tr key={p.clave}>
              <td className="mono"><b>{p.clave}</b></td>
              <td>{admin ? <input className="mono" style={{ width: 220, border: '1px solid var(--borde)', borderRadius: 3, padding: '3px 6px' }}
                value={edit[p.clave] ?? JSON.stringify(p.valor)} onChange={(e) => setEdit({ ...edit, [p.clave]: e.target.value })} /> : <span className="mono">{JSON.stringify(p.valor)}</span>}</td>
              <td style={{ whiteSpace: 'normal' }}>{AYUDA[p.clave] ?? ''}</td>
              <td>{edit[p.clave] !== undefined && <button className="boton primario" style={{ height: 26 }} onClick={() => guardar(p.clave)}>Guardar</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
