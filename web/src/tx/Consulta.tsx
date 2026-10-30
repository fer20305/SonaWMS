import { Fragment, useState } from 'react';
import { get, fechaHora } from '../api';
import { Aviso, Campo, Chip, Fotos, Tabla, useAbrir } from '../comp';
import type { PropsTx } from '../App';
import Reimprimir from './Reimprimir';

export default function Consulta({ parametros }: PropsTx) {
  const [texto, setTexto] = useState(parametros?.codigo ?? '');
  const [nivel, setNivel] = useState('');
  const [r, setR] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const abrir = useAbrir();
  const buscar = async (t = texto) => {
    if (!t.trim()) return;
    setCargando(true); setError(null);
    try { setR(await get(`/api/consulta/${encodeURIComponent(t.trim())}${nivel ? '?nivel=' + nivel : ''}`)); } catch (e) { setError((e as Error).message); setR(null); } finally { setCargando(false); }
  };
  return (
    <div className="pantalla">
      <h2>Consultar <code>ZCON</code></h2>
      <form className="filtros" onSubmit={(e) => { e.preventDefault(); buscar(); }}>
        <Campo etiqueta="Ubic.Ctrl, posición (FI1502), contenedor o artículo"><input autoFocus value={texto} onChange={(e) => setTexto(e.target.value.toUpperCase())} style={{ width: 280 }} /></Campo>
        <Campo etiqueta="Nivel (vacío = todos)"><input value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} style={{ width: 90, minWidth: 90 }} /></Campo>
        <button className="boton primario" disabled={cargando}>{cargando ? 'Consultando WMS…' : 'Consultar'}</button>
      </form>
      {error && <Aviso tipo="error">{error}</Aviso>}
      {r?.tipo === 'nada' && <Aviso tipo="amarillo">{r.mensaje}</Aviso>}
      {r?.tipo === 'posicion' && (
        <>
          <h3>Posición {r.posicion.legible} {r.posicion.ubicCtrl && <span style={{ color: 'var(--tenue)' }}>· Ubic.Ctrl {r.posicion.ubicCtrl}</span>}</h3>
          {!r.niveles.length && <Aviso tipo="amarillo">El WMS no devolvió niveles para esa posición.</Aviso>}
          {r.niveles.map((n: any) => (
            <div key={n.codigo} className="tarjeta">
              <div className="acciones">
                <b className="mono" style={{ fontSize: 14 }}>{n.codigo}</b>
                <span>{n.ocupados}/{n.capacidad} ocupados · {n.libres} libres{n.inhabilitados ? ` · ${n.inhabilitados} inhabilitados` : ''}</span>
                {n.ficha && <span style={{ color: 'var(--tenue)' }}>{n.ficha.tipo} · {n.ficha.rol_zona} · {n.ficha.mezcla}{n.ficha.empresa ? ` · empresa ${n.ficha.empresa}` : ''}{!n.ficha.habilitada ? ' · INHABILITADA' : ''}</span>}
                <div className="der"><button className="boton" onClick={() => abrir('ZPOS', { texto: n.codigo })}>Ficha</button></div>
              </div>
              {n.detalle.length > 0 && <Tabla filas={n.detalle} clave={(c: any) => c.contenedor} alDobleClic={(c: any) => { setTexto(c.contenedor); buscar(c.contenedor); }}
                columnas={[
                  { id: 'foto', titulo: '', render: (c: any) => (c.foto ? <img src={c.foto} style={{ width: 34, height: 34, objectFit: 'cover', borderRadius: 3 }} alt="" /> : '') },
                  { id: 'contenedor', titulo: 'Contenedor', mono: true },
                  { id: 'articulo', titulo: 'Artículo', mono: true },
                  { id: 'descripcion', titulo: 'Descripción' },
                  { id: 'empresaNombre', titulo: 'Empresa' },
                  { id: 'cantidad', titulo: 'Cantidad', num: true },
                  { id: 'm3', titulo: 'm³', num: true },
                ]} />}
            </div>
          ))}
        </>
      )}
      {r?.tipo === 'contenedor' && (
        <div className="dos-col">
          <div className="tarjeta">
            <h3>Contenedor <span className="mono">{r.contenedor.contenedor}</span></h3>
            <div className="detalle" style={{ border: 0, padding: 0, background: 'none' }}>
              <dl>
                <dt>Artículo</dt><dd className="mono">{r.contenedor.articulo}</dd>
                <dt>Descripción</dt><dd>{r.contenedor.descripcion}</dd>
                <dt>Empresa</dt><dd>{r.contenedor.empresaNombre ?? r.contenedor.cliente}</dd>
                <dt>Cantidad</dt><dd>{r.contenedor.cantidad}</dd>
                {r.contenedor.m3 != null && <><dt>m³</dt><dd>{r.contenedor.m3}</dd></>}
                <dt>Posición</dt><dd className="mono">{r.contenedor.posicion}</dd>
                {Object.entries(r.contenedor.campos).filter(([k]) => !['CLIENTE', 'CODIGO_ARTICULO', 'DESCRIPCION_ARTICULO', 'CONTENEDOR', 'CANTIDAD', 'POS'].includes(k))
                  .map(([k, v]) => <Fragment key={k}><dt>{k}</dt><dd>{String(v)}</dd></Fragment>)}
              </dl>
            </div>
            {r.contenedor.foto && <Fotos urls={[r.contenedor.foto]} />}
          </div>
          <div className="tarjeta">
            <h3>Historial en SONA Depósito</h3>
            <Tabla filas={r.movimientos} clave={(m: any) => m.id} vacio="Sin movimientos registrados"
              columnas={[
                { id: 'hora_servidor', titulo: 'Fecha', render: (m: any) => fechaHora(m.hora_servidor) },
                { id: 'tipo', titulo: 'Tipo', render: (m: any) => <Chip v={m.tipo} tipo /> },
                { id: 'origen', titulo: 'Origen', mono: true }, { id: 'destino', titulo: 'Destino', mono: true },
                { id: 'usuario', titulo: 'Operario' }, { id: 'estado', titulo: 'Estado', render: (m: any) => <Chip v={m.estado} /> },
              ]} />
          </div>
        </div>
      )}
      {r?.tipo === 'contenedor' && <Reimprimir key={'r' + r.contenedor.contenedor} contenedor={r.contenedor.contenedor} />}
      {r?.tipo === 'contenedor' && <Historias key={'c' + r.contenedor.contenedor} contenedor={r.contenedor.contenedor} articulo={r.contenedor.articulo} empresa={r.contenedor.empresa} />}
      {r?.tipo === 'articulo' && (
        <div className="tarjeta">
          <h3>Artículo <span className="mono">{r.articulo.articulo}</span> · {r.articulo.descripcion} · {r.articulo.empresaNombre ?? r.articulo.cliente}</h3>
          <Fotos urls={r.articulo.fotos.map((f: any) => f.url)} />
          <div><b>{r.articulo.contenedores.length}</b> contenedores · total <b>{r.articulo.total}</b>
            {r.articulo.contenedores.some((c: any) => c.disponible === false) && <span style={{ color: 'var(--error)', fontWeight: 600 }}> · {r.articulo.contenedores.filter((c: any) => c.disponible === false).length} no disponible(s) según el WMS</span>}</div>
          <Tabla filas={r.articulo.contenedores} clave={(c: any) => c.contenedor + c.posicion} alDobleClic={(c: any) => { setTexto(c.contenedor); buscar(c.contenedor); }}
            columnas={[{ id: 'contenedor', titulo: 'Contenedor', mono: true }, { id: 'posicion', titulo: 'Posición', mono: true }, { id: 'cantidad', titulo: 'Cantidad', num: true },
              { id: 'estado', titulo: 'Estado (WMS)', valor: (c: any) => (c.disponible === false ? 0 : c.disponible ? 1 : 2), render: (c: any) => c.disponible === true
                ? <span className="chip aplicado">Disponible</span>
                : c.disponible === false ? <span className="chip error">No disponible · {c.estadoTexto ?? 'BLOQUEADO'}</span>
                : <span style={{ color: 'var(--tenue)' }}>{c.estadoTexto ?? ''}</span> }]} />
        </div>
      )}
      {r?.tipo === 'articulo' && <Historias key={'a' + r.articulo.articulo} articulo={r.articulo.articulo} empresa={r.articulo.empresa} />}
    </div>
  );
}

/** Historia del WMS oficial (contenedor o artículo, último año) e ingresos / egresos del artículo en el e-TMS. */
function Historias({ contenedor, articulo, empresa }: { contenedor?: string; articulo?: string; empresa?: any }) {
  const [vista, setVista] = useState<{ tipo: 'wms' | 'etms'; titulo: string; datos?: any; error?: string } | null>(null);
  const [cargando, setCargando] = useState(false);
  const wms = async (q: string, titulo: string) => {
    setCargando(true); setVista({ tipo: 'wms', titulo });
    try { setVista({ tipo: 'wms', titulo, datos: await get('/api/historia?' + q) }); } catch (e) { setVista({ tipo: 'wms', titulo, error: (e as Error).message }); } finally { setCargando(false); }
  };
  const etms = async () => {
    const titulo = `Ingresos / Egresos de ${articulo} (e-TMS)`;
    setCargando(true); setVista({ tipo: 'etms', titulo });
    try { setVista({ tipo: 'etms', titulo, datos: await get(`/api/ingresos-egresos/${encodeURIComponent(articulo!)}${empresa ? `?empresa=${empresa}` : ''}`) }); }
    catch (e) { setVista({ tipo: 'etms', titulo, error: (e as Error).message }); } finally { setCargando(false); }
  };
  const d = vista?.datos;
  return (
    <div className="tarjeta">
      <div className="acciones">
        {contenedor && <button className="boton primario" disabled={cargando} onClick={() => wms('contenedor=' + encodeURIComponent(contenedor), `Historia del contenedor ${contenedor} en el WMS (último año)`)}>📜 Historia del contenedor en el WMS</button>}
        {articulo && <button className={`boton ${contenedor ? '' : 'primario'}`} disabled={cargando} onClick={() => wms('articulo=' + encodeURIComponent(articulo), `Historia del artículo ${articulo} en el WMS (último año)`)}>📜 Historia del artículo {articulo} en el WMS</button>}
        {articulo && <button className="boton azul" disabled={cargando} onClick={etms}>📦 Ingresos / Egresos de {articulo} (e-TMS)</button>}
      </div>
      {vista && <h3 style={{ marginTop: 6 }}>{vista.titulo}</h3>}
      {cargando && <div style={{ color: 'var(--tenue)' }}>{vista?.tipo === 'wms' ? 'Pidiendo la historia al WMS (puede tardar unos segundos)…' : 'Consultando el e-TMS…'}</div>}
      {vista?.error && <Aviso tipo="error">{vista.error}</Aviso>}
      {vista?.tipo === 'wms' && d && (() => {
        const cols: string[] = d.columnas ?? [];
        const filas: any[] = (d.filas ?? []).map((f: any, i: number) => ({ ...f, __k: i }));
        if (!filas.length) return <div style={{ color: 'var(--tenue)' }}>El WMS no informa movimientos en el último año.</div>;
        return (
          <>
            <div style={{ color: 'var(--tenue)' }}>{filas.length} movimiento(s)</div>
            <div style={{ maxHeight: 440, display: 'flex' }}>
              <Tabla filas={filas} clave={(f: any) => String(f.__k)}
                columnas={cols.map((n) => ({ id: n, titulo: n, num: /cantidad/i.test(n), mono: /^(Contenedor|Ubicación)$/i.test(n), valor: (f: any) => f[n] }))} />
            </div>
          </>
        );
      })()}
      {vista?.tipo === 'etms' && d && (
        <>
          <div><b>{d.articulo}</b> · {d.descripcion}</div>
          <div className="grilla-kpi" style={{ gridTemplateColumns: 'repeat(3, minmax(120px, 200px))' }}>
            <div className="kpi verde"><div className="v">{d.ingresado ?? '–'}</div><div className="t">Ingresado</div></div>
            <div className="kpi rojo"><div className="v">{d.egresado ?? '–'}</div><div className="t">Egresado</div></div>
            <div className="kpi"><div className="v">{d.saldo ?? '–'}</div><div className="t">Saldo</div></div>
          </div>
          <div style={{ maxHeight: 440, display: 'flex' }}>
            <Tabla filas={(d.movimientos ?? []).map((m: any, i: number) => ({ ...m, __k: i }))} clave={(m: any) => String(m.__k)} vacio="Sin ingresos ni egresos en el e-TMS"
              columnas={[
                { id: 'fecha', titulo: 'Fecha' }, { id: 'tipo', titulo: 'Movimiento' },
                { id: 'entrada', titulo: 'Entrada', num: true }, { id: 'salida', titulo: 'Salida', num: true },
                { id: 'pdv', titulo: 'PDV' }, { id: 'codigoPdv', titulo: 'Cód. PDV', mono: true }, { id: 'remito', titulo: 'Remito', mono: true },
                { id: 'solicitud', titulo: 'Solicitud', mono: true }, { id: 'estado', titulo: 'Estado' }, { id: 'referencia', titulo: 'Referencia' },
              ]} />
          </div>
        </>
      )}
    </div>
  );
}
