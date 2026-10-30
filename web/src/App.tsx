import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType } from 'react';
import { alConexion, alEvento, alCambiarSesion, conectarEventos, estaConectado, get, ingresar, salir, sesion, puede, refrescarPermisos, ErrorApi } from './api';
import { CtxAbrir, CtxMensaje, useMensajeEstado } from './comp';
import Monitor from './tx/Monitor';
import Movimientos from './tx/Movimientos';
import Autorizaciones from './tx/Autorizaciones';
import Consulta from './tx/Consulta';
import Posiciones from './tx/Posiciones';
import Zonas from './tx/Zonas';
import Mapa from './tx/Mapa';
import Conteos from './tx/Conteos';
import Diferencias from './tx/Diferencias';
import Usuarios from './tx/Usuarios';
import Parametros from './tx/Parametros';
import Empresas from './tx/Empresas';
import Auditoria from './tx/Auditoria';
import Estado from './tx/Estado';
import Impresoras from './tx/Impresoras';
import Articulos from './tx/Articulos';
import Credenciales from './tx/Credenciales';
import { Transferir, Guardar, ReubicarCajas, Ajustar, CambiarEstado, CrearContenedor, EnviarNn, Eliminar } from './tx/Operaciones';

export interface PropsTx { parametros?: Record<string, any> }
interface Tx { codigo: string; nombre: string; comp: ComponentType<PropsTx>; permiso?: string }

// permiso = el de ZUSR que habilita la transacción (si no se indica, el permiso es el mismo código)
const TX: Tx[] = [
  { codigo: 'ZMON', nombre: 'Monitor del depósito', comp: Monitor },
  { codigo: 'ZMOV', nombre: 'Movimientos', comp: Movimientos },
  { codigo: 'ZAUT', nombre: 'Autorizaciones', comp: Autorizaciones, permiso: 'autorizar' },
  { codigo: 'ZCON', nombre: 'Consultar', comp: Consulta, permiso: 'consultar' },
  { codigo: 'ZTRF', nombre: 'Transferir', comp: Transferir, permiso: 'transferir' },
  { codigo: 'ZGUA', nombre: 'Guardar recepciones', comp: Guardar, permiso: 'guardar' },
  { codigo: 'ZCAJ', nombre: 'Reubicar cajas', comp: ReubicarCajas, permiso: 'reubicar_cajas' },
  { codigo: 'ZAJU', nombre: 'Ajustar contenedor', comp: Ajustar, permiso: 'ajustar' },
  { codigo: 'ZCES', nombre: 'Cambiar estado', comp: CambiarEstado, permiso: 'cambiar_estado' },
  { codigo: 'ZALT', nombre: 'Crear contenedor', comp: CrearContenedor, permiso: 'crear_contenedor' },
  { codigo: 'ZNN', nombre: 'Enviar a NN (lote)', comp: EnviarNn, permiso: 'enviar_nn' },
  { codigo: 'ZELI', nombre: 'Eliminar contenedor', comp: Eliminar, permiso: 'eliminar' },
  { codigo: 'ZART', nombre: 'Artículos por empresa', comp: Articulos },
  { codigo: 'ZPOS', nombre: 'Fichas de posiciones', comp: Posiciones },
  { codigo: 'ZZON', nombre: 'Zonas · Layout Objetivo', comp: Zonas },
  { codigo: 'ZMAP', nombre: 'Mapa de pasillo', comp: Mapa },
  { codigo: 'ZCNT', nombre: 'Tareas de conteo', comp: Conteos },
  { codigo: 'ZDIF', nombre: 'Conteos y diferencias', comp: Diferencias },
  { codigo: 'ZUSR', nombre: 'Usuarios, perfiles y permisos', comp: Usuarios },
  { codigo: 'ZPAR', nombre: 'Parámetros', comp: Parametros },
  { codigo: 'ZEMP', nombre: 'Empresas', comp: Empresas },
  { codigo: 'ZAUD', nombre: 'Auditoría de cambios', comp: Auditoria },
  { codigo: 'ZEST', nombre: 'Estado y conexiones', comp: Estado },
  { codigo: 'ZIMP', nombre: 'Impresoras de etiquetas', comp: Impresoras },
  { codigo: 'ZCRE', nombre: 'Mis credenciales', comp: Credenciales, permiso: '*' },
];
const porCodigo = new Map(TX.map((t) => [t.codigo, t]));
const habilitada = (c: string) => { const t = porCodigo.get(c); return !!t && (t.permiso === '*' || puede(t.permiso ?? t.codigo)); };

// Otras páginas web del mismo servidor (se abren en otra pestaña del navegador)
const WEB: Record<string, { nombre: string; url: string; permiso: string }> = {
  '@PROD': { nombre: 'Panel de producción', url: '/produccion', permiso: 'produccion_ver' },
  '@ING': { nombre: 'Módulo de ingreso', url: '/ingresos', permiso: 'ingreso' },
};

// Mismo orden que el panel de escritorio
const MENU_TODO: { titulo: string; items: string[] }[] = [
  { titulo: 'Monitoreo', items: ['ZMON', 'ZMOV', 'ZAUT'] },
  { titulo: 'Movimientos', items: ['ZCON', 'ZART', '-', 'ZTRF', 'ZGUA', 'ZCAJ', 'ZAJU', 'ZCES', 'ZALT', 'ZNN', '-', 'ZELI'] },
  { titulo: 'Pedidos e ingresos', items: ['@PROD', '@ING'] },
  { titulo: 'Ubicaciones', items: ['ZPOS', 'ZZON', 'ZMAP'] },
  { titulo: 'Inventario', items: ['ZCNT', 'ZDIF'] },
  { titulo: 'Administración', items: ['ZUSR', 'ZPAR', 'ZEMP', '-', 'ZAUD'] },
  { titulo: 'Sistema', items: ['ZEST', 'ZIMP', '-', 'ZCRE'] },
];
/** El menú con solo lo que el usuario tiene permitido (sin separadores sueltos ni carpetas vacías). */
function menuPermitido() {
  return MENU_TODO.map((m) => {
    const it = m.items.filter((c) => c === '-' || (c.startsWith('@') ? puede(WEB[c].permiso) : habilitada(c)));
    const limpio = it.filter((c, i) => c !== '-' || (i > 0 && i < it.length - 1 && it[i - 1] !== '-'));
    return { titulo: m.titulo, items: limpio };
  }).filter((m) => m.items.some((c) => c !== '-'));
}
const nombreItem = (c: string) => (c.startsWith('@') ? WEB[c].nombre : porCodigo.get(c)!.nombre);

const ROL: Record<string, string> = { maquinista: 'Maquinista', conteo: 'Conteo', picker: 'Picker', supervisor: 'Supervisor', produccion: 'Producción', analista: 'Analista', admin: 'Administrador' };

interface Pestana { id: number; codigo: string; parametros?: Record<string, any> }

export default function App() {
  const [, forzar] = useState(0);
  useEffect(() => alCambiarSesion(() => forzar((x) => x + 1)), []);
  return sesion().token ? <Marco /> : <Login />;
}

function Login() {
  const g = sesion();
  const [usuario, setUsuario] = useState(g.usuario ?? '');
  const [clave, setClave] = useState('');
  const [recordar, setRecordar] = useState(g.recordar ?? true);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const enviar = async (e: React.FormEvent) => {
    e.preventDefault();
    setCargando(true); setError(null);
    try { await ingresar(usuario, clave, recordar); } catch (err) { setError((err as ErrorApi).message); } finally { setCargando(false); }
  };
  return (
    <div className="login">
      <form onSubmit={enviar}>
        <img src="/sona-logo.svg" alt="SONA" />
        <h1>Depósito · Panel de control</h1>
        <div className="campo"><label>Usuario (el del WMS)</label><input autoFocus value={usuario} onChange={(e) => setUsuario(e.target.value.toUpperCase())} /></div>
        <div className="campo"><label>Clave</label><input type="password" value={clave} onChange={(e) => setClave(e.target.value)} /></div>
        <label className="chk"><input type="checkbox" checked={recordar} onChange={(e) => setRecordar(e.target.checked)} /> Mantener la sesión en este equipo (reconecta solo)</label>
        {error && <div className="aviso error">{error}</div>}
        <button className="boton primario" disabled={cargando || !usuario || !clave}>{cargando ? 'Validando con el WMS…' : 'Ingresar'}</button>
      </form>
    </div>
  );
}

function Marco() {
  const s = sesion();
  const MENU = menuPermitido();
  const inicial = ['ZMON', 'ZCON', ...TX.map((t) => t.codigo)].find(habilitada);
  const [pestanas, setPestanas] = useState<Pestana[]>(inicial ? [{ id: 1, codigo: inicial }] : []);
  const [activa, setActiva] = useState(1);
  const sig = useRef(2);
  const [menuAbierto, setMenuAbierto] = useState<string | null>(null);
  const [tx, setTx] = useState('');
  const [arbol, setArbol] = useState(true);
  const [carpetas, setCarpetas] = useState<Set<string>>(new Set(MENU_TODO.map((m) => m.titulo)));
  const abrirItem = (c: string) => { if (c.startsWith('@')) { window.open(WEB[c].url, '_blank'); setMenuAbierto(null); } else abrir(c); };
  const [msg, mostrar] = useMensajeEstado();
  const [conectado, setConectado] = useState(estaConectado());
  const [estado, setEstado] = useState<any>(null);
  const [pendAut, setPendAut] = useState(0);
  const [hora, setHora] = useState(new Date());

  const abrir = useCallback((codigo: string, parametros?: Record<string, any>) => {
    const c = codigo.toUpperCase().replace(/^\/[NO]/, '').trim();
    if (!porCodigo.has(c)) { mostrar(`La transacción ${c} no existe`, 'error'); return; }
    if (!habilitada(c)) { mostrar(`No tenés permiso para ${c} · ${porCodigo.get(c)!.nombre}. Pedíselo a un administrador (ZUSR).`, 'error'); return; }
    const nueva = codigo.toUpperCase().startsWith('/O');
    setPestanas((ps) => {
      const ya = ps.find((p) => p.codigo === c);
      if (ya && !nueva && !parametros) { setActiva(ya.id); return ps; }
      if (ya && !nueva && parametros) {
        const id = sig.current++;          // id nuevo = la pantalla se vuelve a montar con los parámetros
        setActiva(id);
        return ps.map((p) => (p.id === ya.id ? { id, codigo: c, parametros } : p));
      }
      const id = sig.current++;
      setActiva(id);
      return [...ps, { id, codigo: c, parametros }];
    });
    setMenuAbierto(null);
  }, [mostrar]);

  const cerrar = (id: number) => setPestanas((ps) => {
    const r = ps.filter((p) => p.id !== id);
    if (id === activa && r.length) setActiva(r[r.length - 1].id);
    return r;
  });

  useEffect(() => { conectarEventos(); return alConexion(setConectado); }, []);
  // si un administrador cambia los permisos, el menú se actualiza solo
  useEffect(() => { refrescarPermisos(); const t = setInterval(refrescarPermisos, 60000); return () => clearInterval(t); }, []);
  useEffect(() => { setPestanas((ps) => ps.filter((p) => habilitada(p.codigo))); }, [s.permisos?.join(), s.rol]);
  useEffect(() => {
    const leer = () => {
      get('/api/estado').then(setEstado).catch(() => setEstado(null));
      if (puede('autorizar') || puede('ZMOV')) get('/api/autorizaciones?estado=pendiente').then((a: any[]) => setPendAut(a.length)).catch(() => undefined);
    };
    leer();
    const t = setInterval(leer, 15000);
    const r = setInterval(() => setHora(new Date()), 1000);
    const off = alEvento((e) => {
      if (e.tipo === 'autorizacion') {
        leer();
        if (e.estado === 'pendiente') {
          mostrar(`Pedido de autorización de ${e.usuario}: ${e.motivo}`, 'info');
          try { new Audio('data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=').play().catch(() => undefined); } catch { /* */ }
          if ('Notification' in window && Notification.permission === 'granted') new Notification('SONA Depósito', { body: `Autorización: ${e.motivo}` });
        }
      }
      if (e.tipo === 'movimiento' && e.estado === 'error') mostrar(`Error WMS en ${e.contenedor}: ${e.mensaje}`, 'error');
      if (e.tipo === 'movimiento') leer();
    });
    if ('Notification' in window && Notification.permission === 'default') Notification.requestPermission().catch(() => undefined);
    return () => { clearInterval(t); clearInterval(r); off(); };
  }, [mostrar]);

  useEffect(() => {
    const k = (e: KeyboardEvent) => {
      if (e.key === 'F3') { e.preventDefault(); if (pestanas.length > 1) cerrar(activa); }
      if (e.ctrlKey && e.key.toLowerCase() === 'k') { e.preventDefault(); document.getElementById('campo-tx')?.focus(); }
    };
    const c = () => setMenuAbierto(null);
    window.addEventListener('keydown', k);
    window.addEventListener('click', c);
    return () => { window.removeEventListener('keydown', k); window.removeEventListener('click', c); };
  });

  const actual = pestanas.find((p) => p.id === activa);
  const insignias: Record<string, number> = useMemo(() => ({ ZAUT: pendAut, ZMOV: estado?.pendientesWms ?? 0 }), [pendAut, estado]);

  return (
    <CtxMensaje.Provider value={mostrar}>
      <CtxAbrir.Provider value={abrir}>
        <div className="marco">
          <div className="titulo">
            <img src="/sona-logo-blanco.svg" alt="SONA" />
            <span className="nombre">Depósito<small>Panel de control</small></span>
            <span className="espacio" />
            <span className="usuario"><span className="avatar">{(s.usuario ?? '?').slice(0, 2)}</span>{s.nombre || s.usuario} · {ROL[s.rol ?? ''] ?? s.rol}</span>
            <button onClick={() => abrir('ZCRE')} title="Tus usuarios y claves del LES, e-TMS…">🔑 Mis credenciales</button>
            <button onClick={salir}>Salir</button>
          </div>

          <div className="menubar" onClick={(e) => e.stopPropagation()}>
            {MENU.map((m) => (
              <div key={m.titulo} className={`item ${menuAbierto === m.titulo ? 'abierto' : ''}`}
                onClick={() => setMenuAbierto((x) => (x === m.titulo ? null : m.titulo))}
                onMouseEnter={() => menuAbierto && setMenuAbierto(m.titulo)}>
                {m.titulo}
                {menuAbierto === m.titulo && (
                  <div className="desplegable">
                    {m.items.map((c, i) => c === '-' ? <hr key={i} /> : (
                      <div key={c} className={`op ${c.startsWith('@') ? 'externo' : ''}`} onClick={(e) => { e.stopPropagation(); abrirItem(c); }}>
                        <span>{nombreItem(c)}</span><code>{c.startsWith('@') ? WEB[c].url : c}</code>
                      </div>
                    ))}
                    {m.titulo === 'Sistema' && <><hr /><div className="op" onClick={salir}><span>Cerrar sesión</span></div></>}
                  </div>
                )}
              </div>
            ))}
            <div className={`item ${menuAbierto === 'Ayuda' ? 'abierto' : ''}`} onClick={() => setMenuAbierto((x) => (x === 'Ayuda' ? null : 'Ayuda'))}>
              Ayuda
              {menuAbierto === 'Ayuda' && (
                <div className="desplegable" style={{ padding: 10, lineHeight: 1.7 }}>
                  <b>Atajos</b><br />Ctrl+K: código de transacción<br />F3: cerrar pestaña<br />/N + código: abre en la pestaña<br />/O + código: abre en pestaña nueva
                </div>
              )}
            </div>
          </div>

          <div className="herramientas">
            <button className="btn-ico" title="Mostrar/ocultar árbol" onClick={() => setArbol((a) => !a)}>☰</button>
            <form className="tx" onSubmit={(e) => { e.preventDefault(); if (tx.trim()) abrir(tx.trim()); setTx(''); }}>
              <span>Transacción</span>
              <input id="campo-tx" value={tx} onChange={(e) => setTx(e.target.value)} placeholder="ZMOV" list="lista-tx" />
              <datalist id="lista-tx">{TX.filter((t) => habilitada(t.codigo)).map((t) => <option key={t.codigo} value={t.codigo}>{t.nombre}</option>)}</datalist>
            </form>
            <span className="sep" />
            {habilitada('ZMON') && <button className="btn-ico" onClick={() => abrir('ZMON')}>📊 Monitor</button>}
            {habilitada('ZMOV') && <button className="btn-ico" onClick={() => abrir('ZMOV')}>⇄ Movimientos</button>}
            {habilitada('ZAUT') && <button className="btn-ico" onClick={() => abrir('ZAUT')}>✋ Autorizaciones{pendAut > 0 && <span className="chip esperando_autorizacion">{pendAut}</span>}</button>}
            {habilitada('ZCON') && <button className="btn-ico" onClick={() => abrir('ZCON')}>🔎 Consultar</button>}
            {habilitada('ZTRF') && <button className="btn-ico" onClick={() => abrir('ZTRF')}>➜ Transferir</button>}
            <span className="estado-ws"><span className={`punto ${conectado ? 'ok' : 'mal'}`} />{conectado ? 'En vivo' : 'Reconectando…'}</span>
          </div>

          <div className={`cuerpo ${arbol ? '' : 'sin-arbol'}`}>
            <nav className="arbol">
              {MENU.map((m) => (
                <div key={m.titulo}>
                  <div className={`carpeta ${carpetas.has(m.titulo) ? 'abierta' : ''}`} onClick={() => setCarpetas((c) => { const n = new Set(c); n.has(m.titulo) ? n.delete(m.titulo) : n.add(m.titulo); return n; })}>
                    <span className="flecha">▶</span>{m.titulo}
                  </div>
                  {carpetas.has(m.titulo) && m.items.filter((c) => c !== '-').map((c) => (
                    <div key={c} className={`hoja ${actual?.codigo === c ? 'activa' : ''}`} onClick={() => abrirItem(c)}>
                      <span>{nombreItem(c)}{c.startsWith('@') ? ' ↗' : ''}</span>
                      {insignias[c] ? <span className="insignia">{insignias[c]}</span> : <code>{c.startsWith('@') ? '' : c}</code>}
                    </div>
                  ))}
                </div>
              ))}
            </nav>
            <div className="trabajo">
              <div className="pestanas">
                {pestanas.map((p) => (
                  <div key={p.id} className={`pestana ${p.id === activa ? 'activa' : ''}`} onClick={() => setActiva(p.id)}>
                    <span>{porCodigo.get(p.codigo)!.nombre}</span>
                    {pestanas.length > 1 && <button className="x" onClick={(e) => { e.stopPropagation(); cerrar(p.id); }}>✕</button>}
                  </div>
                ))}
              </div>
              <div className="contenido">
                {!pestanas.length && <div className="vacio">Tu usuario no tiene transacciones habilitadas en el panel. Pedíselas a un administrador (ZUSR › Permisos).</div>}
                {pestanas.map((p) => {
                  const C = porCodigo.get(p.codigo)!.comp;
                  return <div key={p.id} style={{ display: p.id === activa ? 'block' : 'none', height: '100%' }}><C parametros={p.parametros} /></div>;
                })}
              </div>
            </div>
          </div>

          <div className="estado-barra">
            <span><span className={`punto ${estado ? 'ok' : 'mal'}`} /> Servidor {estado ? <b>v{estado.version}</b> : 'sin conexión'}</span>
            <span>Pendientes WMS: <b>{estado?.pendientesWms ?? '–'}</b></span>
            <span>Sesiones WMS: <b>{estado?.sesionesWms?.length ?? '–'}</b></span>
            <span>Conectados: <b>{estado?.conectados ?? '–'}</b></span>
            {msg && <span className={`msg-${msg.tipo}`}>{msg.tipo === 'error' ? '⛔' : msg.tipo === 'ok' ? '✔' : 'ℹ'} {msg.texto}</span>}
            <span className="der">{actual?.codigo} · {hora.toLocaleString('es-AR')}</span>
          </div>
        </div>
      </CtxAbrir.Provider>
    </CtxMensaje.Provider>
  );
}
