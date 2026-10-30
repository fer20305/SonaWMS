import { useEffect, useState } from 'react';
import { get, imprimirEnEstaPc, post, sesion } from '../api';
import { useMensaje } from '../comp';

/** Reimprimir la etiqueta del contenedor (con QR): Zebra o A4, en la impresora del servidor o desde esta PC. */
export default function Reimprimir({ contenedor }: { contenedor: string }) {
  const [cfg, setCfg] = useState<any>(null);
  const [previa, setPrevia] = useState<Record<string, string>>({});
  const [enviando, setEnviando] = useState<string | null>(null);
  const mostrar = useMensaje();
  useEffect(() => { get('/api/impresion/etiquetas').then(setCfg).catch(() => setCfg({})); }, []);
  useEffect(() => {
    const urls: string[] = [];
    (async () => {
      for (const f of ['zebra', 'a4']) {
        try {
          const r = await fetch(`/api/contenedores/${encodeURIComponent(contenedor)}/etiqueta.png?formato=${f}`, { headers: { authorization: 'Bearer ' + sesion().token } });
          if (r.ok) { const u = URL.createObjectURL(await r.blob()); urls.push(u); setPrevia((p) => ({ ...p, [f]: u })); }
        } catch { /* sin vista previa */ }
      }
    })();
    return () => urls.forEach((u) => URL.revokeObjectURL(u));
  }, [contenedor]);
  const imprimir = async (formato: string) => {
    setEnviando(formato);
    try {
      const r = await post(`/api/contenedores/${encodeURIComponent(contenedor)}/imprimir`, { formato });
      r.ok ? mostrar(`Etiqueta de ${contenedor} enviada a ${r.impresora}`, 'ok') : mostrar(`No se pudo imprimir en ${r.impresora}: ${r.error}`, 'error');
    } catch (e) { mostrar((e as Error).message, 'error'); } finally { setEnviando(null); }
  };
  const enPc = async (formato: string) => {
    try { await imprimirEnEstaPc(`/api/contenedores/${encodeURIComponent(contenedor)}/etiqueta.html?formato=${formato}`); mostrar('Se abrió el diálogo de impresión de esta computadora', 'ok'); }
    catch (e) { mostrar((e as Error).message, 'error'); }
  };
  const imp = (f: string) => (f === 'a4' ? cfg?.impresoraA4 : cfg?.impresoraZebra) || cfg?.predeterminada || 'la predeterminada';
  return (
    <div className="tarjeta">
      <h3>🖨 Reimprimir etiqueta del contenedor {contenedor}</h3>
      <div className="reimp">
        {[{ f: 'zebra', t: 'Zebra (etiqueta)' }, { f: 'a4', t: 'Hoja A4 grande' }].map(({ f, t }) => (
          <div key={f} className="reimp-op">
            <b>{t}</b>
            {previa[f] ? <img src={previa[f]} alt={t} className={f} /> : <div className="reimp-vacio">Vista previa…</div>}
            <button className="boton primario" onClick={() => enPc(f)}>🖨 Imprimir en esta PC</button>
            <button className="boton" disabled={enviando !== null} onClick={() => imprimir(f)}>{enviando === f ? 'Enviando…' : `En el servidor (${imp(f)})`}</button>
          </div>
        ))}
      </div>
      <div style={{ color: 'var(--tenue)', fontSize: 12 }}>"Imprimir en esta PC" abre el diálogo de impresión de esta computadora: elegí la Zebra o la impresora de hojas que tengas acá (en la Zebra, papel de la medida de la etiqueta y escala 100 %). "En el servidor" usa la impresora de la PC del servidor (Sistema › Impresoras de etiquetas, ZIMP).</div>
    </div>
  );
}
