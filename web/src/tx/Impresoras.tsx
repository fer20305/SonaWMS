import { useEffect, useState } from 'react';
import { get, post, put } from '../api';
import { Aviso, Campo, useDatos, useMensaje } from '../comp';

/** ZIMP – Impresoras de las etiquetas de medida (control de bulto): una por puesto, en la PC del servidor SONA. */
export default function Impresoras() {
  const { datos: d, error, recargar } = useDatos(() => get('/api/bultos/impresion'), []);
  const [v, setV] = useState<any>(null);
  const [previa, setPrevia] = useState(0);
  const mostrar = useMensaje();
  useEffect(() => { if (d) setV({ ...d, impresoras: { ...(d.impresoras ?? {}) } }); }, [d]);
  if (error) return <div className="pantalla"><Aviso tipo="error">{error}</Aviso></div>;
  if (!v) return <div className="pantalla">Cargando…</div>;
  const lista: string[] = v.instaladas ?? [];
  const sel = (valor: string, cambiar: (s: string) => void, vacio: string) => (
    <select value={valor} disabled={!v.puedeEditar} onChange={(e) => cambiar(e.target.value)} style={{ minWidth: 300 }}>
      <option value="">{vacio}</option>
      {valor && !lista.includes(valor) && <option value={valor}>{valor} (no instalada)</option>}
      {lista.map((i) => <option key={i} value={i}>{i}</option>)}
    </select>
  );
  const guardar = async () => {
    try {
      await put('/api/bultos/impresion', { imprimirAlGuardar: v.imprimirAlGuardar, impresoras: v.impresoras, impresora: v.impresora, copias: Number(v.copias), soloNuevos: v.soloNuevos, anchoMm: Number(v.anchoMm), altoMm: Number(v.altoMm) });
      mostrar('Configuración de impresoras guardada', 'ok'); recargar(); setPrevia((x) => x + 1);
    } catch (e) { mostrar((e as Error).message, 'error'); }
  };
  const probar = async (origen: string) => {
    try {
      const r = await post('/api/bultos/impresion/probar', { origen });
      r.ok ? mostrar(`Etiqueta de prueba enviada a ${r.impresora}`, 'ok') : mostrar(`No se pudo imprimir en ${r.impresora}: ${r.error}`, 'error');
    } catch (e) { mostrar((e as Error).message, 'error'); }
  };
  return (
    <div className="pantalla">
      <h2>Impresoras de etiquetas de bultos <code>ZIMP</code></h2>
      <Aviso tipo="info">
        Cuando en la app (Control de bulto) se toca <b>Guardar y generar etiquetas</b>, SONA guarda las medidas en LES e imprime la etiqueta de cada bulto en la
        impresora del puesto elegido. Las impresoras son las instaladas en la PC del servidor SONA{v.predeterminada ? <> (predeterminada: <b>{v.predeterminada}</b>)</> : null}.
      </Aviso>
      {!v.windows && <Aviso tipo="amarillo">El servidor no está en Windows: la impresión directa no funciona en esta instalación.</Aviso>}
      {!v.puedeEditar && <Aviso tipo="amarillo">Solo lectura: para cambiarla hace falta el permiso ZIMP (ZUSR › Permisos).</Aviso>}
      <div className="dos-col">
        <div className="tarjeta">
          <h3>Impresora por puesto</h3>
          {(v.origenes ?? []).map((o: any) => (
            <div key={o.codigo} className="acciones" style={{ alignItems: 'flex-end' }}>
              <Campo etiqueta={o.nombre}>{sel(v.impresoras[o.codigo] ?? '', (s) => setV({ ...v, impresoras: { ...v.impresoras, [o.codigo]: s } }), '— la general —')}</Campo>
              <button className="boton" disabled={!v.puedeEditar} onClick={() => probar(o.codigo)}>🖨 Probar</button>
            </div>
          ))}
          <Campo etiqueta="Impresora general (para los puestos sin impresora propia)">{sel(v.impresora ?? '', (s) => setV({ ...v, impresora: s }), '— predeterminada de Windows —')}</Campo>
        </div>
        <div className="tarjeta">
          <h3>Opciones</h3>
          <label className="chk"><input type="checkbox" disabled={!v.puedeEditar} checked={v.imprimirAlGuardar} onChange={(e) => setV({ ...v, imprimirAlGuardar: e.target.checked })} /> Imprimir al guardar (en la app se puede destildar para un pedido)</label>
          <label className="chk"><input type="checkbox" disabled={!v.puedeEditar} checked={v.soloNuevos} onChange={(e) => setV({ ...v, soloNuevos: e.target.checked })} /> Imprimir solo los bultos nuevos y los modificados (si no, todos los de la solicitud)</label>
          <div className="acciones">
            <Campo etiqueta="Copias"><input type="number" min={1} max={5} disabled={!v.puedeEditar} value={v.copias} onChange={(e) => setV({ ...v, copias: e.target.value })} style={{ width: 80, minWidth: 80 }} /></Campo>
            <Campo etiqueta="Ancho (mm)"><input type="number" disabled={!v.puedeEditar} value={v.anchoMm} onChange={(e) => setV({ ...v, anchoMm: e.target.value })} style={{ width: 90, minWidth: 90 }} /></Campo>
            <Campo etiqueta="Alto (mm)"><input type="number" disabled={!v.puedeEditar} value={v.altoMm} onChange={(e) => setV({ ...v, altoMm: e.target.value })} style={{ width: 90, minWidth: 90 }} /></Campo>
          </div>
          <h3 style={{ marginTop: 6 }}>Así sale la etiqueta</h3>
          <EtiquetaPrevia clave={previa} />
        </div>
      </div>
      {v.puedeEditar && <div className="acciones"><button className="boton primario" onClick={guardar}>Guardar</button></div>}
      <EtiquetasContenedor />
    </div>
  );
}

/** Impresoras para reimprimir la etiqueta del contenedor (con QR): Zebra y A4. */
function EtiquetasContenedor() {
  const { datos: d, recargar } = useDatos(() => get('/api/impresion/etiquetas'), []);
  const [v, setV] = useState<any>(null);
  const mostrar = useMensaje();
  useEffect(() => { if (d) setV({ ...d }); }, [d]);
  if (!v) return null;
  const lista: string[] = v.instaladas ?? [];
  const sel = (valor: string, cambiar: (s: string) => void) => (
    <select value={valor ?? ''} disabled={!v.puedeEditar} onChange={(e) => cambiar(e.target.value)} style={{ minWidth: 300 }}>
      <option value="">— predeterminada de Windows —</option>
      {valor && !lista.includes(valor) && <option value={valor}>{valor} (no instalada)</option>}
      {lista.map((i) => <option key={i} value={i}>{i}</option>)}
    </select>
  );
  const guardar = async () => {
    try { await put('/api/impresion/etiquetas', { impresoraZebra: v.impresoraZebra, impresoraA4: v.impresoraA4, anchoMm: Number(v.anchoMm), altoMm: Number(v.altoMm) }); mostrar('Impresoras de etiquetas de contenedor guardadas', 'ok'); recargar(); }
    catch (e) { mostrar((e as Error).message, 'error'); }
  };
  return (
    <div className="tarjeta">
      <h3>Etiquetas de contenedor (Consultar › Reimprimir etiqueta, con QR)</h3>
      <div className="acciones" style={{ alignItems: 'flex-end' }}>
        <Campo etiqueta="Impresora Zebra">{sel(v.impresoraZebra, (x) => setV({ ...v, impresoraZebra: x }))}</Campo>
        <Campo etiqueta="Ancho (mm)"><input type="number" disabled={!v.puedeEditar} value={v.anchoMm} onChange={(e) => setV({ ...v, anchoMm: e.target.value })} style={{ width: 90, minWidth: 90 }} /></Campo>
        <Campo etiqueta="Alto (mm)"><input type="number" disabled={!v.puedeEditar} value={v.altoMm} onChange={(e) => setV({ ...v, altoMm: e.target.value })} style={{ width: 90, minWidth: 90 }} /></Campo>
      </div>
      <Campo etiqueta="Impresora para hojas A4">{sel(v.impresoraA4, (x) => setV({ ...v, impresoraA4: x }))}</Campo>
      {v.puedeEditar && <div className="acciones"><button className="boton primario" onClick={guardar}>Guardar</button></div>}
    </div>
  );
}

function EtiquetaPrevia({ clave }: { clave: number }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let u: string | null = null;
    (async () => {
      try {
        const t = JSON.parse(localStorage.getItem('sona.panel') ?? '{}').token;
        const r = await fetch('/api/bultos/PRUEBA-1/etiqueta/3123456', { headers: { authorization: 'Bearer ' + t } });
        if (r.ok) { u = URL.createObjectURL(await r.blob()); setUrl(u); }
      } catch { /* sin vista previa */ }
    })();
    return () => { if (u) URL.revokeObjectURL(u); };
  }, [clave]);
  return url ? <img src={url} alt="Etiqueta de ejemplo" style={{ maxWidth: 360, border: '1px solid var(--borde)', borderRadius: 4 }} /> : <div style={{ color: 'var(--tenue)' }}>Sin vista previa</div>;
}
