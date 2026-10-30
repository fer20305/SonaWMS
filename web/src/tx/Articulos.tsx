import { useEffect, useState } from 'react';
import { get, post } from '../api';
import { Aviso, Campo, Tabla, useAbrir, useMensaje } from '../comp';

const num = (v: any) => (v == null ? '' : Number(v).toLocaleString('es-AR', { maximumFractionDigits: 2 }));

/** ZART – Todos los artículos de una empresa (maestro de LES copiado en SONA), con buscador, habilitado y stock. */
export default function Articulos() {
  const [empresa, setEmpresa] = useState('');
  const [q, setQ] = useState('');
  const [conStock, setConStock] = useState(false);
  const [habilitados, setHabilitados] = useState(false);
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const abrir = useAbrir();
  const mostrar = useMensaje();
  const leer = async () => {
    setCargando(true);
    try {
      const p = new URLSearchParams({ limite: '2000' });
      if (empresa) p.set('empresa', empresa); if (q.trim()) p.set('q', q.trim()); if (conStock) p.set('conStock', '1'); if (habilitados) p.set('habilitados', '1');
      setD(await get('/api/articulos/catalogo?' + p)); setError(null);
    } catch (e) { setError((e as Error).message); } finally { setCargando(false); }
  };
  // busca a medida que se escribe
  useEffect(() => { const t = setTimeout(leer, 300); return () => clearTimeout(t); }, [empresa, q, conStock, habilitados]); // eslint-disable-line react-hooks/exhaustive-deps
  // mientras se actualiza desde LES, refresca cada 3 s
  useEffect(() => { if (!d?.estado?.actualizando) return; const t = setTimeout(leer, 3000); return () => clearTimeout(t); }, [d]); // eslint-disable-line react-hooks/exhaustive-deps
  const actualizar = async () => { try { await post('/api/articulos/catalogo/actualizar'); mostrar('Actualizando el maestro de artículos desde LES…', 'info'); setTimeout(leer, 1500); } catch (e) { mostrar((e as Error).message, 'error'); } };
  const exportar = () => {
    const filas = [['Artículo', 'Descripción', 'Empresa', 'Habilitado', 'Stock físico', 'Disponible', 'No disponible', 'Reservado', 'Marca', 'Proyecto'],
      ...(d?.items ?? []).map((a: any) => [a.articulo, a.descripcion, a.empresaNombre, a.habilitado ? 'Sí' : 'No', a.stockFisico, a.stockDisponible, a.stockNoDisponible, a.stockReservado, a.marca, a.proyecto])];
    const csv = '﻿' + filas.map((f) => f.map((x: any) => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');
    const u = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a'); a.href = u; a.download = `articulos_${empresa || 'todas'}.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(u), 2000);
  };
  const est = d?.estado;
  return (
    <div className="pantalla">
      <h2>Artículos por empresa <code>ZART</code></h2>
      <div className="filtros">
        <Campo etiqueta="Empresa">
          <select value={empresa} onChange={(e) => setEmpresa(e.target.value)} style={{ minWidth: 260 }}>
            <option value="">Todas las empresas</option>
            {(d?.empresas ?? []).filter((x: any) => x.empresa != null).map((x: any) => <option key={x.empresa} value={x.empresa}>{x.nombre} ({x.articulos})</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Código, descripción o marca (todas las palabras)"><input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ej. ariel 3kg" style={{ width: 280 }} /></Campo>
        <label className="chk"><input type="checkbox" checked={conStock} onChange={(e) => setConStock(e.target.checked)} /> Solo con stock</label>
        <label className="chk"><input type="checkbox" checked={habilitados} onChange={(e) => setHabilitados(e.target.checked)} /> Solo habilitados</label>
        <div className="acciones" style={{ marginLeft: 'auto' }}>
          <button className="boton" onClick={exportar} disabled={!d?.items?.length}>⬇ Excel (CSV)</button>
          <button className="boton" onClick={actualizar} disabled={est?.actualizando}>↻ Actualizar desde LES</button>
        </div>
      </div>
      {est?.actualizando && <Aviso tipo="info">Leyendo el maestro de artículos de LES… {est.leidos}{est.total ? ` de ${est.total}` : ''} artículos.</Aviso>}
      {est?.error && <Aviso tipo="error">No se pudo leer LES: {est.error}</Aviso>}
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div style={{ color: 'var(--tenue)' }}>
        {cargando ? 'Buscando…' : d ? `${d.total} artículo(s)${d.total > d.mostrados ? ` · se muestran ${d.mostrados} (afiná la búsqueda)` : ''}` : ''}
        {est?.ultima && ` · maestro de LES al ${new Date(est.ultima).toLocaleString('es-AR')}`} · doble clic para consultar el artículo
      </div>
      <Tabla filas={d?.items ?? []} clave={(a: any) => a.articulo} alDobleClic={(a: any) => abrir('ZCON', { codigo: a.articulo })} vacio={est?.actualizando ? 'Cargando el maestro de LES…' : 'Sin artículos'}
        columnas={[
          { id: 'articulo', titulo: 'Artículo', mono: true, render: (a: any) => <b>{a.articulo}</b> },
          { id: 'descripcion', titulo: 'Descripción' },
          { id: 'empresaNombre', titulo: 'Empresa' },
          { id: 'habilitado', titulo: 'Habilitado', render: (a: any) => (a.habilitado ? <span className="chip aplicado">Sí</span> : <span className="chip anulado">No</span>) },
          { id: 'stockFisico', titulo: 'Stock físico', num: true, valor: (a: any) => a.stockFisico ?? 0, render: (a: any) => num(a.stockFisico) },
          { id: 'stockDisponible', titulo: 'Disponible', num: true, valor: (a: any) => a.stockDisponible ?? 0, render: (a: any) => num(a.stockDisponible) },
          { id: 'stockNoDisponible', titulo: 'No disp.', num: true, valor: (a: any) => a.stockNoDisponible ?? 0, render: (a: any) => num(a.stockNoDisponible) },
          { id: 'stockReservado', titulo: 'Reservado', num: true, valor: (a: any) => a.stockReservado ?? 0, render: (a: any) => num(a.stockReservado) },
          { id: 'marca', titulo: 'Marca' },
        ]} />
    </div>
  );
}
