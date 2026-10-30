// Cliente del servidor. Guarda el token y, si el usuario lo pidió, las credenciales para reconectar solo.

const CLAVE = 'sona.panel';
interface Guardado { token?: string; usuario?: string; clave?: string; recordar?: boolean; nombre?: string; rol?: string; permisos?: string[] }

function leer(): Guardado {
  try { return JSON.parse(localStorage.getItem(CLAVE) ?? '{}'); } catch { return {}; }
}
function escribir(g: Guardado) {
  try { localStorage.setItem(CLAVE, JSON.stringify(g)); } catch { /* sin almacenamiento */ }
}
// La clave se guarda ofuscada (no es seguridad fuerte: el equipo del panel debe ser de confianza).
const ofuscar = (s: string) => btoa(unescape(encodeURIComponent([...s].reverse().join(''))));
const desofuscar = (s: string) => [...decodeURIComponent(escape(atob(s)))].reverse().join('');

let estado = leer();
export const sesion = () => estado;
const oyentes = new Set<() => void>();
export const alCambiarSesion = (f: () => void) => { oyentes.add(f); return () => { oyentes.delete(f); }; };
const avisar = () => oyentes.forEach((f) => f());

export class ErrorApi extends Error { constructor(msg: string, readonly estado: number) { super(msg); } }

export async function ingresar(usuario: string, clave: string, recordar: boolean) {
  const r = await fetch('/api/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ usuario, clave, panel: true }) });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new ErrorApi(j.error ?? 'No se pudo ingresar', r.status);
  estado = { token: j.token, usuario: j.usuario.usuario, nombre: j.usuario.nombre, rol: j.usuario.rol, permisos: j.permisos ?? j.usuario.permisos, recordar, clave: recordar ? ofuscar(clave) : undefined };
  escribir(estado);
  avisar();
  return j;
}

export function salir() {
  estado = {};
  escribir(estado);
  avisar();
}

let reconectando: Promise<boolean> | null = null;
/** Si la sesión venció y hay credenciales guardadas, vuelve a ingresar sin preguntar. */
async function reconectar(): Promise<boolean> {
  if (!estado.recordar || !estado.usuario || !estado.clave) return false;
  reconectando ??= ingresar(estado.usuario, desofuscar(estado.clave), true).then(() => true, () => false).finally(() => { reconectando = null; });
  return reconectando;
}

export async function api<T = any>(metodo: string, ruta: string, cuerpo?: unknown, reintento = true): Promise<T> {
  const r = await fetch(ruta, {
    method: metodo,
    headers: { ...(cuerpo !== undefined ? { 'content-type': 'application/json' } : {}), ...(estado.token ? { authorization: 'Bearer ' + estado.token } : {}) },
    body: cuerpo !== undefined ? JSON.stringify(cuerpo) : undefined,
  });
  if (r.status === 401 && reintento) {
    if (await reconectar()) return api(metodo, ruta, cuerpo, false);
    salir();
  }
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new ErrorApi(j.error ?? `Error ${r.status}`, r.status);
  return j as T;
}

/** ¿El usuario tiene este permiso? (ZUSR › permisos). Sin datos de permisos (servidor viejo) no se oculta nada. */
export const puede = (p: string) => estado.rol === 'admin' || !estado.permisos || estado.permisos.includes(p);

/** Vuelve a leer los permisos del servidor (si un administrador los cambió). */
export async function refrescarPermisos() {
  try {
    const yo = await get('/api/yo');
    if (Array.isArray(yo.permisos) && JSON.stringify(yo.permisos) !== JSON.stringify(estado.permisos) || yo.rol !== estado.rol) {
      estado = { ...estado, permisos: yo.permisos, rol: yo.rol, nombre: yo.nombre ?? estado.nombre };
      escribir(estado); avisar();
    }
  } catch { /* sin conexión */ }
}

/** Sube archivos (fotos, APK) como formulario. */
export async function subir<T = any>(ruta: string, datos: FormData): Promise<T> {
  const r = await fetch(ruta, { method: 'POST', headers: estado.token ? { authorization: 'Bearer ' + estado.token } : {}, body: datos });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new ErrorApi(j.error ?? `Error ${r.status}`, r.status);
  return j as T;
}

export const get = <T = any>(ruta: string) => api<T>('GET', ruta);
export const post = <T = any>(ruta: string, cuerpo?: unknown) => api<T>('POST', ruta, cuerpo ?? {});
export const put = <T = any>(ruta: string, cuerpo?: unknown) => api<T>('PUT', ruta, cuerpo ?? {});
export const borrar = <T = any>(ruta: string) => api<T>('DELETE', ruta);

/** Imprime desde ESTA computadora (sus impresoras): trae la página de impresión del servidor y la abre en un marco oculto
 *  que muestra el diálogo de impresión del navegador. */
export async function imprimirEnEstaPc(ruta: string) {
  const r = await fetch(ruta, { headers: { authorization: 'Bearer ' + (estado.token ?? '') } });
  const t = await r.text();
  if (!r.ok) { let m = 'Error ' + r.status; try { m = JSON.parse(t).error || m; } catch { /* texto */ } throw new ErrorApi(m, r.status); }
  document.getElementById('frImp')?.remove();
  const f = document.createElement('iframe');
  f.id = 'frImp'; f.title = 'Impresión';
  f.style.cssText = 'position:fixed;right:0;bottom:0;width:1px;height:1px;border:0;opacity:0';
  f.srcdoc = t;
  document.body.appendChild(f);
}

export async function descargar(ruta: string, nombre: string) {
  let r = await fetch(ruta, { headers: { authorization: 'Bearer ' + estado.token } });
  if (r.status === 401 && (await reconectar())) r = await fetch(ruta, { headers: { authorization: 'Bearer ' + estado.token } });
  if (!r.ok) throw new ErrorApi('No se pudo descargar', r.status);
  const url = URL.createObjectURL(await r.blob());
  const a = document.createElement('a');
  a.href = url; a.download = nombre; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

// ------------------------------------------------------------------ tiempo real
type Evento = { tipo: string; [k: string]: any };
const suscriptores = new Set<(e: Evento) => void>();
let ws: WebSocket | null = null;
let conectado = false;
const oyentesConexion = new Set<(c: boolean) => void>();
export const estaConectado = () => conectado;
export function alConexion(f: (c: boolean) => void) { oyentesConexion.add(f); return () => { oyentesConexion.delete(f); }; }
export function alEvento(f: (e: Evento) => void) { suscriptores.add(f); return () => { suscriptores.delete(f); }; }

export function conectarEventos() {
  if (ws || !estado.token) return;
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  ws = new WebSocket(`${proto}://${location.host}/api/eventos?token=${encodeURIComponent(estado.token)}`);
  ws.onopen = () => { conectado = true; oyentesConexion.forEach((f) => f(true)); };
  ws.onmessage = (m) => { try { const e = JSON.parse(m.data); suscriptores.forEach((f) => f(e)); } catch { /* */ } };
  ws.onclose = () => {
    ws = null; conectado = false; oyentesConexion.forEach((f) => f(false));
    setTimeout(async () => { if (estado.token) { await get('/api/yo').catch(() => undefined); conectarEventos(); } }, 3000);
  };
}

// ------------------------------------------------------------------ formatos
export const fechaHora = (s?: string | null) => (s ? new Date(s).toLocaleString('es-AR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '');
export const hoyISO = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
export const urlFoto = (id: string) => `/api/fotos/${encodeURIComponent(id)}`;
