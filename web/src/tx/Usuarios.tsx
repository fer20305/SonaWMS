import { Fragment, useMemo, useState } from 'react';
import { get, put, borrar, fechaHora, sesion, refrescarPermisos } from '../api';
import { Aviso, Campo, Modal, Tabla, useDatos, useMensaje } from '../comp';

const ROLES: Record<string, string> = {
  maquinista: 'Maquinista', conteo: 'Asistente de conteo', picker: 'Picker', supervisor: 'Supervisor (solo ve)',
  produccion: 'Producción (asigna pedidos)', analista: 'Analista', admin: 'Administrador',
};

interface Permiso { codigo: string; nombre: string; grupo: string }
interface Perfil { codigo: string; nombre: string; descripcion?: string; permisos: string[]; sistema: boolean }
interface DatosPermisos { catalogo: Permiso[]; perfiles: Perfil[] }

const porGrupo = (cat: Permiso[]) => {
  const m = new Map<string, Permiso[]>();
  for (const p of cat) m.set(p.grupo, [...(m.get(p.grupo) ?? []), p]);
  return [...m.entries()];
};

export default function Usuarios() {
  const [vista, setVista] = useState<'usuarios' | 'perfiles'>('usuarios');
  const perm = useDatos<DatosPermisos>(() => get('/api/permisos'), []);
  return (
    <div className="pantalla">
      <h2>Usuarios, perfiles y permisos <code>ZUSR</code></h2>
      <div className="pestanas-int">
        <button className={vista === 'usuarios' ? 'activa' : ''} onClick={() => setVista('usuarios')}>Usuarios</button>
        <button className={vista === 'perfiles' ? 'activa' : ''} onClick={() => setVista('perfiles')}>Perfiles</button>
      </div>
      {perm.error && <Aviso tipo="amarillo">No se pudieron leer los permisos: {perm.error}</Aviso>}
      {vista === 'usuarios' ? <ListaUsuarios perm={perm.datos} /> : <ListaPerfiles perm={perm.datos} recargar={perm.recargar} />}
    </div>
  );
}

// ------------------------------------------------------------------ usuarios
function ListaUsuarios({ perm }: { perm: DatosPermisos | null }) {
  const { datos, error, recargar } = useDatos(() => get('/api/usuarios'), []);
  const [editar, setEditar] = useState<any>(null);
  const [permisos, setPermisos] = useState<any>(null);
  const admin = sesion().rol === 'admin';
  return (
    <>
      <Aviso tipo="info">
        El usuario y la clave son los del WMS. Cada usuario tiene uno o más <b>perfiles</b> (Maquinista, Picker, Analista… o los que armes en la pestaña Perfiles)
        y, si hace falta, permisos <b>agregados</b> o <b>quitados</b> solo para él. En la app, el panel y los paneles web cada uno ve solo las opciones que tiene permitidas.
      </Aviso>
      <div className="acciones">
        {admin && <button className="boton primario" onClick={() => setEditar({ usuario: '', nombre: '', rol: 'maquinista', activo: true, nuevo: true })}>＋ Dar de alta</button>}
        <div className="der"><button className="boton" onClick={recargar}>↻</button></div>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tabla filas={datos ?? []} clave={(u: any) => u.usuario} alDobleClic={(u: any) => admin && setPermisos(u)}
        columnas={[
          { id: 'usuario', titulo: 'Usuario', mono: true, render: (u: any) => <b>{u.usuario}</b> },
          { id: 'nombre', titulo: 'Nombre' },
          { id: 'rol', titulo: 'Perfil principal', render: (u: any) => ROLES[u.rol] ?? perm?.perfiles.find((p) => p.codigo === u.rol)?.nombre ?? u.rol },
          { id: 'perfiles', titulo: 'Perfiles / excepciones', render: (u: any) => resumenPermisos(u, perm) },
          { id: 'activo', titulo: 'Activo', render: (u: any) => (u.activo ? '✔' : '✕') },
          { id: 'clave_guardada', titulo: 'Puede operar WMS', render: (u: any) => (u.clave_guardada ? '✔' : 'Falta ingresar') },
          { id: 'ultimo_acceso', titulo: 'Último ingreso', render: (u: any) => fechaHora(u.ultimo_acceso) },
          { id: 'ultimo_contacto', titulo: 'Último contacto celular', render: (u: any) => fechaHora(u.ultimo_contacto) },
          ...(admin ? [{
            id: 'acc', titulo: '', render: (u: any) => (
              <span style={{ display: 'flex', gap: 4 }}>
                <button className="boton" style={{ height: 24 }} onClick={() => setEditar(u)}>Datos</button>
                <button className="boton azul" style={{ height: 24 }} onClick={() => setPermisos(u)}>🔐 Permisos</button>
              </span>
            ),
          }] : []),
        ]} />
      {editar && <EditarUsuario u={editar} perm={perm} cerrar={() => setEditar(null)} listo={() => { setEditar(null); recargar(); }} />}
      {permisos && perm && <EditarPermisos u={permisos} perm={perm} cerrar={() => setPermisos(null)} listo={() => { setPermisos(null); recargar(); }} />}
    </>
  );
}

function resumenPermisos(u: any, perm: DatosPermisos | null) {
  const lista = (s: any): string[] => { if (Array.isArray(s)) return s; try { return s ? JSON.parse(s) : []; } catch { return []; } };
  const perfiles = lista(u.perfiles); const mas = lista(u.permisos_mas); const menos = lista(u.permisos_menos);
  const nom = (c: string) => perm?.perfiles.find((p) => p.codigo === c)?.nombre ?? ROLES[c] ?? c;
  const partes = [perfiles.length > 1 ? perfiles.map(nom).join(' + ') : ''];
  if (mas.length) partes.push(`+${mas.length}`);
  if (menos.length) partes.push(`−${menos.length}`);
  return partes.filter(Boolean).join(' · ');
}

function EditarUsuario({ u, perm, cerrar, listo }: { u: any; perm: DatosPermisos | null; cerrar: () => void; listo: () => void }) {
  const [v, setV] = useState({ usuario: u.usuario, nombre: u.nombre ?? '', rol: u.rol, activo: u.activo });
  const [error, setError] = useState<string | null>(null);
  const mostrar = useMensaje();
  const opciones = perm?.perfiles.filter((p) => p.sistema).map((p) => [p.codigo, p.nombre] as const) ?? Object.entries(ROLES);
  return (
    <Modal titulo={u.nuevo ? 'Alta de usuario' : `Usuario ${u.usuario}`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" disabled={!v.usuario} onClick={async () => {
        try { await put(`/api/usuarios/${encodeURIComponent(v.usuario)}`, { nombre: v.nombre || null, rol: v.rol, activo: v.activo }); mostrar('Usuario guardado', 'ok'); listo(); } catch (e) { setError((e as Error).message); }
      }}>Guardar</button></>}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, minWidth: 440 }}>
        <Campo etiqueta="Usuario del WMS"><input value={v.usuario} disabled={!u.nuevo} onChange={(e) => setV({ ...v, usuario: e.target.value.toUpperCase() })} /></Campo>
        <Campo etiqueta="Nombre y apellido"><input value={v.nombre} onChange={(e) => setV({ ...v, nombre: e.target.value })} /></Campo>
        <Campo etiqueta="Perfil principal"><select value={v.rol} onChange={(e) => setV({ ...v, rol: e.target.value })}>{opciones.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></Campo>
        <Campo etiqueta="Estado"><select value={v.activo ? 'si' : 'no'} onChange={(e) => setV({ ...v, activo: e.target.value === 'si' })}><option value="si">Activo</option><option value="no">Inactivo</option></select></Campo>
      </div>
      <div style={{ color: 'var(--tenue)', marginTop: 8 }}>Para varios perfiles o permisos sueltos usá el botón 🔐 Permisos.</div>
      {error && <Aviso tipo="error">{error}</Aviso>}
    </Modal>
  );
}

/** Permisos de un usuario: perfiles (tildes) + por cada permiso, lo que dan los perfiles y la excepción (agregar / quitar). */
function EditarPermisos({ u, perm, cerrar, listo }: { u: any; perm: DatosPermisos; cerrar: () => void; listo: () => void }) {
  const actual = useDatos(() => get(`/api/usuarios/${encodeURIComponent(u.usuario)}/permisos`), [u.usuario]);
  const [perfiles, setPerfiles] = useState<string[] | null>(null);
  const [mas, setMas] = useState<string[] | null>(null);
  const [menos, setMenos] = useState<string[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const mostrar = useMensaje();
  const pf: string[] = perfiles ?? actual.datos?.perfiles ?? [];
  const ms: string[] = mas ?? actual.datos?.mas ?? [];
  const mn: string[] = menos ?? actual.datos?.menos ?? [];
  const esAdmin = pf.includes('admin');
  const dePerfiles = useMemo(() => new Set(perm.perfiles.filter((p) => pf.includes(p.codigo)).flatMap((p) => p.permisos)), [perm, pf]);
  const efectivo = (c: string) => esAdmin || ((dePerfiles.has(c) || ms.includes(c)) && !mn.includes(c));
  const alternar = (c: string) => {
    const on = efectivo(c);
    const sinM = ms.filter((x) => x !== c), sinN = mn.filter((x) => x !== c);
    if (on) { setMas(sinM); setMenos(dePerfiles.has(c) ? [...sinN, c] : sinN); }
    else { setMenos(sinN); setMas(dePerfiles.has(c) ? sinM : [...sinM, c]); }
  };
  const guardar = async () => {
    try {
      if (!pf.length) { setError('Elegí al menos un perfil.'); return; }
      await put(`/api/usuarios/${encodeURIComponent(u.usuario)}/permisos`, { perfiles: pf, mas: ms, menos: mn });
      mostrar(`Permisos de ${u.usuario} guardados`, 'ok');
      if (u.usuario === sesion().usuario) refrescarPermisos();
      listo();
    } catch (e) { setError((e as Error).message); }
  };
  return (
    <Modal titulo={`Permisos de ${u.nombre || u.usuario}`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" disabled={!actual.datos} onClick={guardar}>Guardar</button></>}>
      <div style={{ width: 'min(860px, 90vw)', maxHeight: '70vh', overflow: 'auto' }}>
        {actual.error && <Aviso tipo="error">{actual.error}</Aviso>}
        <div className="permisos-grupo">Perfiles</div>
        <div className="permisos-grid">
          {perm.perfiles.map((p) => (
            <label key={p.codigo} title={p.descripcion}>
              <input type="checkbox" checked={pf.includes(p.codigo)} onChange={(e) => setPerfiles(e.target.checked ? [...pf, p.codigo] : pf.filter((x) => x !== p.codigo))} />
              {p.nombre}{!p.sistema && <span style={{ color: 'var(--tenue)' }}> (propio)</span>}
            </label>
          ))}
        </div>
        {esAdmin ? <Aviso tipo="info">Administrador: tiene todos los permisos (no se le pueden quitar).</Aviso> : (
          <>
            <div style={{ color: 'var(--tenue)', margin: '8px 0 0' }}>
              Tildá o destildá para hacer una excepción solo para este usuario: <span style={{ color: 'var(--ok)', fontWeight: 600 }}>verde = agregado</span>, <span style={{ color: 'var(--error)', textDecoration: 'line-through' }}>rojo = quitado</span>.
            </div>
            {porGrupo(perm.catalogo).map(([g, ps]) => (
              <Fragment key={g}>
                <div className="permisos-grupo">{g === 'Panel' ? 'Panel (transacciones)' : g}</div>
                <div className="permisos-grid">
                  {ps.map((p) => (
                    <label key={p.codigo} className={ms.includes(p.codigo) ? 'mas' : mn.includes(p.codigo) ? 'menos' : ''}>
                      <input type="checkbox" checked={efectivo(p.codigo)} onChange={() => alternar(p.codigo)} />{p.nombre}
                    </label>
                  ))}
                </div>
              </Fragment>
            ))}
          </>
        )}
        {error && <Aviso tipo="error">{error}</Aviso>}
      </div>
    </Modal>
  );
}

// ------------------------------------------------------------------ perfiles
function ListaPerfiles({ perm, recargar }: { perm: DatosPermisos | null; recargar: () => void }) {
  const [editar, setEditar] = useState<Perfil | null>(null);
  const admin = sesion().rol === 'admin';
  const mostrar = useMensaje();
  return (
    <>
      <Aviso tipo="info">
        Un perfil es un conjunto de permisos (como un grupo de Active Directory). Los perfiles del sistema son los roles de siempre; podés ajustarlos o crear los tuyos
        (por ejemplo "Jefe de turno"). El perfil Administrador tiene todo y no se modifica.
      </Aviso>
      <div className="acciones">
        {admin && <button className="boton primario" onClick={() => setEditar({ codigo: '', nombre: '', descripcion: '', permisos: [], sistema: false })}>＋ Nuevo perfil</button>}
        <div className="der"><button className="boton" onClick={recargar}>↻</button></div>
      </div>
      <Tabla filas={perm?.perfiles ?? []} clave={(p) => p.codigo} alDobleClic={(p) => admin && p.codigo !== 'admin' && setEditar(p)}
        columnas={[
          { id: 'nombre', titulo: 'Perfil', render: (p) => <b>{p.nombre}</b> },
          { id: 'codigo', titulo: 'Código', mono: true },
          { id: 'sistema', titulo: 'Tipo', render: (p) => (p.sistema ? 'Sistema' : 'Propio') },
          { id: 'permisos', titulo: 'Permisos', num: true, valor: (p) => p.permisos.length },
          { id: 'descripcion', titulo: 'Descripción' },
          ...(admin ? [{
            id: 'acc', titulo: '', render: (p: Perfil) => p.codigo === 'admin' ? '' : (
              <span style={{ display: 'flex', gap: 4 }}>
                <button className="boton" style={{ height: 24 }} onClick={() => setEditar(p)}>Editar</button>
                {!p.sistema && <button className="boton peligro" style={{ height: 24 }} onClick={async () => {
                  if (!confirm(`¿Borrar el perfil ${p.nombre}?`)) return;
                  try { await borrar(`/api/perfiles/${encodeURIComponent(p.codigo)}`); mostrar('Perfil borrado', 'ok'); recargar(); } catch (e) { mostrar((e as Error).message, 'error'); }
                }}>Borrar</button>}
              </span>
            ),
          }] : []),
        ]} />
      {editar && perm && <EditarPerfil p={editar} perm={perm} cerrar={() => setEditar(null)} listo={() => { setEditar(null); recargar(); refrescarPermisos(); }} />}
    </>
  );
}

function EditarPerfil({ p, perm, cerrar, listo }: { p: Perfil; perm: DatosPermisos; cerrar: () => void; listo: () => void }) {
  const nuevo = !p.codigo;
  const [v, setV] = useState({ codigo: p.codigo, nombre: p.nombre, descripcion: p.descripcion ?? '', permisos: p.permisos });
  const [error, setError] = useState<string | null>(null);
  const mostrar = useMensaje();
  const codigo = nuevo ? v.nombre.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '') : v.codigo;
  const guardar = async () => {
    try {
      if (!codigo || !v.nombre.trim()) { setError('Poné un nombre.'); return; }
      await put(`/api/perfiles/${encodeURIComponent(codigo)}`, { nombre: v.nombre.trim(), descripcion: v.descripcion || null, permisos: v.permisos });
      mostrar('Perfil guardado', 'ok'); listo();
    } catch (e) { setError((e as Error).message); }
  };
  const marcar = (cods: string[], on: boolean) => setV({ ...v, permisos: on ? [...new Set([...v.permisos, ...cods])] : v.permisos.filter((x) => !cods.includes(x)) });
  return (
    <Modal titulo={nuevo ? 'Nuevo perfil' : `Perfil ${p.nombre}`} cerrar={cerrar}
      pie={<><button className="boton" onClick={cerrar}>Cancelar</button><button className="boton primario" onClick={guardar}>Guardar</button></>}>
      <div style={{ width: 'min(860px, 90vw)', maxHeight: '70vh', overflow: 'auto' }}>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 10 }}>
          <Campo etiqueta={`Nombre${nuevo && codigo ? ` (código ${codigo})` : ''}`}><input autoFocus value={v.nombre} onChange={(e) => setV({ ...v, nombre: e.target.value })} placeholder="Jefe de turno" /></Campo>
          <Campo etiqueta="Descripción"><input value={v.descripcion} onChange={(e) => setV({ ...v, descripcion: e.target.value })} /></Campo>
        </div>
        {porGrupo(perm.catalogo).map(([g, ps]) => (
          <Fragment key={g}>
            <div className="permisos-grupo">
              {g === 'Panel' ? 'Panel (transacciones)' : g}{' '}
              <button className="boton" style={{ height: 22, fontSize: 11 }} onClick={() => marcar(ps.map((x) => x.codigo), true)}>todos</button>{' '}
              <button className="boton" style={{ height: 22, fontSize: 11 }} onClick={() => marcar(ps.map((x) => x.codigo), false)}>ninguno</button>
            </div>
            <div className="permisos-grid">
              {ps.map((x) => (
                <label key={x.codigo}><input type="checkbox" checked={v.permisos.includes(x.codigo)} onChange={(e) => marcar([x.codigo], e.target.checked)} />{x.nombre}</label>
              ))}
            </div>
          </Fragment>
        ))}
        {error && <Aviso tipo="error">{error}</Aviso>}
      </div>
    </Modal>
  );
}
